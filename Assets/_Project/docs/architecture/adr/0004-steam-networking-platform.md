# 0004. Steam as Primary Networking Platform

Date: 2026-01-17
Status: Accepted

## Context

The game requires multiplayer functionality:
- Lobby creation/joining
- Player matchmaking
- Session management

Target platforms: Windows, Mac, Linux (PC only for initial release).

Evaluation criteria:
- Existing platform infrastructure
- Development and hosting costs
- Unity integration quality
- Player base accessibility

## Decision

Use Steam as the primary networking platform with:
- `SteamLobbyProvider` implementing `ILobbyProvider` interface
- Lobby management through Steam's P2P infrastructure
- Conditional compilation for platform-specific code

```csharp
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
    _lobbyProvider = new SteamLobbyProvider();
#endif
```

Future platforms (Epic, GOG) can be added by implementing `ILobbyProvider`.

## Consequences

### Positive
- Zero hosting costs (Steam handles relay)
- Large existing player base
- Excellent P2P infrastructure
- Built-in friend system and invites

### Negative
- Steam dependency for PC release
- 30% revenue share
- Limited to Steam players (no cross-store play initially)
- Requires Steamworks SDK integration

## Alternatives Considered

### Photon (PUN/Fusion)
- Pros: Cross-platform, proven at scale
- Cons: Monthly hosting costs, vendor lock-in
- Rejected: Cost not justified for indie scale

### Unity Relay + Lobby Services
- Pros: Platform-agnostic, Unity-native
- Cons: Less mature, additional costs at scale
- Rejected: Steam provides better value for PC-only

### Self-Hosted Dedicated Servers
- Pros: Full control, no platform dependency
- Cons: High ops burden, hosting costs
- Rejected: Ops overhead too high for team size
