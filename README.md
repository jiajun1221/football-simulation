# Football Simulation

A C# .NET football simulation game with a Windows desktop interface.

## Repository layout

- `src/FootballSimulation.Core` - reusable simulation logic with categorized engines, models, services, data contracts, and canonical JSON data
- `src/FootballSimulation.Wpf` - Windows desktop UI with categorized controls, helpers, models, services, and workflow views
- `src/FootballSimulation.Console` - text-based UI
- `src/FootballSimulation.Api` - HTTP API
- `src/FootballSimulation.React` - experimental web client (not part of the .NET solution)
- `tests/FootballSimulation.Console.Tests` - automated tests for simulation and console behavior
- `tools/data-maintenance` - one-off scripts for refreshing squad, rating, and badge data

Generated `bin`, `obj`, and `artifacts` directories are intentionally excluded from source control. Run maintenance scripts from the repository root because their data paths are repository-relative.

## Requirements

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

Check that the SDK is installed:

```powershell
dotnet --version
```

## Run the desktop app

Open PowerShell in the repository root (`C:\Users\jchng\football-simulation`) and run:

```powershell
dotnet run --project .\src\FootballSimulation.Wpf\FootballSimulation.Wpf.csproj
```

The WPF game window should open automatically.

## Run from Visual Studio

1. Open `FootballSimulation.sln` in Visual Studio.
2. Set `FootballSimulation.Wpf` as the startup project.
3. Press `F5` to run with debugging, or `Ctrl+F5` to run without debugging.

## Build the solution

```powershell
dotnet build .\FootballSimulation.sln
```

## Run the console version

The repository also includes a text-based version of the game:

```powershell
dotnet run --project .\src\FootballSimulation.Console\FootballSimulation.Console.csproj
```

## Run tests

```powershell
dotnet test .\FootballSimulation.sln
```

## Data maintenance

The scripts in `tools/data-maintenance` update canonical files under `src/FootballSimulation.Core/Data/Json` or WPF club assets. For example:

```powershell
node .\tools\data-maintenance\update-2026-27-squads.mjs
```

Review a script's options before using it. Some scripts download external data or require an explicit `--write` argument.

## Troubleshooting

- If `dotnet` is not recognized, install the .NET 10 SDK and reopen PowerShell.
- The WPF app is Windows-only. The console project can run on other platforms that support .NET 10.
- If a build fails because files are locked, close the running app and try again.
