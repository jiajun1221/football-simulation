# Goal: live-match realism review and improvement

## Objective

Audit the live football simulation, improve Physics and Randomness most deeply, tune only existing interactions in other areas, validate at least 500 matches, and report the six requested evidence-backed sections.

## Done Condition

- [ ] Every requested feature is classified as Implemented, Partial, or Missing with source references and parameters.
- [ ] Every Physics and Randomness sub-item has a current-model review, realism gap, randomness assessment, and 1-5 score.
- [ ] A reproducible 500+ match benchmark reports every measurable requested metric and flags unsupported metrics honestly.
- [ ] High-impact physics/randomness improvements are implemented in Core without UI dependencies or unrelated refactors.
- [ ] Existing non-primary systems are changed only where they interact with physics/randomness.
- [ ] Deterministic regression tests, full solution build, and full solution tests pass.
- [ ] The final report contains all six requested output sections and before/after measurements.

## Deny List

- No new UI.
- No new non-physics feature systems outside the primary scope.
- No unrelated refactors.
- Do not overwrite or discard pre-existing working-tree changes.
- Do not claim an unobservable metric was measured.
