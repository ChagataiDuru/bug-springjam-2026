using PurrNet;
using Taiyun.SuckTheWater.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Card 5: Host-authoritative networked state sync for enemy/creature objects.
    ///
    /// Architecture
    /// ────────────
    /// Host owns: AI execution, knockback physics, stun timing, position/rotation SyncVars.
    /// Clients:   AI (EnemyMobile) and NavMeshAgent disabled on spawn; interpolate from SyncVars.
    ///
    /// Authoritative hit-reaction call flow
    /// ─────────────────────────────────────
    ///   AuthoritativeDamageResolver (host)
    ///     → ApplyAuthoritativeHitReaction()
    ///     → ApplyKnockback() / ApplyStun()   (host-only NavAgent + transform ops)
    ///     → IEnemyHitReactionHost callbacks  (EnemyController)
    ///     → SyncVars propagate stun to clients
    ///     → NotifyObserversHitReaction RPC   (presentation-only on observer clients)
    ///
    /// Assembly boundary
    /// ─────────────────
    /// This file lives in the Gameplay assembly.  EnemyController (AI assembly) is reached
    /// exclusively through IEnemyHitReactionHost (Game assembly), avoiding a circular dependency.
    /// EnemyMobile is disabled by string-name lookup to avoid the same issue.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NetworkedEnemySync : NetworkBehaviour
    {
        // ── SyncVars ──────────────────────────────────────────────────────────────
        // Host writes; all connected clients read automatically via PurrNet replication.

        /// <summary>Whether this creature is currently stunned. Host is the authority.</summary>
        public SyncVar<bool> IsStunned = new SyncVar<bool>(false);

        /// <summary>Server-local Time.time value when the current stun expires.</summary>
        public SyncVar<float> StunEndServerTime = new SyncVar<float>(0f);

        /// <summary>Host-authoritative world position, snapshotted at <see cref="_positionSyncRate"/> Hz.</summary>
        public SyncVar<Vector3> SyncedPosition = new SyncVar<Vector3>(Vector3.zero);

        /// <summary>Host-authoritative world rotation, snapshotted alongside position.</summary>
        public SyncVar<Quaternion> SyncedRotation = new SyncVar<Quaternion>(Quaternion.identity);

        // ── Inspector settings ────────────────────────────────────────────────────

        [Header("Position Sync")]
        [Tooltip("Position/rotation snapshots per second sent to clients.")]
        [SerializeField] float _positionSyncRate = 10f;

        [Tooltip("Client-side lerp speed toward synced transform.")]
        [SerializeField] float _interpolationSpeed = 14f;

        [Tooltip("Distance above which clients snap instead of interpolating.")]
        [SerializeField] float _snapThreshold = 4f;

        [Header("Knockback")]
        [Tooltip("Seconds the knockback displacement lasts before AI resumes.")]
        [SerializeField] float _knockbackDuration = 0.25f;

        [Tooltip("Max knockback speed in m/s (clamps incoming strength).")]
        [SerializeField] float _knockbackMaxSpeed = 18f;

        // ── Internal state ────────────────────────────────────────────────────────

        IEnemyHitReactionHost _hitReactionHost;
        NavMeshAgent _navAgent;
        MonoBehaviour _enemyMobile; // Held as MonoBehaviour to avoid AI assembly reference

        bool _isInitialized;

        // Host-only knockback state
        bool _isKnockedBack;
        Vector3 _knockbackVelocity;
        float _knockbackEndTime;

        // Client-side stun state mirror (drives OnHitReactionStunBegin/Expired callbacks)
        bool _lastObservedStunState;

        float _lastPositionSyncTime;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);

            // Standard host double-spawn guard used throughout this codebase
            if (!asServer && isServer) return;
            if (_isInitialized) return;

            _navAgent = GetComponent<NavMeshAgent>();
            _hitReactionHost = GetComponent<IEnemyHitReactionHost>();

            // Locate EnemyMobile by type name so Gameplay assembly doesn't need to
            // reference the AI assembly directly.
            foreach (var comp in GetComponents<MonoBehaviour>())
            {
                if (comp != null && comp.GetType().Name == "EnemyMobile")
                {
                    _enemyMobile = comp;
                    break;
                }
            }

            if (isServer)
            {
                SyncedPosition.value = transform.position;
                SyncedRotation.value = transform.rotation;
                _lastObservedStunState = false;
            }
            else
            {
                // Clients don't run AI — host is the single authority for enemy behavior.
                if (_enemyMobile != null)
                {
                    _enemyMobile.enabled = false;
                }

                if (_navAgent != null)
                {
                    _navAgent.enabled = false;
                }

                _lastObservedStunState = IsStunned.value;
            }

            _isInitialized = true;
        }

        protected override void OnDespawned()
        {
            _isKnockedBack = false;
            _isInitialized = false;
            base.OnDespawned();
        }

        void Update()
        {
            if (!_isInitialized) return;

            if (isServer)
                ServerUpdate();
            else
                ClientUpdate();
        }

        // ── Server update ─────────────────────────────────────────────────────────

        void ServerUpdate()
        {
            TickKnockback();
            TickStunExpiry();
            TickPositionSync();
        }

        void TickKnockback()
        {
            if (!_isKnockedBack) return;

            if (Time.time >= _knockbackEndTime)
            {
                EndKnockback();
                return;
            }

            // NavMeshAgent.Move() displaces the agent while respecting NavMesh geometry.
            // Safe to call even when isStopped = true (it bypasses the automatic movement).
            if (_navAgent != null && _navAgent.isActiveAndEnabled)
            {
                _navAgent.Move(_knockbackVelocity * Time.deltaTime);
            }
        }

        void TickStunExpiry()
        {
            if (!IsStunned.value) return;
            if (Time.time < StunEndServerTime.value) return;

            IsStunned.value = false;
            _hitReactionHost?.OnHitReactionStunExpired();

            // Resume NavAgent only once knockback has also ended
            if (!_isKnockedBack && _navAgent != null && _navAgent.isActiveAndEnabled)
            {
                _navAgent.isStopped = false;
            }

            Debug.Log("[NetworkedEnemySync] Stun expired — AI resuming.");
        }

        void TickPositionSync()
        {
            if (Time.time < _lastPositionSyncTime + (1f / _positionSyncRate)) return;

            SyncedPosition.value = transform.position;
            SyncedRotation.value = transform.rotation;
            _lastPositionSyncTime = Time.time;
        }

        // ── Client update ─────────────────────────────────────────────────────────

        void ClientUpdate()
        {
            InterpolateToSyncedTransform();
            MirrorStunStateFromSyncVar();
        }

        void InterpolateToSyncedTransform()
        {
            float dist = Vector3.Distance(transform.position, SyncedPosition.value);

            if (dist > _snapThreshold)
            {
                transform.position = SyncedPosition.value;
                transform.rotation = SyncedRotation.value;
            }
            else if (dist > 0.005f)
            {
                float t = Time.deltaTime * _interpolationSpeed;
                transform.position = Vector3.Lerp(transform.position, SyncedPosition.value, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, SyncedRotation.value, t);
            }
        }

        /// <summary>
        /// Polls the IsStunned SyncVar each frame and fires IEnemyHitReactionHost callbacks
        /// when the value transitions. This drives the stun state on both server and clients
        /// without requiring PurrNet SyncVar onChange wiring.
        /// </summary>
        void MirrorStunStateFromSyncVar()
        {
            bool current = IsStunned.value;
            if (current == _lastObservedStunState) return;

            _lastObservedStunState = current;

            if (current)
                _hitReactionHost?.OnHitReactionStunBegin();
            else
                _hitReactionHost?.OnHitReactionStunExpired();
        }

        // ── Public authoritative reaction entry point ─────────────────────────────

        /// <summary>
        /// Host-only entry point. Called by AuthoritativeDamageResolver after a validated
        /// weapon shot hits this creature. Applies knockback and/or stun through the
        /// host-authoritative path, then broadcasts a presentation RPC to observers.
        /// </summary>
        public void ApplyAuthoritativeHitReaction(in EnemyHitReactionContext reaction)
        {
            if (!isServer)
            {
                Debug.LogWarning($"[NetworkedEnemySync] ApplyAuthoritativeHitReaction called on non-server (seq={reaction.sequenceId}). Ignoring.");
                return;
            }

            if (_hitReactionHost != null && _hitReactionHost.IsDeadOrDying)
            {
                Debug.Log($"[NetworkedEnemySync] Skipping reaction seq={reaction.sequenceId}: target dead or dying.");
                return;
            }

            if (reaction.knockbackStrength > 0f)
            {
                ApplyKnockback(reaction.knockbackDirection, reaction.knockbackStrength);
            }

            if (reaction.stunDuration > 0f)
            {
                ApplyStun(reaction.stunDuration);
            }

            // Replicate hit-reaction presentation to all observer clients
            NotifyObserversHitReaction(reaction.hitPoint, reaction.knockbackDirection, reaction.sequenceId);

            Debug.Log($"[NetworkedEnemySync] Reaction applied seq={reaction.sequenceId} — knockback={reaction.knockbackStrength:0.##} m/s, stun={reaction.stunDuration:0.##}s");
        }

        // ── Knockback implementation (host-only) ──────────────────────────────────

        void ApplyKnockback(Vector3 direction, float strength)
        {
            if (_navAgent == null || !_navAgent.isActiveAndEnabled) return;

            _navAgent.isStopped = true;
            _navAgent.ResetPath();

            float speed = Mathf.Min(strength, _knockbackMaxSpeed);
            _knockbackVelocity = direction.normalized * speed;
            _knockbackEndTime = Time.time + _knockbackDuration;
            _isKnockedBack = true;

            _hitReactionHost?.OnHitReactionKnockbackBegin();

            Debug.Log($"[NetworkedEnemySync] Knockback: dir={direction:F2}, speed={speed:0.##}, dur={_knockbackDuration:0.##}s");
        }

        void EndKnockback()
        {
            _isKnockedBack = false;
            _knockbackVelocity = Vector3.zero;

            _hitReactionHost?.OnHitReactionKnockbackEnd();

            // Resume NavAgent only when stun has also ended
            if (!IsStunned.value && _navAgent != null && _navAgent.isActiveAndEnabled)
            {
                _navAgent.isStopped = false;
            }
        }

        // ── Stun implementation (host-only) ───────────────────────────────────────

        void ApplyStun(float duration)
        {
            if (_navAgent != null && _navAgent.isActiveAndEnabled)
            {
                _navAgent.isStopped = true;
                _navAgent.ResetPath();
            }

            // Refresh: take the later expiry rather than stacking independent timers
            float proposedEnd = Time.time + duration;
            if (!IsStunned.value || proposedEnd > StunEndServerTime.value)
            {
                StunEndServerTime.value = proposedEnd;
            }

            if (!IsStunned.value)
            {
                IsStunned.value = true;
                // OnHitReactionStunBegin is driven via MirrorStunStateFromSyncVar on the
                // next Update tick so both server and clients take the same code path.
            }

            Debug.Log($"[NetworkedEnemySync] Stun applied: dur={duration:0.##}s, expires at server t={StunEndServerTime.value:0.##}");
        }

        // ── Observer presentation RPC ─────────────────────────────────────────────

        /// <summary>
        /// Sent to all clients (including host) after an authoritative hit reaction is applied.
        /// Clients use this for visual/audio presentation only — no gameplay state changes.
        /// </summary>
        [ObserversRpc]
        void NotifyObserversHitReaction(Vector3 hitPoint, Vector3 knockbackDirection, int sequenceId)
        {
            // Host already handled gameplay in ApplyAuthoritativeHitReaction; skip here.
            if (isServer) return;

            _hitReactionHost?.PlayHitReactionPresentation(hitPoint, knockbackDirection);
            Debug.Log($"[NetworkedEnemySync] Observer presentation seq={sequenceId}");
        }
    }
}
