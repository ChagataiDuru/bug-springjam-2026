using UnityEngine;

namespace Taiyun.SuckTheWater.Game
{
    /// <summary>
    /// Card 5: Data carried from the authoritative hit/damage flow to the creature's reaction entry point.
    /// Passed host-only to NetworkedEnemySync.ApplyAuthoritativeHitReaction().
    /// </summary>
    [System.Serializable]
    public struct EnemyHitReactionContext
    {
        /// <summary>World-space point where the shot landed on the creature.</summary>
        public Vector3 hitPoint;

        /// <summary>
        /// Direction to push the creature (normalized). Computed by the resolver from
        /// attacker-to-target vector, flattened to horizontal. Zero = no knockback.
        /// </summary>
        public Vector3 knockbackDirection;

        /// <summary>Knockback impulse speed (m/s). 0 = no knockback requested.</summary>
        public float knockbackStrength;

        /// <summary>Stun duration in seconds. 0 = no stun requested.</summary>
        public float stunDuration;

        /// <summary>Matches the originating shot sequenceId for tracing and dedup.</summary>
        public int sequenceId;
    }

    /// <summary>
    /// Card 5: Interface for host-authoritative hit-reaction entry points on a creature object.
    ///
    /// Lives in the shared Game assembly so both:
    ///   - Gameplay assembly (AuthoritativeDamageResolver / NetworkedEnemySync) can call it, and
    ///   - AI assembly (EnemyController) can implement it,
    /// without creating a circular assembly dependency.
    ///
    /// Rule: every method on this interface is called from NetworkedEnemySync (host or client
    /// depending on method). None of these methods may contain gameplay-truth mutations that
    /// bypass the host-authoritative path.
    /// </summary>
    public interface IEnemyHitReactionHost
    {
        /// <summary>
        /// True if the creature is dead or in its death sequence.
        /// Reaction application should be skipped when this is true.
        /// </summary>
        bool IsDeadOrDying { get; }

        /// <summary>
        /// True when the creature is currently stunned (set by stun begin/expire callbacks).
        /// Read by EnemyMobile to suppress AI state machine execution.
        /// </summary>
        bool IsStunned { get; }

        /// <summary>
        /// Called on the host when authoritative knockback begins.
        /// Use for animation triggers or other host-side state setup.
        /// Must NOT apply movement — NetworkedEnemySync owns displacement.
        /// </summary>
        void OnHitReactionKnockbackBegin();

        /// <summary>
        /// Called on the host when knockback displacement finishes and the NavAgent resumes.
        /// </summary>
        void OnHitReactionKnockbackEnd();

        /// <summary>
        /// Called on host AND clients (via SyncVar mirror) when stun begins.
        /// Sets IsStunned = true on the implementor so EnemyMobile can gate its AI.
        /// </summary>
        void OnHitReactionStunBegin();

        /// <summary>
        /// Called on host AND clients (via SyncVar mirror) when stun expires.
        /// Sets IsStunned = false.
        /// </summary>
        void OnHitReactionStunExpired();

        /// <summary>
        /// Called on observer clients only via ObserversRpc for presentation-only feedback.
        /// Must NOT apply any gameplay-truth state changes — purely visual/audio.
        /// </summary>
        void PlayHitReactionPresentation(Vector3 hitPoint, Vector3 knockbackDirection);
    }
}
