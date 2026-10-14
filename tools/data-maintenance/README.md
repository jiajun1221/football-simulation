# Data maintenance tools

Run these scripts from the repository root. They use repository-relative paths and are not required to build or run the game.

- `update-2026-27-squads.mjs` refreshes 2026-27 squad data.
- `update-epl-squads.mjs` refreshes Premier League squad data.
- `update-fc26-ratings.mjs` refreshes player ratings.
- `import-attached-2026-27-rosters.mjs` imports supplied roster and transfer files; it writes only when passed `--write`.
- `download-2026-27-badges.mjs` downloads club badge assets.

Review the inputs and resulting diff before committing regenerated data or assets.
