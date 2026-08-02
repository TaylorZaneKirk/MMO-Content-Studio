# T3B Acceptance: Weapon and Tool Domain Foundation

T3B Phase 1 adds the backend foundation for hand-held weapons and tools. It
does not add the full Godot weapon/tool editor.

## Implemented

- `GET /api/v1/hand-equipment/options`
- `GET /api/v1/hand-equipment?search=...`
- `GET /api/v1/hand-equipment/{item_id}`
- `POST /api/v1/hand-equipment/{item_id}/preview`
- `PUT /api/v1/hand-equipment/{item_id}/draft`
- `POST /api/v1/hand-equipment/{item_id}/publish`
- `POST /api/v1/hand-equipment/{item_id}/disable`

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
`integrations/mmo-project/prototype/sql/018_item_tool_capabilities.sql`. It must
be reviewed and applied to the MMO Project repository before development-machine
runtime verification. The migration creates `item_tool_capabilities`, prevents
capabilities from attaching to non-hand items, and prevents an item from moving
out of `right_hand` or `left_hand` while capability rows remain. Structural
migrations do not seed item-specific content.

## Verification

- Source contracts cover routes, aggregate contracts, validation, transaction
  shape, migration scope, bidirectional slot integrity, the absence of
  item-specific migration seeds, and the absence of Godot SQL.
- Compiled host tests cover deterministic capability normalization, canonical
  duplicate detection, derived classification labels, active runtime weapon-slot
  rules, registry values, normalization edge cases, and classification behavior.
- Full database transaction, optimistic-concurrency, and HTTP integration
  verification still requires the configured development PostgreSQL database.
