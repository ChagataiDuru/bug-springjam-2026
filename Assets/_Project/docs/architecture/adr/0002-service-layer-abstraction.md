# 0002. Service Layer with Interface-Based Abstraction

Date: 2026-01-17
Status: Accepted

## Context

The game needs multiple services (connectivity checking, lobby management, platform-specific features) that:
- Follow a consistent lifecycle (Init → Start → Stop)
- Can be mocked for testing
- Support platform-specific implementations (Steam, Epic, etc.)
- Allow async initialization

## Decision

Implement a service abstraction layer:

1. **IService interface** - Common lifecycle contract
   ```csharp
   interface IService {
       Task<bool> InitService(params object[] args);
       Task<bool> StartService();
       Task<bool> StopService();
   }
   ```

2. **ServiceManager** - Coordinates all services
   - Owns service instances
   - Sequential async initialization
   - Update loop for services needing per-frame processing

3. **Platform providers** - Hidden behind interfaces
   - `ILobbyProvider` implemented by `SteamLobbyProvider`
   - Game code uses `LobbyManager`, unaware of Steam-specific details

## Consequences

### Positive
- Platform code is isolated from game logic
- Easy to add new platforms (Epic, GOG) without touching game code
- Services can be tested with mock implementations
- Clear initialization order and error handling

### Negative
- More interfaces and indirection
- Async initialization adds complexity
- ServiceManager must be updated when adding new services

## Alternatives Considered

### Direct Platform API Calls
- Pros: Simpler, no abstraction overhead
- Cons: Platform code scattered everywhere, hard to add platforms
- Rejected: Not scalable for multi-platform

### Dependency Injection Framework
- Pros: Industry standard, flexible
- Cons: Overkill for current project size, learning curve
- Rejected: Added complexity not justified yet
