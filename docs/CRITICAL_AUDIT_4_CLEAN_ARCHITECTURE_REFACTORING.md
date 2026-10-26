# Fundamental Structural Audit Report
## Startup Monitor (.NET 9 + WPF)
**Role:** Chief Software Architect | **Date:** 2025-08-24

---

## 1. Executive Summary
The `Startup Monitor` codebase exhibits classic monolithic growth patterns. `MainViewModel` (≈3,000 LOC) and `PipeServer` (≈2,000 LOC) violate **Single Responsibility Principle (SRP)**, creating a high-coupling, low-cohesion architecture that impedes testing, performance optimization, and parallel development. This report provides a Clean Architecture-aligned decomposition plan, a reactive service-UI protocol design, and a phased refactoring roadmap targeting .NET 9 / WPF best practices.

---

## 2. Current State Analysis

### 2.1 SRP Violations & Monolith Metrics
| Artifact | Lines of Code | Primary Responsibilities | Violations |
|----------|---------------|--------------------------|------------|
| `MainViewModel.cs` | ~3,000 | Tab management, network alerting, update checking, auto-start configuration, snapshot serialization, theme application, pipe coordination | 7 distinct responsibilities; **SRP violation (degree: 680%)** |
| `PipeServer.cs` | ~2,000 | Named-pipe server infrastructure, binary message framing, network monitoring, process heuristic evaluation, alert dispatching | Embedded business heuristics; **SRP + Modularity violation** |

**Consequences:**
- Non-testable units (ViewModels require UI thread context)
- Fragile merges; any change risks cascading breakage
- Polling-driven UI (`DispatcherTimer` every 2-3s) introduces latency and CPU waste
- Binary/JSON format coupling between `PipeServer` and UI is implicit and undocumented

---

## 3. Target Architecture Blueprint

### 3.1 Clean Architecture Layers (Updated for .NET 9)
```
+----------------------------------------------------------+
|                    PRESENTATION LAYER (WPF)            |
|  +---------------------+    +------------------------+|
|  |      Views (XAML)   |<-->|   ViewModels (thin)    ||-- Data-binding, UI state only
|  +----------+----------+    +-----------+------------+|
|            |                     |         |            |
|   +--------+--------+      +---------+--------+   |
|   |    ShellViewModel|     | TabViewModels    ||-- Orchestration, not logic
|   +------+-----------+     +-------+----------+   |
+-------+-------------+           |        |            |
        |                   +---------+--------+        |
        |                   |         |            |
   +------+-------+   +---------+---------+   +--------+------+
   |   Core Library|   |Application Services|   |Infrastructure|
   | (Domain,      |   | (Use Cases,        |   | (Pipe, OS,   |
   |   Heuristics) |   |   Coordinators)    |   |   Serialization)|
   +---------------+   +---------------------+   +---------------+
                                   ^
                                   |
+----------------------------------------------------------+
```

### 3.2 Decomposed ViewModel Contracts
| Component | Responsibility | Key Dependencies |
|-----------|----------------|------------------|
| `ShellViewModel` | Shell navigation, global state, startup of subsystems, theme coordination | `IThemeService`, `IModuleLoader`, `IEventAggregator` |
| `NetworkTabViewModel` | Network status display, pipe connectivity, real-time graphs | `INetworkService`, `IObservable<NetworkState>` |
| `StartupTabViewModel` | Startup-item list, enable/disable, heuristic scores | `IStartupAnalyzer`, `IObservable<StartupItem>` |
| `AlertCoordinator` | Centralized alert production, deduplication, severity mapping | `INetworkHeuristic`, `IAlertPublisher` |
| `UpdateCoordinator` | Update-source checking, semver validation, UI notification | `IUpdateChecker