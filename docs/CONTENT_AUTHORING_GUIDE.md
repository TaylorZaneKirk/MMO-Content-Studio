# Content Authoring Guide Reference

The authoritative content-authoring guide lives in the MMO Project repository:

```text
MMO-Project/docs/development/CONTENT_AUTHORING_GUIDE.md
```

Do not duplicate that runtime guide in this repository. Content Studio planning
and implementation documents should cite the MMO Project guide, then keep only
tool-specific contracts, acceptance notes, and handoff artifacts here.

T4 Phase 0 used the guide's `Adding a New Mob` section as the primary source for
the manual mob-authoring workflow. The resulting Content Studio decisions are
tracked in:

- [`T4_MOB_DOMAIN_AUDIT.md`](T4_MOB_DOMAIN_AUDIT.md)
- [`T4_IMPLEMENTATION_PLAN.md`](T4_IMPLEMENTATION_PLAN.md)
- [`T4_ACCEPTANCE.md`](T4_ACCEPTANCE.md)

## Mob combat-level preview

The Mobs workspace retains a read-only derived combat level and equivalent-level
bonus diagnostics in Primary Attack. The formula uses permanent Attack, Strength,
Defence and maximum Health, matching MMO Project's current melee-Mob formula.
It rounds down once and has a minimum of 1, with no player-level cap.

Formula help explains that bonuses, attack speed and range are excluded. Advisory
warnings identify positive selected attack, melee strength or defence bonuses and
unequal defence styles. They flag known differences, not a calibrated difficulty
threshold, and do not block saving or publishing. Compare the numeric equivalent
levels to judge magnitude; no manual combat-level override is exposed.

Local edits, including accuracy-style changes, refresh the diagnostics. Host
preview responses supply the same existing diagnostic fields. Disabling the
primary combat profile clears the preview warnings. No schema, persistence or
runtime protocol change is involved.

2026-09-13 delivery: retained preview/diagnostics completed with formula help,
advisory warnings and accuracy-style refresh. Build, test, parse, smoke and
independent review runs were skipped under Taylor's current direction; no new
validation or human acceptance is claimed.


## R5 ammunition authoring

The unified Item editor now exposes an optional Ammo family for Ranged weapons.
Arrow hides the weapon-owned damage type; None (self-contained) requires a
Light/Standard/Heavy weapon damage type. The Ammo equipment slot exposes its own
Arrow family and damage type and requires a stackable item, ammo profile and no
weapon profile for publication. Other slots cannot carry an ammunition profile.
Combat bonuses continue to own Ranged Attack/Strength; no Ammo paper-doll art is
required. Migration 070 is mirrored under `integrations/mmo-project/prototype/sql`.

Use Preview -> Save Draft -> reload -> Preview -> Publish for new ammunition, and
Preview -> Save & Publish -> reload when updating a Published bow. These supported
API flows authored `copper_arrows` and updated `inventory_346_wooden_bow` in the
configured development database. See the parent guide's **Ranged weapons and
Ammo (R5)** section for exact content and runtime rules. No tests were run;
production host build, Godot 4.7 parse/startup and live API/reload verification
passed. Manual editor/gameplay acceptance remains Taylor's gate.
