# 0001. Supreme Manager as Persistent Singleton Orchestrator

Date: 2026-01-17
Status: Accepted

## Context

The game requires persistent state management across scene transitions. Services (networking, lobby, etc.) need to survive scene loads and maintain consistent state. Unity's default scene lifecycle destroys GameObjects on scene unload.

Key requirements:
- Services must persist across scenes
- Single point of coordination for scene loading
- Centralized popup/dialog system
- Clean separation between persistent and scene-specific code

## Decision

Implement a `SupremeManager` as a persistent singleton that:
- Lives in a dedicated "Supreme" scene that is never unloaded
- Manages all services via `ServiceManager`
- Handles both local and networked scene loading
- Provides centralized popup/dialog APIs

```
Supreme Scene (persistent, never unloaded)
└── SupremeManager
    ├── ServiceManager
    ├── SceneLoader
    └── PopupManager
```

## Consequences

### Positive
- Services survive scene transitions automatically
- Single source of truth for scene loading logic
- Centralized error handling and connectivity monitoring
- Clear separation: Supreme = infrastructure, other scenes = gameplay

### Negative
- All scene loads must go through SupremeManager
- Harder to test individual scenes in isolation
- Potential for SupremeManager to become a "god object" if not careful

## Alternatives Considered

### DontDestroyOnLoad for Each Manager
- Pros: Simpler, no orchestrator needed
- Cons: Hard to coordinate initialization order, race conditions
- Rejected: Multiple persistent roots become unwieldy

### ScriptableObject-Based Service Locator
- Pros: Decoupled, Unity-native
- Cons: Async initialization is complex, lifecycle less clear
- Rejected: Service initialization requires async patterns
