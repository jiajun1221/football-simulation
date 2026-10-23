# Gates: mental and personality system

OWNS: src/FootballSimulation.Core/**, src/FootballSimulation.Wpf/Views/Squad/**, tests/FootballSimulation.Console.Tests/**, .unlazy/mental-system/**

Scope: add a feature-gated mental, personality, relationship, chemistry, dressing-room, persistence, and Squad-tab system without changing disabled simulation behavior

- [x] G1: the complete solution builds
  CHECK: dotnet build FootballSimulation.sln --nologo --no-restore
  EXPECT: Build succeeded.
  EVIDENCE: exit=0; PowerShell; repository root; Build succeeded with 0 errors and 2 NU1900 vulnerability-feed warnings

- [x] G2: the complete automated test suite passes
  CHECK: dotnet test FootballSimulation.sln --nologo --no-build
  EXPECT: Passed!
  EVIDENCE: exit=0; PowerShell; repository root; Passed 522, failed 0, skipped 0 in 12m20s with the Morale feature enabled by default

- [x] G3: feature-off seeded simulations remain identical and mental model unit tests pass
  CHECK: dotnet test tests\FootballSimulation.Console.Tests\FootballSimulation.Console.Tests.csproj --nologo --no-build --filter Category=MentalSystem
  EXPECT: Passed!
  EVIDENCE: exit=0; PowerShell; repository root; focused mental-system tests passed

- [x] G4: old saves load with safe mental defaults and new saves round-trip
  CHECK: dotnet test tests\FootballSimulation.Console.Tests\FootballSimulation.Console.Tests.csproj --nologo --no-build --filter Category=MentalSave
  EXPECT: Passed!
  EVIDENCE: exit=0; PowerShell; repository root; version-8 missing-field migration and version-9 round-trip passed

- [x] G5: 500-match feature-off and feature-on benchmark remains reproducible and within agreed aggregate tolerances
  CHECK: dotnet test tests\FootballSimulation.Console.Tests\FootballSimulation.Console.Tests.csproj --nologo --no-build --filter Category=MentalBenchmark --logger "console;verbosity=detailed"
  EXPECT: Passed!
  EVIDENCE: exit=0; PowerShell; repository root; 500 seeds per mode passed all 5% distribution checks and the performance bound

- [x] G6: the Morale tab is positioned between Squad and Formation and hidden when disabled
  EVIDENCE: inspected SquadOverviewView XAML/code-behind; order is Squad, Morale, Formation, Statistics and both Morale controls collapse when MentalSystemSettings.IsEnabled is false

