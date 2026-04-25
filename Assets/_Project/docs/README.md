# Project Documentation

Navigate technical documentation for the Suck The Water project.

## Architecture Decision Records

| ADR | Title | Status |
|-----|-------|--------|
| [0001](architecture/adr/0001-supreme-manager-singleton.md) | Supreme Manager as Persistent Singleton Orchestrator | Accepted |
| [0002](architecture/adr/0002-service-layer-abstraction.md) | Service Layer with Interface-Based Abstraction | Accepted |
| [0003](architecture/adr/0003-type-safe-event-system.md) | Type-Safe Event System for Cross-System Communication | Accepted |
| [0004](architecture/adr/0004-steam-networking-platform.md) | Steam as Primary Networking Platform | Accepted |
| [0005](architecture/adr/0005-scene-based-organization.md) | Scene-Based Code Organization | Accepted |

## Documentation Structure

```
docs/
├── architecture/adr/    # Architecture Decision Records
├── domain/              # Business domain (glossary, models)
├── systems/             # Per-system documentation
└── workflows/           # Process and state machine docs
```

## Key Architectural Concepts

- **SupremeManager**: Persistent singleton orchestrating services and scene loading
- **ServiceManager**: Manages async service lifecycle with platform abstraction
- **EventManager**: Type-safe decoupled communication between systems
- **Scene Organization**: Code separated by scene ownership vs shared systems
