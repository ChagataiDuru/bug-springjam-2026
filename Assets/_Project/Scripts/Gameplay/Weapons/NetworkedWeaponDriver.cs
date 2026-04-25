using System;
using System.Collections.Generic;
using PurrNet;
using Taiyun.SuckTheWater.Game;
using UnityEngine;
using UnityEngine.Events;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Card 2 authoritative seam for player-held weapon usage:
    /// owner input -> local predicted presentation -> server validation -> authoritative trace shot.
    /// </summary>
    [RequireComponent(typeof(NetworkedPlayerController))]
    [RequireComponent(typeof(PlayerWeaponsManager))]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class NetworkedWeaponDriver : NetworkBehaviour
    {
        public enum WeaponUseKind
        {
            Primary,
            Secondary,
            Mode
        }

        public enum WeaponUsePhase
        {
            Begin,
            Hold,
            End
        }

        public enum ShotTargetType
        {
            None,
            Player,
            Damageable,
            World,
            Water
        }

        [Serializable]
        public struct WeaponUseIntentPayload
        {
            public int sequenceId;
            public WeaponUseKind useKind;
            public WeaponUsePhase usePhase;
            public int weaponIndex;
            public Vector3 aimOrigin;
            public Vector3 aimDirection;
            public float chargeRatio;
            public float clientTime;
        }

        [Serializable]
        public struct AuthoritativeShotResultPayload
        {
            public int sequenceId;
            public ulong shooterClientId;
            public int weaponIndex;
            public WeaponUseKind useKind;
            public Vector3 shotOrigin;
            public Vector3 shotDirection;
            public Vector3 shotEndPoint;
            public bool didHit;
            public Vector3 hitPoint;
            public Vector3 hitNormal;
            public float hitDistance;
            public int hitLayer;
            public ShotTargetType targetType;
            public ulong targetPlayerClientId;
            public float appliedDamage;
            public float serverTime;
        }

        struct AuthoritativeShotResult
        {
            public Vector3 shotOrigin;
            public Vector3 shotDirection;
            public Vector3 shotEndPoint;
            public bool didHit;
            public Vector3 hitPoint;
            public Vector3 hitNormal;
            public float hitDistance;
            public int hitLayer;
            public ShotTargetType targetType;
            public Damageable hitDamageable;
            public NetworkedPlayerController hitPlayer;
            public float appliedDamage;
        }

        struct PredictedShotSequenceRecord
        {
            public int sequenceId;
            public float localTime;
        }

        struct ReplicatedDamageMirrorRecord
        {
            public ulong shooterClientId;
            public int sequenceId;
            public float localTime;
        }

        public UnityAction<WeaponUseIntentPayload> OnLocalUseIntentRaised;
        public UnityAction<WeaponUseIntentPayload> OnUseRequestSubmitted;
        public UnityAction<WeaponUseIntentPayload> OnServerUseApprovedEvent;
        public UnityAction<WeaponUseIntentPayload> OnServerUseRejectedEvent;
        public UnityAction<WeaponUseIntentPayload> OnObserverUseReplicatedEvent;
        public UnityAction<WeaponUseIntentPayload> OnServerUseRequestAcceptedEvent;
        public UnityAction<WeaponUseIntentPayload> OnServerUseRequestRejectedEvent;
        public UnityAction<AuthoritativeShotResultPayload> OnAuthoritativeShotExecutedEvent;
        public UnityAction<AuthoritativeShotResultPayload> OnObserverAuthoritativeShotReplicatedEvent;

        [SerializeField] bool _enableShotDebugLogs;

        [Header("Creature Hit Reaction (Card 5)")]
        [Tooltip("Apply knockback to creature targets on a confirmed authoritative hit.")]
        [SerializeField] bool _allowCreatureKnockback = true;

        [Tooltip("Apply stun to creature targets on a confirmed authoritative hit.")]
        [SerializeField] bool _allowCreatureStun = true;

        [Tooltip("Knockback impulse speed (m/s) sent to NetworkedEnemySync.")]
        [SerializeField] float _creatureKnockbackStrength = 8f;

        [Tooltip("Stun duration (seconds) sent to NetworkedEnemySync.")]
        [SerializeField] float _creatureStunDuration = 0.7f;

        NetworkedPlayerController _networkedPlayer;
        PlayerWeaponsManager _weaponsManager;
        PlayerInputHandler _inputHandler;
        Health _health;
        Collider[] _selfColliders;
        AuthoritativeDamageResolver _authoritativeDamageResolver;

        bool _isInitialized;
        bool _primaryHeld;
        bool _secondaryHeld;
        int _localSequenceId;
        int _lastProcessedPrimarySequenceId = -1;
        float _lastAcceptedPrimaryShotServerTime = Mathf.NegativeInfinity;
        WeaponController _predictedPrimaryWeapon;
        readonly Queue<PredictedShotSequenceRecord> _predictedPrimaryShotSequences = new Queue<PredictedShotSequenceRecord>();
        readonly Queue<ReplicatedDamageMirrorRecord> _replicatedDamageMirrorRecords =
            new Queue<ReplicatedDamageMirrorRecord>();

        float _lastWaterGunPrimaryHoldSubmitTime  = Mathf.NegativeInfinity;
        float _lastWaterGunSecondaryHoldSubmitTime = Mathf.NegativeInfinity;

        const float k_DefaultTraceDistance = 100f;
        const float k_MinTraceDistance = 5f;
        const float k_MaxTraceDistance = 300f;
        const float k_MinSphereCastRadius = 0.001f;
        const float k_MaxAimOriginDistanceFromShooter = 5f;
        const float k_PredictedSequenceCacheTtlSeconds = 3f;
        const float k_ReplicatedDamageMirrorTtlSeconds = 4f;
        const float k_MinTracerDistance = 0.1f;
        const float k_ReplicatedDamageResolveRadius = 0.75f;
        // Card 6: water gun Hold RPCs are throttled to this interval (seconds) on the client.
        const float k_WaterGunHoldIntervalSeconds = 0.1f;
        const QueryTriggerInteraction k_TriggerInteraction = QueryTriggerInteraction.Collide;
        int _waterLayer = -1;

        void Awake()
        {
            _waterLayer = LayerMask.NameToLayer("Water");
            CacheReferences();
        }

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);

            // Keep the same host double-spawn guard shape used by movement adapter / door flow.
            if (!asServer && isServer)
            {
                return;
            }

            if (_isInitialized)
            {
                return;
            }

            CacheReferences();
            _isInitialized = true;
        }

        protected override void OnDespawned()
        {
            StopAllLocalPredictedUse();
            ClearPredictedPrimaryShotSequenceCache();
            _isInitialized = false;
            _primaryHeld = false;
            _secondaryHeld = false;
            _predictedPrimaryWeapon = null;
            _replicatedDamageMirrorRecords.Clear();
            base.OnDespawned();
        }

        void OnDisable()
        {
            StopAllLocalPredictedUse();
            ClearPredictedPrimaryShotSequenceCache();
            _replicatedDamageMirrorRecords.Clear();
        }

        void Update()
        {
            if (!_isInitialized || !isOwner)
            {
                return;
            }

            if (_weaponsManager == null || _inputHandler == null)
            {
                return;
            }

            HandlePrimaryInputLifecycle();
            HandleSecondaryInputLifecycle();
            EnsurePredictedUseConsistency();
            PrunePredictedPrimaryShotSequences();
        }

        void CacheReferences()
        {
            if (_networkedPlayer == null)
            {
                _networkedPlayer = GetComponent<NetworkedPlayerController>();
            }

            if (_weaponsManager == null)
            {
                _weaponsManager = GetComponent<PlayerWeaponsManager>();
            }

            if (_inputHandler == null)
            {
                _inputHandler = GetComponent<PlayerInputHandler>();
            }

            if (_health == null)
            {
                _health = GetComponent<Health>();
            }

            if (_authoritativeDamageResolver == null)
            {
                _authoritativeDamageResolver = new AuthoritativeDamageResolver(_enableShotDebugLogs);
            }

            _selfColliders = GetComponentsInChildren<Collider>(true);
        }

        void HandlePrimaryInputLifecycle()
        {
            bool primaryPressed = _inputHandler.GetFireInputDown();
            bool primaryHeld = _inputHandler.GetFireInputHeld();
            bool primaryReleased = _inputHandler.GetFireInputReleased();

            if (primaryPressed)
            {
                HandlePrimaryUsePressed();
            }
            else if (primaryHeld)
            {
                HandlePrimaryUseHeld();
            }

            if (primaryReleased)
            {
                HandlePrimaryUseReleased();
            }
        }

        void HandleSecondaryInputLifecycle()
        {
            bool secondaryHeldNow = _inputHandler.GetAimInputHeld();

            if (secondaryHeldNow && !_secondaryHeld)
            {
                HandleSecondaryUsePressed();
            }
            else if (!secondaryHeldNow && _secondaryHeld)
            {
                HandleSecondaryUseReleased();
            }
            _secondaryHeld = secondaryHeldNow;
        }

        public void HandlePrimaryUsePressed()
        {
            _primaryHeld = true;

            WeaponUseIntentPayload payload = BuildIntentPayload(WeaponUseKind.Primary, WeaponUsePhase.Begin);
            OnLocalUseIntentRaised?.Invoke(payload);

            bool shotAttempted = TryBeginLocalPredictedUse(payload);
            if (shotAttempted)
            {
                SubmitUseRequestToServer(payload);
            }
        }

        void HandlePrimaryUseHeld()
        {
            if (!_primaryHeld)
            {
                _primaryHeld = true;
            }

            WeaponUseIntentPayload payload = BuildIntentPayload(WeaponUseKind.Primary, WeaponUsePhase.Hold);
            bool shotAttempted = TryBeginLocalPredictedUse(payload);
        }

        public void HandlePrimaryUseReleased()
        {
            if (!_primaryHeld && _predictedPrimaryWeapon == null)
            {
                return;
            }

            _primaryHeld = false;
            WeaponUseIntentPayload payload = BuildIntentPayload(WeaponUseKind.Primary, WeaponUsePhase.End);
            OnLocalUseIntentRaised?.Invoke(payload);

            bool shotAttempted = TryBeginLocalPredictedUse(payload);
            if (shotAttempted)
            {
                SubmitUseRequestToServer(payload);
            }
        }

        public void HandleSecondaryUsePressed()
        {
            WeaponUseIntentPayload payload = BuildIntentPayload(WeaponUseKind.Secondary, WeaponUsePhase.Begin);
            OnLocalUseIntentRaised?.Invoke(payload);
        }

        public void HandleSecondaryUseReleased()
        {
            WeaponUseIntentPayload payload = BuildIntentPayload(WeaponUseKind.Secondary, WeaponUsePhase.End);
            OnLocalUseIntentRaised?.Invoke(payload);
            // No server RPC needed for water gun End — mutations stop when Hold RPCs stop arriving.
        }

        public void HandleWeaponSwapRequest(int requestedWeaponIndex)
        {
            // Placeholder seam for future authoritative swap requests.
            _ = requestedWeaponIndex;
        }

        public bool TryBeginLocalPredictedUse(WeaponUseIntentPayload payload)
        {
            if (_weaponsManager == null || payload.useKind != WeaponUseKind.Primary)
            {
                return false;
            }

            WeaponController activeWeapon = _weaponsManager.GetActiveWeapon();
            if (payload.usePhase == WeaponUsePhase.Begin)
            {
                _predictedPrimaryWeapon = activeWeapon;
            }

            bool shotAttempted = _weaponsManager.ProcessPredictedPrimaryUseInput(
                payload.usePhase == WeaponUsePhase.Begin,
                payload.usePhase != WeaponUsePhase.End,
                payload.usePhase == WeaponUsePhase.End);

            if (payload.usePhase == WeaponUsePhase.End)
            {
                if (_predictedPrimaryWeapon != null && _predictedPrimaryWeapon != activeWeapon)
                {
                    _predictedPrimaryWeapon.PlayLocalPredictedUseStop();
                }

                _weaponsManager.StopPredictedUseOnWeapon(activeWeapon);
                _predictedPrimaryWeapon = null;
            }

            if (shotAttempted && isOwner)
            {
                TrackPredictedPrimaryShotSequence(payload.sequenceId);
            }

            return shotAttempted;
        }

        public void SubmitUseRequestToServer(WeaponUseIntentPayload payload)
        {
            OnUseRequestSubmitted?.Invoke(payload);

            if (isServer)
            {
                ProcessUseRequestOnServer(payload, default, bypassSenderCheck: true);
                return;
            }

            SubmitUseRequestServerRpc(payload);
        }

        [ServerRpc(requireOwnership: true)]
        void SubmitUseRequestServerRpc(WeaponUseIntentPayload payload, RPCInfo info = default)
        {
            ProcessUseRequestOnServer(payload, info, bypassSenderCheck: false);
        }

        void ProcessUseRequestOnServer(WeaponUseIntentPayload payload, RPCInfo info, bool bypassSenderCheck)
        {
            if (!isServer)
            {
                return;
            }

            if (!ValidateRequestSender(info, bypassSenderCheck))
            {
                RejectUseRequestOnServer(payload, "sender mismatch");
                return;
            }

            if (!ValidateServerRequest(payload, out WeaponController authoritativeWeapon, out string rejectReason))
            {
                RejectUseRequestOnServer(payload, rejectReason);
                return;
            }

            _lastAcceptedPrimaryShotServerTime = Time.time;
            OnServerUseRequestAcceptedEvent?.Invoke(payload);

            AuthoritativeShotResult shotResult = ExecuteAuthoritativeShotOnServer(payload, authoritativeWeapon);
            ResolveAuthoritativeShotResult(ref shotResult, authoritativeWeapon, payload);

            AuthoritativeShotResultPayload replicatedResult = BuildShotResultPayload(payload, shotResult);
            OnAuthoritativeShotExecutedEvent?.Invoke(replicatedResult);

            LogShotDebug(
                $"Accepted seq={payload.sequenceId}, phase={payload.usePhase}, hit={shotResult.didHit}, target={shotResult.targetType}, appliedDamage={shotResult.appliedDamage:0.##}");

            UseApprovedOwnerRpc(payload);
            ReplicateUseObserversRpc(payload);
            ReplicateAuthoritativeShotObserversRpc(replicatedResult);

            LogShotDebug(
                $"Broadcast authoritative presentation seq={payload.sequenceId}, origin={replicatedResult.shotOrigin}, end={replicatedResult.shotEndPoint}, hit={replicatedResult.didHit}");
        }

        bool ValidateRequestSender(RPCInfo info, bool bypassSenderCheck)
        {
            if (!owner.HasValue)
            {
                return false;
            }

            if (bypassSenderCheck)
            {
                return true;
            }

            return owner.Value.id == info.sender.id;
        }

        bool ValidateServerRequest(WeaponUseIntentPayload payload, out WeaponController authoritativeWeapon,
            out string rejectReason)
        {
            authoritativeWeapon = null;
            rejectReason = string.Empty;

            // Mode use kind has no server path.  Primary and Secondary are both supported.
            if (payload.useKind == WeaponUseKind.Mode)
            {
                rejectReason = "unsupported use kind";
                return false;
            }

            // Resolve weapon early so we can check the type for Secondary gating.
            authoritativeWeapon = ResolveAuthoritativeWeapon(payload.weaponIndex);
            if (authoritativeWeapon == null)
            {
                rejectReason = "missing active weapon";
                return false;
            }

            // Duplicate / stale sequence guard applies to Primary only.
            // Water gun secondary is a continuous stream — no sequence dedup needed.
            if (payload.useKind == WeaponUseKind.Primary)
            {
                if (payload.sequenceId <= _lastProcessedPrimarySequenceId)
                {
                    rejectReason = "stale or duplicate sequence";
                    return false;
                }

                _lastProcessedPrimarySequenceId = payload.sequenceId;
            }

            if (_health != null && _health.CurrentHealth <= 0f)
            {
                rejectReason = "shooter dead";
                return false;
            }

            if (_networkedPlayer != null &&
                _networkedPlayer.CharacterController != null &&
                _networkedPlayer.CharacterController.IsDead)
            {
                rejectReason = "character dead";
                return false;
            }

            if (!IsFiniteVector3(payload.aimOrigin) || !IsFiniteVector3(payload.aimDirection))
            {
                rejectReason = "invalid aim payload";
                return false;
            }

            if (payload.aimDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                rejectReason = "zero aim direction";
                return false;
            }

            float originOffsetSqr = (payload.aimOrigin - transform.position).sqrMagnitude;
            if (originOffsetSqr > k_MaxAimOriginDistanceFromShooter * k_MaxAimOriginDistanceFromShooter)
            {
                rejectReason = "aim origin too far from shooter";
                return false;
            }

            float requiredDelay = Mathf.Max(0f, authoritativeWeapon.DelayBetweenShots);
            if (requiredDelay > 0f && Time.time < _lastAcceptedPrimaryShotServerTime + requiredDelay)
            {
                rejectReason = "cooldown not elapsed";
                return false;
            }

            if (authoritativeWeapon.gameObject.scene.IsValid())
            {
                if (authoritativeWeapon.IsReloading)
                {
                    rejectReason = "weapon reloading";
                    return false;
                }
            }

            return true;
        }

        WeaponController ResolveAuthoritativeWeapon(int weaponIndex)
        {
            if (_weaponsManager == null)
            {
                return null;
            }

            WeaponController runtimeWeapon = _weaponsManager.GetWeaponAtSlotIndex(weaponIndex);
            if (runtimeWeapon != null)
            {
                return runtimeWeapon;
            }

            runtimeWeapon = _weaponsManager.GetActiveWeapon();
            if (runtimeWeapon != null)
            {
                return runtimeWeapon;
            }

            if (weaponIndex >= 0 && weaponIndex < _weaponsManager.StartingWeapons.Count)
            {
                WeaponController startingWeapon = _weaponsManager.StartingWeapons[weaponIndex];
                if (startingWeapon != null)
                {
                    return startingWeapon;
                }
            }

            for (int i = 0; i < _weaponsManager.StartingWeapons.Count; i++)
            {
                WeaponController candidate = _weaponsManager.StartingWeapons[i];
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        AuthoritativeShotResult ExecuteAuthoritativeShotOnServer(WeaponUseIntentPayload payload,
            WeaponController authoritativeWeapon)
        {
            BuildTraceParameters(authoritativeWeapon, payload,
                out float traceRadius,
                out LayerMask hittableLayers,
                out float traceDistance,
                out int traceCount);

            Vector3 traceOrigin = ResolveAuthoritativeShotOrigin(authoritativeWeapon, payload.aimOrigin);
            Vector3 baseDirection = payload.aimDirection.normalized;
            if (baseDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                baseDirection = transform.forward;
            }

            AuthoritativeShotResult result = new AuthoritativeShotResult
            {
                shotOrigin = traceOrigin,
                shotDirection = baseDirection,
                shotEndPoint = traceOrigin + (baseDirection * traceDistance),
                didHit = false,
                hitPoint = Vector3.zero,
                hitNormal = Vector3.zero,
                hitDistance = 0f,
                hitLayer = -1,
                targetType = ShotTargetType.None,
                hitDamageable = null,
                hitPlayer = null,
                appliedDamage = 0f
            };

            for (int i = 0; i < traceCount; i++)
            {
                Vector3 traceDirection = ApplyAuthoritativeSpread(baseDirection, authoritativeWeapon.BulletSpreadAngle);
                bool hitFound = TryPerformAuthoritativeTrace(
                    traceOrigin,
                    traceDirection,
                    traceRadius,
                    traceDistance,
                    hittableLayers,
                    out RaycastHit hit,
                    out Damageable hitDamageable,
                    out NetworkedPlayerController hitPlayer,
                    out ShotTargetType targetType);

                if (!hitFound)
                {
                    continue;
                }

                if (!result.didHit || hit.distance < result.hitDistance)
                {
                    result.didHit = true;
                    result.hitPoint = hit.point;
                    result.hitNormal = hit.normal;
                    result.hitDistance = hit.distance;
                    result.hitLayer = hit.collider != null ? hit.collider.gameObject.layer : -1;
                    result.targetType = targetType;
                    result.hitDamageable = hitDamageable;
                    result.hitPlayer = hitPlayer;
                    result.shotDirection = traceDirection;
                    result.shotEndPoint = hit.point;
                }
            }

            if (!result.didHit)
            {
                result.shotEndPoint = result.shotOrigin + (result.shotDirection * traceDistance);
            }

            return result;
        }

        static Vector3 ResolveAuthoritativeShotOrigin(WeaponController authoritativeWeapon, Vector3 fallbackOrigin)
        {
            if (authoritativeWeapon != null &&
                authoritativeWeapon.WeaponMuzzle != null &&
                authoritativeWeapon.WeaponMuzzle.gameObject.scene.IsValid())
            {
                return authoritativeWeapon.WeaponMuzzle.position;
            }

            return fallbackOrigin;
        }

        void BuildTraceParameters(WeaponController authoritativeWeapon, WeaponUseIntentPayload payload,
            out float traceRadius, out LayerMask hittableLayers, out float traceDistance, out int traceCount)
        {
            traceRadius = 0f;
            hittableLayers = Physics.DefaultRaycastLayers;
            traceDistance = k_DefaultTraceDistance;
            traceCount = 1;

            if (authoritativeWeapon == null)
            {
                return;
            }

            traceCount = GetTraceCount(authoritativeWeapon, payload.chargeRatio);

            ProjectileBase projectilePrefab = authoritativeWeapon.ProjectilePrefab;
            if (projectilePrefab == null)
            {
                return;
            }

            ProjectileStandard projectileStandard = projectilePrefab.GetComponent<ProjectileStandard>();
            if (projectileStandard == null)
            {
                return;
            }

            traceRadius = Mathf.Max(0f, projectileStandard.Radius);
            if (projectileStandard.HittableLayers.value != 0)
            {
                hittableLayers = projectileStandard.HittableLayers;
            }

            float derivedDistance = projectileStandard.Speed * projectileStandard.MaxLifeTime;
            if (derivedDistance > 0f)
            {
                traceDistance = Mathf.Clamp(derivedDistance, k_MinTraceDistance, k_MaxTraceDistance);
            }
        }

        int GetTraceCount(WeaponController weapon, float chargeRatio)
        {
            int baseCount = Mathf.Max(1, weapon.BulletsPerShot);
            if (weapon.ShootType == WeaponShootType.Charge)
            {
                return Mathf.Max(1, Mathf.CeilToInt(Mathf.Clamp01(chargeRatio) * baseCount));
            }

            return baseCount;
        }

        Vector3 ApplyAuthoritativeSpread(Vector3 direction, float spreadAngle)
        {
            if (spreadAngle <= 0f)
            {
                return direction;
            }

            float spreadAngleRatio = Mathf.Clamp01(spreadAngle / 180f);
            Vector3 spreadDirection = Vector3.Slerp(direction, UnityEngine.Random.insideUnitSphere, spreadAngleRatio);
            if (spreadDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return direction;
            }

            return spreadDirection.normalized;
        }

        bool TryPerformAuthoritativeTrace(Vector3 traceOrigin, Vector3 traceDirection, float traceRadius,
            float traceDistance, LayerMask hittableLayers, out RaycastHit bestHit, out Damageable hitDamageable,
            out NetworkedPlayerController hitPlayer, out ShotTargetType targetType)
        {
            bestHit = default;
            hitDamageable = null;
            hitPlayer = null;
            targetType = ShotTargetType.None;

            RaycastHit[] hits = traceRadius > k_MinSphereCastRadius
                ? Physics.SphereCastAll(traceOrigin, traceRadius, traceDirection, traceDistance, hittableLayers,
                    k_TriggerInteraction)
                : Physics.RaycastAll(traceOrigin, traceDirection, traceDistance, hittableLayers, k_TriggerInteraction);

            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsAuthoritativeHitValid(hit))
                {
                    continue;
                }

                bestHit = hit;
                hitDamageable = hit.collider.GetComponent<Damageable>();
                if (hitDamageable == null)
                {
                    hitDamageable = hit.collider.GetComponentInParent<Damageable>();
                }

                hitPlayer = hit.collider.GetComponentInParent<NetworkedPlayerController>();
                if (_waterLayer >= 0 && hit.collider.gameObject.layer == _waterLayer)
                {
                    targetType = ShotTargetType.Water;
                }
                else if (hitPlayer != null)
                {
                    targetType = ShotTargetType.Player;
                }
                else if (hitDamageable != null)
                {
                    targetType = ShotTargetType.Damageable;
                }
                else
                {
                    targetType = ShotTargetType.World;
                }

                return true;
            }

            return false;
        }

        bool IsAuthoritativeHitValid(RaycastHit hit)
        {
            if (hit.collider == null)
            {
                return false;
            }

            if (hit.collider.GetComponent<IgnoreHitDetection>() != null)
            {
                return false;
            }

            if (hit.collider.isTrigger)
            {
                Damageable triggerDamageable = hit.collider.GetComponent<Damageable>();
                if (triggerDamageable == null)
                {
                    triggerDamageable = hit.collider.GetComponentInParent<Damageable>();
                }

                if (triggerDamageable == null)
                {
                    return false;
                }
            }

            if (_selfColliders != null)
            {
                for (int i = 0; i < _selfColliders.Length; i++)
                {
                    if (_selfColliders[i] == hit.collider)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        void ResolveAuthoritativeShotResult(ref AuthoritativeShotResult shotResult, WeaponController weapon,
            WeaponUseIntentPayload payload)
        {
            if (!shotResult.didHit)
            {
                return;
            }

            float damage = ResolveAuthoritativeDamage(weapon, payload.chargeRatio);
            TryApplyAuthoritativeHit(ref shotResult, damage, payload);
        }

        bool TryApplyAuthoritativeHit(ref AuthoritativeShotResult shotResult, float damage, WeaponUseIntentPayload payload)
        {
            if (!isServer || !shotResult.didHit || shotResult.hitDamageable == null || damage <= 0f)
            {
                return false;
            }

            if (_authoritativeDamageResolver == null)
            {
                _authoritativeDamageResolver = new AuthoritativeDamageResolver(_enableShotDebugLogs);
            }

            AuthoritativeDamageContext damageContext = new AuthoritativeDamageContext
            {
                sequenceId = payload.sequenceId,
                attackerClientId = owner?.id ?? 0,
                isServerAuthority = isServer,
                attackerObject = gameObject,
                targetDamageable = shotResult.hitDamageable,
                baseDamage = damage,
                sourceKind = DamageSourceKind.WeaponShot,
                weaponIndex = payload.weaponIndex,
                useKind = payload.useKind,
                hitPoint = shotResult.hitPoint,
                hitNormal = shotResult.hitNormal,
                allowKnockback = _allowCreatureKnockback,
                allowStun = _allowCreatureStun,
                knockbackStrength = _creatureKnockbackStrength,
                stunDuration = _creatureStunDuration,
                reactionSourcePosition = transform.position
            };

            bool applied = _authoritativeDamageResolver.TryResolveAndApply(damageContext, out AuthoritativeDamageResult result);
            shotResult.appliedDamage = result.appliedDamage;

            if (!applied)
            {
                LogShotDebug(
                    $"Damage rejected seq={payload.sequenceId}, reason={result.reason}, target={result.targetKind}, baseDamage={damage:0.##}");
            }
            else
            {
                LogShotDebug(
                    $"Damage applied seq={payload.sequenceId}, target={result.targetKind}, appliedDamage={result.appliedDamage:0.##}, killed={result.killed}");
            }

            return applied;
        }


        float ResolveAuthoritativeDamage(WeaponController weapon, float chargeRatio)
        {
            if (weapon == null || weapon.ProjectilePrefab == null)
            {
                return 0f;
            }

            ProjectileStandard projectileStandard = weapon.ProjectilePrefab.GetComponent<ProjectileStandard>();
            if (projectileStandard == null)
            {
                return 0f;
            }

            float damage = projectileStandard.Damage;

            if (weapon.ShootType == WeaponShootType.Charge)
            {
                ProjectileChargeParameters chargeParameters =
                    weapon.ProjectilePrefab.GetComponent<ProjectileChargeParameters>();
                if (chargeParameters != null)
                {
                    damage = chargeParameters.Damage.GetValueFromRatio(Mathf.Clamp01(chargeRatio));
                }
            }

            return Mathf.Max(0f, damage);
        }

        AuthoritativeShotResultPayload BuildShotResultPayload(WeaponUseIntentPayload payload,
            AuthoritativeShotResult shotResult)
        {
            return new AuthoritativeShotResultPayload
            {
                sequenceId = payload.sequenceId,
                shooterClientId = owner?.id ?? 0,
                weaponIndex = payload.weaponIndex,
                useKind = payload.useKind,
                shotOrigin = shotResult.shotOrigin,
                shotDirection = shotResult.shotDirection,
                shotEndPoint = shotResult.shotEndPoint,
                didHit = shotResult.didHit,
                hitPoint = shotResult.hitPoint,
                hitNormal = shotResult.hitNormal,
                hitDistance = shotResult.hitDistance,
                hitLayer = shotResult.hitLayer,
                targetType = shotResult.targetType,
                targetPlayerClientId = shotResult.hitPlayer != null ? shotResult.hitPlayer.PlayerClientId : 0,
                appliedDamage = shotResult.appliedDamage,
                serverTime = Time.time
            };
        }

        void RejectUseRequestOnServer(WeaponUseIntentPayload payload, string reason)
        {
            OnServerUseRequestRejectedEvent?.Invoke(payload);
            LogShotDebug($"Rejected seq={payload.sequenceId}, phase={payload.usePhase}, reason={reason}");
            UseRejectedOwnerRpc(payload);
        }

        [ObserversRpc]
        void UseApprovedOwnerRpc(WeaponUseIntentPayload payload)
        {
            if (!isOwner)
            {
                return;
            }

            OnServerUseApproved(payload);
        }

        [ObserversRpc]
        void UseRejectedOwnerRpc(WeaponUseIntentPayload payload)
        {
            if (!isOwner)
            {
                return;
            }

            OnServerUseRejected(payload);
        }

        [ObserversRpc]
        void ReplicateUseObserversRpc(WeaponUseIntentPayload payload)
        {
            if (isOwner)
            {
                return;
            }

            OnObserverUseReplicated(payload);
        }

        [ObserversRpc]
        void ReplicateAuthoritativeShotObserversRpc(AuthoritativeShotResultPayload resultPayload)
        {
            OnObserverAuthoritativeShotReplicated(resultPayload);
        }

        public void OnServerUseApproved(WeaponUseIntentPayload payload)
        {
            OnServerUseApprovedEvent?.Invoke(payload);
        }

        public void OnServerUseRejected(WeaponUseIntentPayload payload)
        {
            OnServerUseRejectedEvent?.Invoke(payload);
            StopAllLocalPredictedUse();
        }

        public void OnObserverUseReplicated(WeaponUseIntentPayload payload)
        {
            OnObserverUseReplicatedEvent?.Invoke(payload);

            if (_weaponsManager == null)
            {
                return;
            }

            WeaponController replicatedWeapon = _weaponsManager.GetWeaponAtSlotIndex(payload.weaponIndex);
            if (replicatedWeapon == null)
            {
                replicatedWeapon = _weaponsManager.GetActiveWeapon();
            }

            if (replicatedWeapon == null)
            {
                return;
            }

            if (payload.usePhase == WeaponUsePhase.Begin)
            {
                replicatedWeapon.PlayRemoteUseStart();
            }
            else if (payload.usePhase == WeaponUsePhase.End)
            {
                replicatedWeapon.PlayRemoteUseStop();
            }
        }

        public void OnObserverAuthoritativeShotReplicated(AuthoritativeShotResultPayload resultPayload)
        {
            OnObserverAuthoritativeShotReplicatedEvent?.Invoke(resultPayload);

            if (!TrySanitizeShotPresentationPayload(resultPayload, out AuthoritativeShotResultPayload sanitizedPayload))
            {
                LogShotDebug($"Skipped authoritative presentation seq={resultPayload.sequenceId}: invalid payload vectors");
                return;
            }

            WeaponController presentationWeapon =
                ResolvePresentationWeapon(sanitizedPayload.weaponIndex, out WeaponController presentationConfig);

            bool ownerSuppressedDuplicateFire = isOwner && ConsumePredictedPrimaryShotSequence(sanitizedPayload.sequenceId);
            if (ownerSuppressedDuplicateFire)
            {
                LogShotDebug($"Owner suppressed duplicate fire replay seq={sanitizedPayload.sequenceId}");
            }
            else
            {
                PlayReplicatedFirePresentation(sanitizedPayload, presentationWeapon, presentationConfig);
                LogShotDebug($"Observer played fire presentation seq={sanitizedPayload.sequenceId}, owner={isOwner}");
            }

            WeaponController.ReplicatedImpactType impactType = ResolveReplicatedImpactType(sanitizedPayload);
            if (impactType == WeaponController.ReplicatedImpactType.None)
            {
                return;
            }

            if (PlayReplicatedImpactPresentation(sanitizedPayload, impactType, presentationWeapon, presentationConfig))
            {
                LogShotDebug(
                    $"Impact presentation seq={sanitizedPayload.sequenceId}, type={impactType}, point={sanitizedPayload.hitPoint}");
            }

            TryMirrorReplicatedNonPlayerDamage(sanitizedPayload);
        }

        bool TrySanitizeShotPresentationPayload(AuthoritativeShotResultPayload payload,
            out AuthoritativeShotResultPayload sanitizedPayload)
        {
            sanitizedPayload = payload;

            if (!IsFiniteVector3(payload.shotOrigin))
            {
                return false;
            }

            Vector3 shotDirection = payload.shotDirection;
            if (!IsFiniteVector3(shotDirection) || shotDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                shotDirection = payload.didHit && IsFiniteVector3(payload.hitPoint)
                    ? payload.hitPoint - payload.shotOrigin
                    : transform.forward;
            }

            if (shotDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            shotDirection = shotDirection.normalized;

            Vector3 shotEndPoint = payload.shotEndPoint;
            if (!IsFiniteVector3(shotEndPoint))
            {
                shotEndPoint = payload.didHit && IsFiniteVector3(payload.hitPoint)
                    ? payload.hitPoint
                    : payload.shotOrigin + (shotDirection * k_DefaultTraceDistance);
            }

            if ((shotEndPoint - payload.shotOrigin).sqrMagnitude < k_MinTracerDistance * k_MinTracerDistance)
            {
                shotEndPoint = payload.shotOrigin + (shotDirection * k_MinTracerDistance);
            }

            sanitizedPayload.shotDirection = shotDirection;
            sanitizedPayload.shotEndPoint = shotEndPoint;

            if (sanitizedPayload.didHit)
            {
                if (!IsFiniteVector3(sanitizedPayload.hitPoint))
                {
                    sanitizedPayload.didHit = false;
                    sanitizedPayload.targetType = ShotTargetType.None;
                }
                else if (!IsFiniteVector3(sanitizedPayload.hitNormal) ||
                         sanitizedPayload.hitNormal.sqrMagnitude <= Mathf.Epsilon)
                {
                    sanitizedPayload.hitNormal = -shotDirection;
                }
            }

            return true;
        }

        WeaponController ResolvePresentationWeapon(int weaponIndex, out WeaponController presentationConfig)
        {
            presentationConfig = null;

            if (_weaponsManager == null)
            {
                return null;
            }

            WeaponController runtimeWeapon = _weaponsManager.GetWeaponAtSlotIndex(weaponIndex);
            if (runtimeWeapon == null)
            {
                runtimeWeapon = _weaponsManager.GetActiveWeapon();
            }

            if (runtimeWeapon != null)
            {
                presentationConfig = runtimeWeapon;
                return runtimeWeapon;
            }

            if (weaponIndex >= 0 && weaponIndex < _weaponsManager.StartingWeapons.Count)
            {
                presentationConfig = _weaponsManager.StartingWeapons[weaponIndex];
            }

            if (presentationConfig != null)
            {
                return null;
            }

            for (int i = 0; i < _weaponsManager.StartingWeapons.Count; i++)
            {
                WeaponController candidate = _weaponsManager.StartingWeapons[i];
                if (candidate != null)
                {
                    presentationConfig = candidate;
                    break;
                }
            }

            return null;
        }

        void PlayReplicatedFirePresentation(AuthoritativeShotResultPayload payload, WeaponController runtimeWeapon,
            WeaponController presentationConfig)
        {
            if (runtimeWeapon != null)
            {
                runtimeWeapon.PlayReplicatedObserverFirePresentation(
                    payload.shotOrigin,
                    payload.shotDirection,
                    payload.shotEndPoint);
                return;
            }

            Vector3 shotDirection = payload.shotDirection.sqrMagnitude > Mathf.Epsilon
                ? payload.shotDirection.normalized
                : transform.forward;
            Quaternion shotRotation = Quaternion.LookRotation(shotDirection);

            if (presentationConfig != null)
            {
                WeaponController.SpawnReplicatedMuzzleFlash(
                    presentationConfig.MuzzleFlashPrefab,
                    payload.shotOrigin,
                    shotRotation);
                WeaponController.PlayReplicatedShootSfx(
                    ResolveReplicatedFireSfxClip(presentationConfig),
                    payload.shotOrigin);
            }

            WeaponController.SpawnReplicatedTracer(payload.shotOrigin, payload.shotEndPoint);
        }

        bool PlayReplicatedImpactPresentation(AuthoritativeShotResultPayload payload,
            WeaponController.ReplicatedImpactType impactType, WeaponController runtimeWeapon,
            WeaponController presentationConfig)
        {
            WeaponController sourceWeapon = runtimeWeapon != null ? runtimeWeapon : presentationConfig;
            WeaponController.ImpactPresentationData impactData = BuildImpactPresentationData(sourceWeapon);
            if (!impactData.IsConfigured)
            {
                return false;
            }

            if (runtimeWeapon != null)
            {
                runtimeWeapon.PlayAuthoritativeImpactPresentation(payload.hitPoint, payload.hitNormal, impactType,
                    impactData);
            }
            else
            {
                WeaponController.SpawnImpactPresentation(payload.hitPoint, payload.hitNormal, impactData);
            }

            return true;
        }

        void TryMirrorReplicatedNonPlayerDamage(AuthoritativeShotResultPayload payload)
        {
            if (isServer || !payload.didHit || payload.appliedDamage <= 0f || payload.sequenceId <= 0)
            {
                return;
            }

            if (payload.targetType != ShotTargetType.Damageable)
            {
                return;
            }

            if (IsReplicatedDamageMirrorDuplicate(payload.shooterClientId, payload.sequenceId))
            {
                return;
            }

            Damageable targetDamageable = ResolveReplicatedDamageableAtPoint(payload.hitPoint);
            if (targetDamageable == null)
            {
                LogShotDebug($"Replicated damage mirror skipped seq={payload.sequenceId}: no local damageable at hit");
                return;
            }

            if (targetDamageable.GetComponentInParent<NetworkedPlayerController>() != null)
            {
                return;
            }

            bool applied = targetDamageable.TryApplyAuthoritativeDamage(payload.appliedDamage, false, null,
                out float mirroredDamage, out bool killed);
            if (!applied)
            {
                return;
            }

            TrackReplicatedDamageMirror(payload.shooterClientId, payload.sequenceId);
            LogShotDebug(
                $"Replicated non-player damage mirrored seq={payload.sequenceId}, amount={mirroredDamage:0.##}, killed={killed}");
        }

        Damageable ResolveReplicatedDamageableAtPoint(Vector3 hitPoint)
        {
            if (!IsFiniteVector3(hitPoint))
            {
                return null;
            }

            Collider[] overlaps = Physics.OverlapSphere(
                hitPoint,
                k_ReplicatedDamageResolveRadius,
                Physics.DefaultRaycastLayers,
                k_TriggerInteraction);

            if (overlaps == null || overlaps.Length == 0)
            {
                return null;
            }

            Damageable bestDamageable = null;
            float bestDistanceSqr = float.PositiveInfinity;

            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider overlap = overlaps[i];
                if (overlap == null)
                {
                    continue;
                }

                Damageable candidate = overlap.GetComponent<Damageable>();
                if (candidate == null)
                {
                    candidate = overlap.GetComponentInParent<Damageable>();
                }

                if (candidate == null || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                Vector3 closestPoint = overlap.ClosestPoint(hitPoint);
                float distanceSqr = (closestPoint - hitPoint).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestDamageable = candidate;
                }
            }

            return bestDamageable;
        }

        bool IsReplicatedDamageMirrorDuplicate(ulong shooterClientId, int sequenceId)
        {
            PruneReplicatedDamageMirrorRecords();

            bool duplicate = false;
            int pending = _replicatedDamageMirrorRecords.Count;
            for (int i = 0; i < pending; i++)
            {
                ReplicatedDamageMirrorRecord record = _replicatedDamageMirrorRecords.Dequeue();
                if (!duplicate &&
                    record.shooterClientId == shooterClientId &&
                    record.sequenceId == sequenceId)
                {
                    duplicate = true;
                }

                _replicatedDamageMirrorRecords.Enqueue(record);
            }

            return duplicate;
        }

        void TrackReplicatedDamageMirror(ulong shooterClientId, int sequenceId)
        {
            PruneReplicatedDamageMirrorRecords();
            _replicatedDamageMirrorRecords.Enqueue(new ReplicatedDamageMirrorRecord
            {
                shooterClientId = shooterClientId,
                sequenceId = sequenceId,
                localTime = Time.time
            });
        }

        void PruneReplicatedDamageMirrorRecords()
        {
            while (_replicatedDamageMirrorRecords.Count > 0)
            {
                ReplicatedDamageMirrorRecord oldestRecord = _replicatedDamageMirrorRecords.Peek();
                if (Time.time <= oldestRecord.localTime + k_ReplicatedDamageMirrorTtlSeconds)
                {
                    break;
                }

                _replicatedDamageMirrorRecords.Dequeue();
            }
        }

        WeaponController.ReplicatedImpactType ResolveReplicatedImpactType(AuthoritativeShotResultPayload payload)
        {
            if (!payload.didHit)
            {
                return WeaponController.ReplicatedImpactType.None;
            }

            if (payload.targetType == ShotTargetType.Water || (_waterLayer >= 0 && payload.hitLayer == _waterLayer))
            {
                return WeaponController.ReplicatedImpactType.Water;
            }

            if (payload.targetType == ShotTargetType.Player || payload.targetType == ShotTargetType.Damageable)
            {
                return WeaponController.ReplicatedImpactType.Character;
            }

            return WeaponController.ReplicatedImpactType.World;
        }

        WeaponController.ImpactPresentationData BuildImpactPresentationData(WeaponController sourceWeapon)
        {
            if (sourceWeapon == null || sourceWeapon.ProjectilePrefab == null)
            {
                return default;
            }

            ProjectileStandard projectileStandard = sourceWeapon.ProjectilePrefab.GetComponent<ProjectileStandard>();
            if (projectileStandard == null)
            {
                return default;
            }

            return new WeaponController.ImpactPresentationData
            {
                ImpactVfxPrefab = projectileStandard.ImpactVfx,
                ImpactVfxLifetime = projectileStandard.ImpactVfxLifetime,
                ImpactVfxSpawnOffset = projectileStandard.ImpactVfxSpawnOffset,
                ImpactSfxClip = projectileStandard.ImpactSfxClip
            };
        }

        static AudioClip ResolveReplicatedFireSfxClip(WeaponController weapon)
        {
            if (weapon == null)
            {
                return null;
            }

            if (weapon.ShootSfx != null)
            {
                return weapon.ShootSfx;
            }

            if (weapon.ContinuousShootStartSfx != null)
            {
                return weapon.ContinuousShootStartSfx;
            }

            return null;
        }

        void EnsurePredictedUseConsistency()
        {
            if (!_primaryHeld || _predictedPrimaryWeapon == null || _weaponsManager == null)
            {
                return;
            }

            WeaponController activeWeapon = _weaponsManager.GetActiveWeapon();
            if (activeWeapon == _predictedPrimaryWeapon)
            {
                return;
            }

            _predictedPrimaryWeapon.PlayLocalPredictedUseStop();
            _predictedPrimaryWeapon = activeWeapon;
        }

        WeaponUseIntentPayload BuildIntentPayload(WeaponUseKind useKind, WeaponUsePhase usePhase)
        {
            Transform aimTransform = ResolveAimTransform();
            int activeWeaponIndex = _weaponsManager != null ? _weaponsManager.ActiveWeaponIndex : -1;

            WeaponController activeWeapon = _weaponsManager != null ? _weaponsManager.GetActiveWeapon() : null;
            if (activeWeapon == null)
            {
                activeWeapon = _predictedPrimaryWeapon;
            }

            Vector3 aimDirection = aimTransform.forward;
            if (aimDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                aimDirection = transform.forward;
            }

            return new WeaponUseIntentPayload
            {
                sequenceId = ++_localSequenceId,
                useKind = useKind,
                usePhase = usePhase,
                weaponIndex = activeWeaponIndex,
                aimOrigin = aimTransform.position,
                aimDirection = aimDirection.normalized,
                chargeRatio = ResolveChargeRatioForPayload(activeWeapon),
                clientTime = Time.time
            };
        }

        Transform ResolveAimTransform()
        {
            if (_weaponsManager != null && _weaponsManager.WeaponCamera != null)
            {
                return _weaponsManager.WeaponCamera.transform;
            }

            if (_networkedPlayer != null &&
                _networkedPlayer.CharacterController != null &&
                _networkedPlayer.CharacterController.PlayerCamera != null)
            {
                return _networkedPlayer.CharacterController.PlayerCamera.transform;
            }

            return transform;
        }

        void TrackPredictedPrimaryShotSequence(int sequenceId)
        {
            PrunePredictedPrimaryShotSequences();

            _predictedPrimaryShotSequences.Enqueue(new PredictedShotSequenceRecord
            {
                sequenceId = sequenceId,
                localTime = Time.time
            });

            LogShotDebug($"Owner predicted shot seq={sequenceId}");
        }

        bool ConsumePredictedPrimaryShotSequence(int sequenceId)
        {
            PrunePredictedPrimaryShotSequences();

            bool found = false;
            int pending = _predictedPrimaryShotSequences.Count;
            for (int i = 0; i < pending; i++)
            {
                PredictedShotSequenceRecord record = _predictedPrimaryShotSequences.Dequeue();
                if (!found && record.sequenceId == sequenceId)
                {
                    found = true;
                    continue;
                }

                _predictedPrimaryShotSequences.Enqueue(record);
            }

            return found;
        }

        void PrunePredictedPrimaryShotSequences()
        {
            while (_predictedPrimaryShotSequences.Count > 0)
            {
                PredictedShotSequenceRecord oldestRecord = _predictedPrimaryShotSequences.Peek();
                if (Time.time <= oldestRecord.localTime + k_PredictedSequenceCacheTtlSeconds)
                {
                    break;
                }

                _predictedPrimaryShotSequences.Dequeue();
            }
        }

        void ClearPredictedPrimaryShotSequenceCache()
        {
            _predictedPrimaryShotSequences.Clear();
        }

        void StopAllLocalPredictedUse()
        {
            if (_predictedPrimaryWeapon != null)
            {
                _predictedPrimaryWeapon.PlayLocalPredictedUseStop();
                _predictedPrimaryWeapon = null;
            }

            if (_weaponsManager != null)
            {
                _weaponsManager.StopPredictedUseOnWeapon(_weaponsManager.GetActiveWeapon());
            }
        }

        static bool IsFiniteVector3(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float ResolveChargeRatioForPayload(WeaponController weapon)
        {
            if (weapon == null)
            {
                return 0f;
            }

            if (!weapon.IsCharging)
            {
                return Mathf.Clamp01(weapon.CurrentCharge);
            }

            if (weapon.MaxChargeDuration <= 0f)
            {
                return 1f;
            }

            float derivedCharge = (Time.time - weapon.LastChargeTriggerTimestamp) / weapon.MaxChargeDuration;
            return Mathf.Clamp01(Mathf.Max(weapon.CurrentCharge, derivedCharge));
        }

        void LogShotDebug(string message)
        {
            if (!_enableShotDebugLogs)
            {
                return;
            }

            Debug.Log($"[NetworkedWeaponDriver] {message}");
        }
    }
}
