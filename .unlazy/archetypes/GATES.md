# Gates: expanded personality archetypes

OWNS: src/FootballSimulation.Core/Services/Mental/**, src/FootballSimulation.Wpf/Views/Squad/DressingRoomView.xaml.cs, tests/FootballSimulation.Console.Tests/MentalSystemTests.cs

Scope: add ordered, configurable archetypes with bounded existing-system effects, UI labels, distribution reporting, and tests

- [ ] G1: archetype boundary, priority, fallback, and distribution tests pass
  CHECK: dotnet test tests/FootballSimulation.Console.Tests/FootballSimulation.Console.Tests.csproj --no-restore --filter Category=MentalSystem
  EXPECT: Failed:     0
  EVIDENCE: pending

- [ ] G2: the complete solution builds successfully
  CHECK: dotnet build FootballSimulation.sln --no-restore
  EXPECT: Build succeeded.
  EVIDENCE: pending

- [ ] G3: distribution report measures Balanced below 20 percent and every named archetype below 15 percent in a 10000-profile sample
  CHECK: dotnet test tests/FootballSimulation.Console.Tests/FootballSimulation.Console.Tests.csproj --no-restore --filter FullyQualifiedName~ArchetypeDistribution
  EXPECT: Failed:     0
  EVIDENCE: pending

- [ ] G4: UI displays primary archetype pills with distinct new colors and a secondary detail tag
  EVIDENCE: pending
