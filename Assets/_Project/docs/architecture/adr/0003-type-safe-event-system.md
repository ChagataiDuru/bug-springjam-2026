# 0003. Type-Safe Event System for Cross-System Communication

Date: 2026-01-17
Status: Accepted

## Context

Systems need to communicate without direct references:
- Enemy death should notify objectives, UI, and audio systems
- Pickup collection should update inventory and play effects
- Player death should trigger game flow changes

Problems with existing approaches:
- Direct references create tight coupling
- Unity Events require stringly-typed method names
- Singleton managers create global state issues

## Decision

Implement a static `EventManager` with generic methods keyed by event type:

```csharp
// Event definition
public class EnemyKillEvent : GameEvent {
    public GameObject Enemy;
    public int RemainingEnemyCount;
}

// Subscribe
EventManager.AddListener<EnemyKillEvent>(OnEnemyKilled);

// Broadcast (zero-allocation singleton pattern)
EnemyKillEvent evt = Events.EnemyKillEvent;
evt.Enemy = this.gameObject;
EventManager.Broadcast(evt);
```

Key features:
- Compile-time type safety
- Zero allocation via singleton event instances
- Listeners stored by event type in dictionary

## Consequences

### Positive
- Fully decoupled systems
- Type-safe subscriptions (no stringly-typed APIs)
- Zero GC allocation per broadcast
- Easy to trace: search for event type usage

### Negative
- Static global state requires clearing on scene unload
- Listeners MUST unsubscribe in OnDestroy (memory leak risk)
- No guaranteed event ordering

## Alternatives Considered

### ScriptableObject Events
- Pros: Inspector-visible, asset-based
- Cons: Allocates per broadcast, harder to trace in code
- Rejected: Performance and debugging concerns

### C# Events on Managers
- Pros: Standard C# pattern
- Cons: Requires direct reference to event source
- Rejected: Doesn't solve coupling problem
