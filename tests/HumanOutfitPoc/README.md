# Human outfit authoring POC

One wizard lifecycle against an explicitly guarded disposable PostgreSQL clone:
existing-art options/detail response, normalization, preview signature, validation,
save/reload/publication, unchanged combat/loot, and invalid path rejection.
The original approved outfit is restored afterward.

Requires migration 114 and `human_mobs.sql` in the disposable fixture. Set
`MMO_TEST_CLUSTER` to a workflow-owned private cluster and `MMO_TEST_DATABASE`
to `human_checked`; pass the game client assets directory as the first argument.
Use an isolated `--artifacts-path` for the project and referenced host.
Never point this POC at the live database.

Browser/native forms select cosmetic player layers. Their existing source PNG
preview remains a clearly labelled fallback; the actual composed outfit uses
the game's existing PlayerComposite renderer. Migration 114 is a deployment
prerequisite for the updated host. No running Studio was updated by this pass.
