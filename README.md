# Football Simulation

A C# .NET football simulation game with a Windows desktop interface.

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

## Troubleshooting

- If `dotnet` is not recognized, install the .NET 10 SDK and reopen PowerShell.
- The WPF app is Windows-only. The console project can run on other platforms that support .NET 10.
- If a build fails because files are locked, close the running app and try again.
