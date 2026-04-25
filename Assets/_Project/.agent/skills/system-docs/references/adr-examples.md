# ADR Examples

Concrete examples of Architecture Decision Records for common scenarios.

## Example 1: Entity System Choice

```markdown
# 0001. Use ECS for Enemy AI State Management

Date: 2025-03-15
Status: Accepted

## Context

Our enemy AI system needs to manage state for hundreds of concurrent enemies.
The current MonoBehaviour-based approach causes performance issues at scale
due to individual Update() calls and scattered memory access patterns.

Unity's DOTS/ECS provides data-oriented design with burst compilation and
job system integration.

## Decision

We will use Unity ECS (Entities package) for enemy AI state management
while keeping MonoBehaviour wrappers for editor tooling and inspector access.

Hybrid approach:
- ECS systems process AI state transitions
- MonoBehaviour wraps IComponentData for inspector editing
- Conversion workflow syncs changes at runtime

## Consequences

### Positive
- 10x performance improvement for 500+ enemies
- Cache-friendly memory layout
- Enables burst-compiled pathfinding

### Negative
- Steeper learning curve for team
- More complex debugging (no inspector by default)
- Hybrid pattern adds indirection

## Alternatives Considered

### Pure MonoBehaviour with Object Pooling
- Pros: Familiar, good tooling
- Cons: Still per-object Update overhead
- Rejected: Performance ceiling too low

### Custom State Machine with Arrays
- Pros: Simpler than full ECS
- Cons: No burst, manual memory management
- Rejected: Re-inventing ECS poorly
```

---

## Example 2: Event System Architecture

```markdown
# 0002. Implement Type-Safe Event System

Date: 2025-03-20
Status: Accepted

## Context

Cross-system communication currently uses:
1. Direct references (creates coupling)
2. Unity events (stringly-typed, error-prone)
3. Singleton managers (global state issues)

We need a decoupled communication pattern that:
- Is type-safe at compile time
- Supports multiple listeners
- Has minimal allocation overhead
- Is easy to debug

## Decision

Implement a static EventManager with generic Add/Remove/Broadcast methods
keyed by event type. Event data is carried in GameEvent subclasses.

```csharp
// Usage
EventManager.AddListener<EnemyKillEvent>(OnEnemyKilled);
EventManager.Broadcast(Events.EnemyKillEvent);
```

## Consequences

### Positive
- Compile-time type safety
- Zero allocation for event data (singleton pattern)
- Easy to trace listeners in code search
- Decoupled systems

### Negative
- Static global state (must clear on scene unload)
- Listeners must remember to unsubscribe
- No event ordering guarantees

## Alternatives Considered

### ScriptableObject Events
- Pros: Inspector-visible, asset-based
- Cons: Allocation per broadcast, harder to trace
- Rejected: Performance and debugging concerns

### C# Events on Managers
- Pros: Standard C# pattern
- Cons: Still requires direct reference to manager
- Rejected: Doesn't solve coupling issue
```

---

## Example 3: Third-Party Library Decision

```markdown
# 0003. Use Photon for Multiplayer Networking

Date: 2025-04-01
Status: Accepted

## Context

The game requires real-time multiplayer for 2-8 players with:
- Low latency (< 100ms for gameplay)
- Host migration if host disconnects
- Cross-platform (PC, mobile, console)

Budget: $500/month for hosting at 1000 CCU.

## Decision

Use Photon Fusion for networking layer with interest management
for scalability.

## Consequences

### Positive
- Proven at scale (millions of users)
- Excellent Unity integration
- Built-in matchmaking
- Host migration supported

### Negative
- Monthly cost scales with CCU
- Vendor lock-in (proprietary protocol)
- Limited server-side logic without custom plugins

## Alternatives Considered

### Mirror (Open Source)
- Pros: Free, open source, familiar API
- Cons: Self-hosted complexity, no managed infrastructure
- Rejected: Ops overhead too high for team size

### Unity Netcode for GameObjects
- Pros: Official Unity support
- Cons: Less mature, feature gaps vs Photon
- Rejected: Missing host migration, matchmaking
```

---

## ADR Lifecycle

1. **Proposed**: Under discussion, may change
2. **Accepted**: Decision finalized and being implemented
3. **Deprecated**: No longer applies (context changed)
4. **Superseded by ADR-XXXX**: Replaced by newer decision
