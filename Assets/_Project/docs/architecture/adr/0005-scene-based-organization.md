# 0005. Scene-Based Code Organization

Date: 2026-01-17
Status: Accepted

## Context

The codebase needs clear organization for:
- Scene-specific functionality (lobby UI, game logic)
- Shared game systems (AI, combat, weapons)
- Infrastructure/services (networking, persistence)

Unity projects often become disorganized as they grow, with unclear boundaries between scene-specific and shared code.

## Decision

Organize scripts by responsibility with scene-based prefixes for scene-specific code:

```
Scripts/
├── Main/              # Persistent managers (SupremeManager, SceneLoader)
├── Service/           # Platform services (lobby, connectivity)
├── Game/              # Core gameplay systems (shared across modes)
│   ├── Managers/      # ActorsManager, EventManager, etc.
│   └── Shared/        # Health, Damageable, WeaponController
├── Gameplay/          # FPS gameplay (player, weapons, projectiles)
├── AI/                # Enemy AI systems
├── UI/                # UI components
├── InitScene/         # Initialization scene code
├── LobbyScene/        # Lobby scene code
├── GameScene/         # Game scene code
└── MainMenuScene/     # Main menu code
```

Key principles:
1. Scene-prefixed folders contain ONLY code specific to that scene
2. Shared code lives in Game/, Gameplay/, AI/, UI/
3. Infrastructure lives in Main/ and Service/

## Consequences

### Positive
- Clear ownership: easy to find scene-specific code
- Shared code is explicitly separate
- New team members can navigate easily
- Prevents accidental coupling between scenes

### Negative
- Some judgment calls on "shared vs scene-specific"
- Must move code when it becomes shared
- More folders to navigate

## Alternatives Considered

### Feature-Based Organization
- Pros: All related code together
- Cons: Harder to identify what's scene-specific
- Rejected: Scene boundaries are primary concern

### Single Flat Structure
- Pros: No folder decisions needed
- Cons: Doesn't scale, hard to navigate
- Rejected: Already over 100 scripts
