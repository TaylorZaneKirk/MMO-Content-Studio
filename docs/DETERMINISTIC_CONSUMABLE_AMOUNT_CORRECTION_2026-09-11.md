# Deterministic consumable amount correction

Status: **Implemented on feature branch; exact-delta review and human acceptance pending.**

Branch: `feature/deterministic-consumable-amount`.
Execution base: `0675a76511e0640b641aad13f7ee00dbe6bb43fd`.

This executes the MMO Project prompt
`CONTENT_STUDIO_DETERMINISTIC_CONSUMABLE_AMOUNT_IMPLEMENTATION_PROMPT_2026-09-11.md`
staged on `rewrite/r1-foundation` at `cc17401e7661d6620dc497d0e87652511ced9184`.
Only Content Studio files changed. MMO Project consumption, schema handoff,
food balance, timing, and Items I2 acceptance remain separate work.

## Result and ownership

- Contracts, normalization, validation, schema health, and transactional unified
  item persistence now use one integer `amount` for each `restore_resource` effect.
  The existing serialized aggregate comparison and preview signature include that
  amount directly, without another adapter or state owner.
- The unified Items editor loads, previews, and saves one amount control per effect.
  New effects default to 1. Targets and all other consumable semantics are unchanged.
- Corrected integration migration `017` creates the deterministic schema with the
  existing 1–1,000,000 limit and seeds no food profiles or effects.
- Forward migration `056` checks every historical effect before changing the table.
  Equal values migrate by column rename, preserving rows and timestamps. Any true
  range aborts the entire block with an item/effect diagnostic and a reauthoring hint.
  Successful migration removes the old axis and range constraint. Fresh schemas and
  repeated execution are safe.
- Current README, API, architecture, roadmap, T2 acceptance, and integration notes
  describe deterministic authoring and the pending game-server consumption boundary.

The existing declarative profile/requirements/effects boundary is retained as the
donor implementation. This is authoring/schema plumbing: **no meaningful OSRS
gameplay analogue**. Ordinary-food gameplay rules remain outside this correction.
The migration uses ordinary PostgreSQL [ALTER TABLE](https://www.postgresql.org/docs/current/sql-altertable.html)
and [PL/pgSQL exception](https://www.postgresql.org/docs/current/plpgsql-errors-and-messages.html)
behavior; no external architecture or code was imported.

## Validation on 2026-09-11

- Focused migration checks: **5/5 passed**, including actual PostgreSQL 16 execution
  for fresh schema/repeat execution, lossless migration with timestamp/constraint
  preservation, atomic refusal of true ranges, and rejection of zero amounts.
  Used a disposable local cluster through `CONTENT_STUDIO_MIGRATION_DSN`; each
  database case rolled back its isolated schema. No development schema was migrated.
- Required `./tools/test.sh`: **227 Python tests; 220 passed, 5 failed, 1 error,
  1 skipped**. The five failures reject pre-existing parent-checkout changes in
  `prototype/shared/maps/mobs/catalog.json` and/or `mmo-context.txt` (D1, D2, T5B,
  T5D, U4 checks). The error is the existing T4D reference to removed
  `MobCatalogExportCli.cs`. These tests and parent files were not modified.
  The exact skip was: `MMO Project checkout is unavailable; runtime handoff source
  check is skipped.` The script stopped before its host/Godot stages.
- Host stages run directly: build passed with 0 errors and 2 NU1900 feed warnings;
  host suite reported **327 passed, 0 failed, 0 skipped**. Seven existing opt-in
  repository integration cases return without execution when their database
  environment variables are unset; those results do not prove database integration.
  The new migration cases above did execute against PostgreSQL.
- All four Godot commands from the entrypoint were attempted directly. Grip-anchor
  and actor/item-alignment fixtures printed their pass markers. The entrypoint's
  `--quit` ended the asynchronous contract and socket fixtures before completion.
  Rerunning without that flag exposed a foreground-grip-overlay assertion in the
  contract fixture, also reproduced with the unchanged pre-correction fixture script.
  Tree/resource cleanup errors accompanied it. Its eventual pass marker/exit 0 is
  not counted as a pass. The unchanged socket fixture timed out after 90 seconds.
- The new consumable fixture was also invoked alone through a temporary subclass
  of the checked-in contract fixture, with failure tracking: **passed**, exit 0.
  It verifies default amount, one control after loading, and exact edited payload.
- `git diff --check`: passed. Relevant file/link references and remaining historical
  range-name occurrences were inspected; normal authoring surfaces have no range axis.

## Existing content requiring a decision

A read-only inspection of the configured development database found **24 true-range
effects**, all effect index 0 targeting health. Examples include
`inventory_2_apple` (2–4), `inventory_30_pie` (6–10), and
`inventory_33_fish` (6–9). The first blocking row in migration order is
`inventory_257_trout` (3–5).

Migration `056` cannot succeed on that content until all true ranges are deliberately
reauthored. No fixed values were selected, no authored rows were changed, and the
migration was not applied to that database. Restarting the updated host against the
unmigrated database will correctly report the missing deterministic schema.

This branch is ready for exact-delta review with the validation limitations above.
It is not self-accepted and does not authorize merging or the MMO Project handoff.
