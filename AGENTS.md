# AGENTS.md

This project is a C# .NET football simulation game with shared core simulation logic and a WPF desktop UI.

## Project Rules

- Keep simulation logic separate from UI.
- Reuse existing core simulation classes where possible.
- The current UI is WPF.
- Prefer beginner-friendly code and readable naming.
- Do not refactor unrelated files.
- Keep `MainWindow` simple.
- Use WPF code-behind before introducing MVVM.
- Show changed files only when making updates.
- Preserve existing simulation behavior unless explicitly requested.

## Architecture Guidance

- Put reusable game logic in `src/FootballSimulation.Core`.
- Put WPF UI code in `src/FootballSimulation.Wpf`.
- Keep console-specific code in `src/FootballSimulation.Console`.
- Avoid adding UI dependencies to core simulation classes.
- Avoid duplicating match, league, fixture, or team logic in the UI.

## Verification

- Run `dotnet build FootballSimulation.sln` after project-level changes.
- Run `dotnet test FootballSimulation.sln` after changing simulation logic.
- If the WPF app is running and locks build output, close it before rebuilding.
