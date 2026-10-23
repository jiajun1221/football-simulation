# Gates: football realism

OWNS: GOAL.md, .unlazy/football-realism/**, src/FootballSimulation.Core/**, tests/FootballSimulation.Console.Tests/**

Scope: audit, minimally improve, statistically validate, and report live-match physics and randomness realism

- [ ] G1: requested feature inventory and physics/randomness scorecard are evidence-backed
  EVIDENCE: pending

- [ ] G2: a reproducible benchmark simulates at least 500 matches and emits independently measured metrics
  EVIDENCE: pending

- [ ] G3: seeded physics/randomness improvements have focused regression coverage
  EVIDENCE: pending

- [ ] G4: complete solution builds
  CHECK: dotnet build FootballSimulation.sln --nologo
  EXPECT: Build succeeded.
  EVIDENCE: pending

- [ ] G5: complete solution test suite passes
  CHECK: dotnet test FootballSimulation.sln --nologo --no-build
  EXPECT: Passed!
  EVIDENCE: pending

- [ ] G6: final six-section report includes before/after metrics and explicit out-of-scope gaps
  EVIDENCE: pending
