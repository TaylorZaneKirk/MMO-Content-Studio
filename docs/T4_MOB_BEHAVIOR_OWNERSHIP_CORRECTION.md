# T4 Mob Behavior Ownership Correction

## Status

This decision supersedes earlier T4 documentation that assigned routine mob
behavior fields such as `spawn_behavior` and `leash_radius_tiles` to Tiled
placement.

The earlier boundary was too broad. Tiled should own spatial placement facts;
Content Studio should own reusable gameplay behavior.

## Locked Principle

> The mob definition decides how a mob behaves. The Tiled spawn supplies the
> spatial origin and other map-specific placement facts that the behavior acts
> around.

The authoritative composition is:

```text
Content Studio MobDefinition
        +
Tiled EnemySpawn placement
        ↓
Server-owned runtime mob instance
```

## Corrected Ownership Boundary

### Content Studio owns reusable mob behavior

The reusable mob definition owns:

- movement behavior, including `static` and `random_wander`/roaming modes
- idle wander radius
- aggression mode
- aggression or player-detection radius
- proactive hostile-mob detection radius
- leash radius
- retaliation policy
- chase and disengagement policy
- return-home behavior
- target scan interval and candidate limit
- movement speed
- combat profile, stats, bonuses, faction, visuals, footprint, and drops

Behavioral values should be authored once and reused by every ordinary placement
of that mob definition. Balance changes should not require editing every map that
contains the mob.

### Tiled owns placement and map-specific geometry

An `EnemySpawn` placement owns:

- stable spawn id
- `mob_definition_id`
- map, region, chunk, and source coordinates
- initial facing
- home position, normally derived from the spawn coordinate
- explicit patrol waypoints or map paths when patrol support exists
- links to encounter boundaries or other placed map objects
- rare, explicit instance overrides when a real content requirement justifies
  them

Tiled must not routinely duplicate reusable behavior such as leash radius,
aggression range, wander radius, or static-versus-roaming mode.

## Runtime Interpretation

For a mob definition such as:

```text
movement_behavior = random_wander
wander_radius_tiles = 5
aggression_radius_tiles = 7
leash_radius_tiles = 12
return_home_behavior = return_to_spawn
```

and a Tiled placement at tile `(24, 18)`, the runtime interprets `(24, 18)` as
the mob's home point. Wander, chase, leash, and return-home calculations are
measured from that placement-derived home point using the behavior authored in
Content Studio.

## Placement Overrides

Definition defaults are authoritative for ordinary spawns. Placement overrides
may exist only for genuinely map-specific exceptions, such as:

- making one guard stationary while the reusable guard definition normally roams
- assigning a special boss-arena leash boundary
- supplying patrol waypoints for one placed instance
- binding a spawn to a specific encounter region

Overrides must be:

- explicitly named as overrides
- optional
- narrowly scoped
- validated against the mob definition
- visible in publication and diagnostic output

The importer/runtime must not treat legacy Tiled behavior properties as silent,
normal overrides. Existing properties should be migrated or rejected with clear
remediation once the corrected boundary is implemented.

## Initial Behavior Model

The first corrected runtime-compatible behavior model should include:

- `movement_behavior`: initially `static` or `random_wander`
- `wander_radius_tiles`
- `aggression_mode`: initially passive/retaliatory/proactive as supported by the
  runtime audit
- `aggression_radius_tiles` for player or general target acquisition where the
  runtime supports it
- existing hostile-mob targeting fields
- `leash_radius_tiles`
- `return_home_behavior`

Exact naming should follow current runtime conventions after implementation
inspection. Do not create a general behavior scripting language.

## Home Position and Patrols

Home position remains placement-owned because it is inherently spatial. In the
normal case it is derived directly from the Tiled spawn coordinate.

Patrol capability is split:

- Content Studio declares whether a definition supports or defaults to patrol
  behavior and owns reusable patrol behavior parameters.
- Tiled owns the actual map-specific waypoint path.

Patrol authoring remains deferred until the runtime has an authoritative patrol
contract.

## Required T4 Corrections

The next implementation pass must reconcile all T4 artifacts and runtime paths:

1. Update T4 audit, implementation, acceptance, roadmap, API, integration, and
   content-authoring documentation to use the corrected ownership boundary.
2. Extend the mob authoring schema and contracts with reusable movement,
   aggression, leash, and return-home fields supported by the runtime.
3. Expose those fields in the Content Studio Mobs workspace.
4. Export behavior through `mob_definition_catalog`.
5. Remove routine behavior ownership from `EnemySpawn` generated/static-content
   shape.
6. Update the Tiled importer so ordinary spawns require only placement identity,
   definition linkage, coordinates, and optional facing.
7. Migrate existing Tiled behavior values into their reusable mob definitions.
8. Make runtime enemy construction obtain behavior from the definition and home
   position from placement.
9. Add compatibility validation for stale Tiled behavior properties.
10. Verify generated-file and database static-content sources remain
    semantically equivalent.

## Non-goals

This correction does not yet add:

- arbitrary behavior scripts
- complex patrol route authoring
- encounter scripting
- boss phase behavior
- per-spawn unrestricted overrides
- hot reload
- respawn authoring unless separately audited and approved

## Acceptance Rule

The ownership correction is complete when an ordinary Tiled `EnemySpawn` can be
placed using only its stable spawn identity, `mob_definition_id`, coordinates,
and optional facing, while the runtime obtains static/roaming behavior, wander
radius, aggression settings, leash radius, and return-home policy from the
published Content Studio mob definition.
