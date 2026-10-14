# Gates: source-folder categorization

OWNS: src/**, README.md, GATES.md

Scope: categorize remaining source files by responsibility without changing namespaces, runtime resource paths, or behavior

- [x] G1: every multi-file C# or WPF source area is grouped into documented responsibility folders
  EVIDENCE: Audited Models, Engine, JsonModels, WPF Models, Helpers, Controls, Services, and Views; no C# or XAML source files remain directly in those category roots. Each area has a README describing its responsibility folders.

- [x] G2: moved files retain their existing namespaces and XAML class identities
  EVIDENCE: Namespace audit found all 40 Core models, 11 engine classes, 11 JSON contracts, 8 WPF models, 7 helpers, and 3 controls in their original namespaces. Every moved XAML file has its adjacent code-behind pair; no x:Class values were edited.

- [x] G3: the complete .NET solution builds after all structural moves
  CHECK: dotnet build FootballSimulation.sln --nologo --no-restore
  EXPECT: Build succeeded.
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\jchng\football-simulation; path=7b4bc2d71bbe/17 entries; output=0 Error(s) | Time Elapsed 00:00:21.65

- [x] G4: the complete .NET test suite passes because Core model and engine paths changed
  CHECK: dotnet test FootballSimulation.sln --nologo --no-build
  EXPECT: Passed!
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\jchng\football-simulation; path=7b4bc2d71bbe/17 entries; output=A total of 1 test files matched the specified pattern. | Passed!  - Failed:     0, Passed:   491, Skipped:     0, Total:   491, Duration: 9 m 35 s - FootballSimulation.Console.Tests.dll (net10.0)
