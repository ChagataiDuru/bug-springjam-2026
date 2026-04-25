using System;
using System.Collections.Generic;
using Taiyun.SuckTheWater.Game;
using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    public enum DamageTargetKind
    {
        None,
        Player,
        Enemy,
        Damageable
    }

    public enum DamageRejectReason
    {
        None,
        NotServerAuthority,
        InvalidContext,
        InvalidDamageAmount,
        MissingTarget,
        DuplicateRequest,
        TargetAlreadyDead,
        UnsupportedTarget,
        ApplyFailed
    }

    public enum DamageSourceKind
    {
        Unknown,
        WeaponShot,
        Explosion,
        Projectile,
        Environment
    }

    [Serializable]
    public struct AuthoritativeDamageContext
    {
        public int sequenceId;
        public ulong attackerClientId;
        public bool isServerAuthority;
        public GameObject attackerObject;
        public Damageable targetDamageable;
        public float baseDamage;
        public DamageSourceKind sourceKind;
        public int weaponIndex;
        public NetworkedWeaponDriver.WeaponUseKind useKind;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
        public bool allowKnockback;
        public bool allowStun;
        // Card 5: reaction parameters (used when allowKnockback/allowStun are true)
        public float knockbackStrength;
        public float stunDuration;
        /// <summary>World-space position of the attacker, used to compute knockback direction.</summary>
        public Vector3 reactionSourcePosition;
    }

    [Serializable]
    public struct AuthoritativeDamageResult
    {
        public bool success;
        public DamageRejectReason reason;
        public DamageTargetKind targetKind;
        public float appliedDamage;
        public bool killed;
        public int sequenceId;
        /// <summary>Card 5: True if knockback/stun reaction was dispatched to NetworkedEnemySync.</summary>
        public bool reactionApplied;
    }

    public sealed class AuthoritativeDamageResolver
    {
        struct DamageDuplicateKey : IEquatable<DamageDuplicateKey>
        {
            public ulong attackerClientId;
            public int sequenceId;
            public int targetInstanceId;

            public bool Equals(DamageDuplicateKey other)
            {
                return attackerClientId == other.attackerClientId &&
                       sequenceId == other.sequenceId &&
                       targetInstanceId == other.targetInstanceId;
            }

            public override bool Equals(object obj)
            {
                return obj is DamageDuplicateKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = attackerClientId.GetHashCode();
                    hash = (hash * 397) ^ sequenceId;
                    hash = (hash * 397) ^ targetInstanceId;
                    return hash;
                }
            }
        }

        struct DamageDuplicateRecord
        {
            public DamageDuplicateKey key;
            public float recordedAt;
        }

        readonly bool _enableDebugLogs;
        readonly Queue<DamageDuplicateRecord> _duplicateRecordQueue = new Queue<DamageDuplicateRecord>();
        readonly HashSet<DamageDuplicateKey> _duplicateKeys = new HashSet<DamageDuplicateKey>();

        const float k_DuplicateRecordTtlSeconds = 2f;

        public AuthoritativeDamageResolver(bool enableDebugLogs)
        {
            _enableDebugLogs = enableDebugLogs;
        }

        public bool TryResolveAndApply(in AuthoritativeDamageContext context, out AuthoritativeDamageResult result)
        {
            PruneDuplicateRecords();

            result = BuildRejectedResult(context.sequenceId, DamageRejectReason.InvalidContext, DamageTargetKind.None);
            Log(
                $"Request seq={context.sequenceId}, attacker={context.attackerClientId}, source={context.sourceKind}, baseDamage={context.baseDamage:0.##}");

            if (!context.isServerAuthority)
            {
                result.reason = DamageRejectReason.NotServerAuthority;
                Log($"Rejected seq={context.sequenceId}: not server authority");
                return false;
            }

            if (context.targetDamageable == null)
            {
                result.reason = DamageRejectReason.MissingTarget;
                Log($"Rejected seq={context.sequenceId}: missing target damageable");
                return false;
            }

            if (context.baseDamage <= 0f || float.IsNaN(context.baseDamage) || float.IsInfinity(context.baseDamage))
            {
                result.reason = DamageRejectReason.InvalidDamageAmount;
                Log($"Rejected seq={context.sequenceId}: invalid damage={context.baseDamage}");
                return false;
            }

            if (!context.targetDamageable.gameObject.scene.IsValid())
            {
                result.reason = DamageRejectReason.InvalidContext;
                Log($"Rejected seq={context.sequenceId}: target scene invalid");
                return false;
            }

            DamageDuplicateKey duplicateKey;
            bool shouldGuardDuplicates = TryBuildDuplicateKey(context, out duplicateKey);
            if (shouldGuardDuplicates && IsDuplicateRequest(duplicateKey))
            {
                result.reason = DamageRejectReason.DuplicateRequest;
                Log($"Rejected seq={context.sequenceId}: duplicate request");
                return false;
            }

            result.targetKind = ClassifyTarget(context.targetDamageable, out NetworkedPlayerController targetPlayer);

            if (result.targetKind == DamageTargetKind.None)
            {
                result.reason = DamageRejectReason.UnsupportedTarget;
                Log($"Rejected seq={context.sequenceId}: unsupported target");
                return false;
            }

            bool applied;
            switch (result.targetKind)
            {
                case DamageTargetKind.Player:
                    applied = targetPlayer != null &&
                              targetPlayer.TryApplyAuthoritativeDamage(
                                  context.baseDamage,
                                  context.attackerObject,
                                  out result.appliedDamage,
                                  out result.killed);
                    break;
                case DamageTargetKind.Enemy:
                    applied = context.targetDamageable.TryApplyAuthoritativeDamage(
                        context.baseDamage,
                        context.sourceKind == DamageSourceKind.Explosion,
                        context.attackerObject,
                        out result.appliedDamage,
                        out result.killed);
                    // Card 5: route authoritative hit reaction if damage landed and target survived
                    if (applied && !result.killed && (context.allowKnockback || context.allowStun))
                    {
                        TryApplyEnemyHitReaction(in context);
                        result.reactionApplied = true;
                    }
                    break;
                case DamageTargetKind.Damageable:
                    applied = context.targetDamageable.TryApplyAuthoritativeDamage(
                        context.baseDamage,
                        context.sourceKind == DamageSourceKind.Explosion,
                        context.attackerObject,
                        out result.appliedDamage,
                        out result.killed);
                    break;
                default:
                    applied = false;
                    result.appliedDamage = 0f;
                    result.killed = false;
                    break;
            }

            result.success = applied;
            result.reason = applied
                ? DamageRejectReason.None
                : (result.killed ? DamageRejectReason.TargetAlreadyDead : DamageRejectReason.ApplyFailed);

            if (!applied)
            {
                if (shouldGuardDuplicates)
                {
                    RemoveDuplicateKey(duplicateKey);
                }

                Log($"Rejected seq={context.sequenceId}: apply failed, target={result.targetKind}");
                return false;
            }

            Log(
                $"Accepted seq={context.sequenceId}, source={context.sourceKind}, target={result.targetKind}, damage={result.appliedDamage:0.##}, killed={result.killed}");
            return true;
        }

        static AuthoritativeDamageResult BuildRejectedResult(int sequenceId, DamageRejectReason reason,
            DamageTargetKind targetKind)
        {
            return new AuthoritativeDamageResult
            {
                success = false,
                reason = reason,
                targetKind = targetKind,
                appliedDamage = 0f,
                killed = false,
                sequenceId = sequenceId
            };
        }

        bool TryBuildDuplicateKey(in AuthoritativeDamageContext context, out DamageDuplicateKey key)
        {
            key = default;

            if (context.sequenceId <= 0 || context.targetDamageable == null)
            {
                return false;
            }

            key = new DamageDuplicateKey
            {
                attackerClientId = context.attackerClientId,
                sequenceId = context.sequenceId,
                targetInstanceId = context.targetDamageable.GetInstanceID()
            };

            return true;
        }

        bool IsDuplicateRequest(in DamageDuplicateKey key)
        {
            if (_duplicateKeys.Contains(key))
            {
                return true;
            }

            _duplicateKeys.Add(key);
            _duplicateRecordQueue.Enqueue(new DamageDuplicateRecord
            {
                key = key,
                recordedAt = Time.time
            });
            return false;
        }

        void RemoveDuplicateKey(in DamageDuplicateKey key)
        {
            _duplicateKeys.Remove(key);
        }

        void PruneDuplicateRecords()
        {
            while (_duplicateRecordQueue.Count > 0)
            {
                DamageDuplicateRecord oldestRecord = _duplicateRecordQueue.Peek();
                if (Time.time <= oldestRecord.recordedAt + k_DuplicateRecordTtlSeconds)
                {
                    break;
                }

                _duplicateRecordQueue.Dequeue();
                _duplicateKeys.Remove(oldestRecord.key);
            }
        }

        static DamageTargetKind ClassifyTarget(Damageable targetDamageable, out NetworkedPlayerController targetPlayer)
        {
            targetPlayer = targetDamageable != null
                ? targetDamageable.GetComponentInParent<NetworkedPlayerController>()
                : null;

            if (targetPlayer != null)
            {
                return DamageTargetKind.Player;
            }

            if (targetDamageable == null)
            {
                return DamageTargetKind.None;
            }

            if (HasEnemyControllerInParent(targetDamageable.transform))
            {
                return DamageTargetKind.Enemy;
            }

            if (targetDamageable.Health != null || targetDamageable.GetComponentInParent<Health>() != null)
            {
                return DamageTargetKind.Damageable;
            }

            return DamageTargetKind.None;
        }

        static bool HasEnemyControllerInParent(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                if (current.GetComponent("EnemyController") != null)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// Card 5: Called on the host after a confirmed enemy damage application.
        /// Locates NetworkedEnemySync on the target hierarchy and dispatches an
        /// EnemyHitReactionContext so knockback/stun are applied through the
        /// host-authoritative creature reaction path.
        /// </summary>
        void TryApplyEnemyHitReaction(in AuthoritativeDamageContext context)
        {
            // NetworkedEnemySync lives in the same Gameplay assembly — direct reference is fine.
            NetworkedEnemySync enemySync = context.targetDamageable.GetComponentInParent<NetworkedEnemySync>();
            if (enemySync == null)
            {
                Log($"[Reaction] seq={context.sequenceId}: no NetworkedEnemySync found on enemy target — reaction skipped");
                return;
            }

            // Compute knockback direction: from attacker toward creature, flattened to horizontal
            // so it doesn't push enemies into the ground or sky.
            Vector3 toTarget = context.targetDamageable.transform.position - context.reactionSourcePosition;
            toTarget.y = 0f;

            Vector3 knockbackDir;
            if (toTarget.sqrMagnitude > 0.001f)
            {
                knockbackDir = toTarget.normalized;
            }
            else
            {
                // Fallback: use inverse of hit normal when attacker and target share the same XZ pos
                knockbackDir = -context.hitNormal;
                knockbackDir.y = 0f;
                knockbackDir = knockbackDir.sqrMagnitude > 0.001f ? knockbackDir.normalized : Vector3.forward;
            }

            var reactionContext = new Game.EnemyHitReactionContext
            {
                hitPoint           = context.hitPoint,
                knockbackDirection = knockbackDir,
                knockbackStrength  = context.allowKnockback ? context.knockbackStrength : 0f,
                stunDuration       = context.allowStun      ? context.stunDuration      : 0f,
                sequenceId         = context.sequenceId
            };

            enemySync.ApplyAuthoritativeHitReaction(reactionContext);
            Log($"[Reaction] seq={context.sequenceId}: dispatched to NetworkedEnemySync — kb={reactionContext.knockbackStrength:0.##}, stun={reactionContext.stunDuration:0.##}s");
        }

        void Log(string message)
        {
            if (!_enableDebugLogs)
            {
                return;
            }

            Debug.Log($"[AuthoritativeDamageResolver] {message}");
        }
    }
}
