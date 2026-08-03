# T3B Acceptance: Weapons & Tools Workspace

T3B adds the backend foundation and dedicated Godot workspace for hand-held
weapons and tools. MMO Project runtime execution of tool capabilities remains
deferred.

## Implemented

- `GET /api/v1/hand-equipment/options`
- `GET /api/v1/hand-equipment?search=...`
- `GET /api/v1/hand-equipment/{item_id}`
- `POST /api/v1/hand-equipment/{item_id}/preview`
- `PUT /api/v1/hand-equipment/{item_id}/draft`
- `POST /api/v1/hand-equipment/{item_id}/publish`
- `POST /api/v1/hand-equipment/{item_id}/disable`
- Godot **Weapons & Tools** top-level workspace
- current-style `AuthoringHostClient` hand-equipment signals and methods
- shared `PaperDollPreview` helper used by both Equipment and Weapons & Tools

The aggregate includes the base item, equipability, `right_hand` / `left_hand`
slot, required Strength, skill requirements, skill modifiers, combat bonuses,
optional `weapon_profile`, zero or more ordered `tool_capabilities`,
publication state, and aggregate concurrency metadata.

## Rules

- Weapons and tools remain equipment; no new top-level item kind is stored.
- `right_hand` and `left_hand` are the only hand-slot identifiers.
- The current runtime resolves active weapon combat profiles from `right_hand`.
- `left_hand` may carry tool capabilities, but cannot publish a weapon profile
  until the runtime resolves left-hand weapons.
- Weapon range is stored in logical tiles.
- Attack speed is stored as `attack_speed_units`; one unit is 600 milliseconds.
- Combat bonuses remain in `item_combat_bonuses`; `weapon_profile` stores
  timing/range/style only.
- Tool behavior is declarative ordered capability IDs, not a single fixed tool
  type and not executable scripts.
- Durability, ammo, charges, item instance state, and two-handed semantics are
  deferred.

Every T3B mutation requires a matching `preview_signature`, an
`expected_updated_at_utc` concurrency token, row locking, transaction-scoped
child replacement, reload inside the transaction, commit, and reload-after-
commit verification.

Turning equipability off clears the slot, requirements, modifiers,
`weapon_profile`, combat bonuses, and `tool_capabilities`. Moving an item from a
hand slot to a wearable slot clears hand-only specialization rows.

## Workspace

The UI supports ordinary Basic item promotion, existing hand equipment,
weapon-only, tool-only, combined Weapon + Tool, hand equipment without
specialization, movement to a wearable slot, and turning Equippable off.

It exposes the base item fields, icon selection/import, publication metadata,
equipability, slot, required Strength, skill requirements, skill modifiers,
combat bonuses, optional weapon profile, and ordered tool capabilities.

Weapon profile controls include profile ID, attack family/type, attack style,
minimum and maximum range in logical tiles, and `attack_speed_units`. The UI
displays derived timing as `N units x 600 ms = X ms` and persists only
`attack_speed_units`.

Tool capability rows preserve displayed order in the outgoing payload and expose
capability ID, power tier, optional action animation ID, optional effect
resource ID, move up, move down, and remove controls.

`AuthoringWorkspaceSupport` owns preview state, apply eligibility, operation
matching, logical-change rendering, and validation rendering. The editor sends
the server-issued `preview_signature` on save, publish, and disable apply calls.

The prior UI attempt was recovered selectively: list/form structure, row
patterns, and paper-doll behavior informed the final implementation. Obsolete
bootstrap payloads, staging chunks, recovery workflows, direct transport code,
and editor-local preview-state fields were not restored.

## Validation

Drafts are permissive enough to save incomplete hand equipment, but still reject
malformed identifiers, invalid ranges, invalid `attack_speed_units`, unsupported
attack family/style values, duplicate or unknown `tool_capabilities`,
non-equippable specialization, consumable overlap, and non-hand specialization
metadata.

Publication is stricter: `right_hand` items require a valid `weapon_profile`,
published weapon ranges must be at least one logical tile, and `left_hand`
weapon profiles are rejected until the runtime supports them.

## Database

T3B uses existing runtime tables for base items, equipment slots, requirements,
modifiers, `item_combat_profiles`, and `item_combat_bonuses`.

The new schema handoff artifact is
`integrations/mmo-project/prototype/sql/018_item_tool_capabilities.sql`. It must be reviewed and applied to the MMO Project repository before development-machine
runtime verification. The migration creates `item_tool_capabilities`, prevents
capabilities from attaching to non-hand items, and prevents an item from moving
out of `right_hand` or `left_hand` while capability rows remain. Structural
migrations do not seed item-specific content.

## Verification

- Source contracts cover routes, aggregate contracts, validation, transaction
  shape, migration scope, bidirectional slot integrity, the absence of
  item-specific migration seeds, dedicated Godot navigation, T3B client methods,
  shared preview support, paper-doll extraction, preview signatures, and the
  absence of Godot SQL or recovery artifacts.
- Compiled host tests cover deterministic capability normalization, canonical
  duplicate detection, derived classification labels, active runtime weapon-slot
  rules, registry values, normalization edge cases, and classification behavior.
- Development-machine runtime verification still requires the configured
  PostgreSQL database, asset roots, and MMO Project runtime environment.
