# Plan: football realism

## Shared contract

- Core simulation remains UI-independent and backward compatible.
- Seeded runs must be reproducible.
- Existing user changes in `MatchEngine.cs`, `MatchLiveView.xaml.cs`, and substitution stoppage files are preserved.
- New measurements must derive from simulation results, not copied target values.

## Depth tree

- root: audit, implementation, benchmark, integration report (OPEN)
  - leaf-audit: feature inventory and realism review (READY; read-only)
  - leaf-stats: benchmark feasibility and baseline metrics (READY; read-only)
  - leaf-implementation: minimal Core/test changes based on verified findings (WAITING on audit and stats)
  - node-integration: build, full test, 500+ post-change benchmark, final report (WAITING on implementation)

## Toolchain

- PowerShell working directory: repository root
- .NET solution: `FootballSimulation.sln`
- Required verification: `dotnet build FootballSimulation.sln`, then `dotnet test FootballSimulation.sln`
