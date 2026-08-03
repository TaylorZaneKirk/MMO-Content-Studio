# Interactable World Objects - Domain Direction and Roadmap Plan

This document schedules a future Interactable World Objects domain for MMO
Content Studio. It establishes ownership boundaries and a phased direction
without locking final database tables, API contracts, Godot workspace layout, or
MMO Project runtime behavior.

The design target includes levers, switches, gates, linked mechanisms,
quest-related objects, searchable scenery, containers, disarmable traps, mining
rocks, trees, gathering resources, fishing and harvesting nodes, cooking ranges,
furnaces, anvils, looms, crafting benches, agility or challenge objects, and
inspectable scenery.

## Core Ownership Lock

> Interactable world objects are reusable definitions with typed capabilities. Tiled owns placed instances and map-specific links. The authoritative runtime owns interaction execution and mutable state.

Interactable world objects are reusable definitions with typed capabilities.
Tiled owns placed instances and map-specific links. The authoritative runtime
owns interaction execution and mutable state.

```text
WorldObjectDefinition
        +
Tiled WorldObjectSpawn
        |
        v
Runtime WorldObjectInstance
```

This follows the same successful ownership pattern used for mobs:

- Content Studio authors reusable definitions.
- Tiled authors placement.
- MMO Project composes both into authoritative runtime instances.

## Ownership Boundaries

### Content Studio Owns Reusable Definitions

Reusable definition concerns may include:

- stable `world_object_definition_id`
- display name
- visual states
- footprint
- collision policy
- interaction distance
- interaction options
- typed capability configuration
- requirements
- state-transition definitions
- publication lifecycle
- reusable reward or recipe references
- reusable tool-capability requirements

These are directional concepts, not a locked relational schema.

### Tiled Owns Placed Instances

Placement-owned concerns may include:

- stable placement ID
- `world_object_definition_id`
- region, map, chunk, and coordinates
- facing or rotation
- initial state override
- map-specific configuration
- linked placement IDs
- quest-instance keys
- placement-specific reset or leash-like boundaries where applicable

Map-specific object links remain placement-owned. A reusable lever definition
must not contain a hard-coded gate ID from one map.

```text
lever_001
    -> linked placement: dungeon_gate_001
    -> typed action: open
```

### MMO Project Runtime Owns Authoritative Execution

The runtime owns:

- click-to-approach-and-interact
- reachability and interaction range
- action timing
- cancellation and interruption
- skill requirements
- tool-capability checks
- inventory consumption and rewards
- XP rewards
- success/failure resolution
- mutable object state
- depletion and regeneration
- multiplayer contention
- quest-state integration
- linked-object transitions
- interest publication and revisioned updates
- persistence where state must survive restart

Clients may request interactions and render state, but clients are not
authoritative for interaction results.

## Definition, Placement, And Runtime Instance

### `WorldObjectDefinition`

Reusable authored content describing what an object is and which typed
capabilities it supports.

### `WorldObjectSpawn`

A Tiled-authored placement referencing a stable reusable definition.

### `WorldObjectRuntimeInstance`

The server-owned live object created by composing the definition and placement.
Potential runtime state may include:

- current state
- current visual state
- available or depleted status
- cooldown or regeneration deadline
- current reservation or active user
- quest-controlled flags
- revision number

The persistence strategy for runtime state is deferred until a dedicated
runtime audit.

## Typed Capability Model

The system should prefer typed capabilities rather than:

- a giant object-type enum
- arbitrary executable scripts
- one custom server class for every object
- a general-purpose visual state-machine language

### Interaction Actions

Interaction actions are player-visible verbs, such as:

- Mine
- Chop
- Cook
- Smith
- Search
- Pull
- Disarm
- Inspect
- Open
- Read
- Activate

### Runtime Capabilities

Runtime capabilities identify the authoritative subsystem that resolves an
action, such as:

- `state_toggle`
- `linked_mechanism`
- `quest_interaction`
- `resource_gathering`
- `processing_station`
- `trap_disarm`
- `container_search`
- `inspectable`
- `challenge_obstacle`

An object may expose multiple actions backed by different capabilities.

```text
Ancient Furnace
    Inspect -> quest_interaction
    Smelt   -> processing_station
```

## Capability Families

### Toggle And Linked Mechanisms

Examples include levers, switches, pressure plates, gates, bridges, and puzzle
controls.

Potential typed outcomes include:

- set linked object state
- open or close a barrier
- activate an encounter
- emit a quest event
- enable or disable another object

No arbitrary scripts should be stored or executed from authoring content.

### Quest Interactions

Examples include inspecting a statue, searching a quest crate, reading an
inscription, placing an item on an altar, and retrieving a quest item.

Potential concerns include:

- quest predicates
- item requirements
- item consumption
- emitted quest event
- alternate visual state
- dialogue or message reference
- state scope

Quest-object state scope must eventually be explicit:

- per-player
- globally shared
- temporarily shared
- party/instance scoped

This document does not resolve the full quest model.

### Gathering Resource Nodes

Examples include mining rocks, trees, harvesting plants, and fishing spots.

Potential reusable concerns include:

- skill requirement
- tool capability requirement
- action duration
- success profile
- produced item references
- XP reward
- depletion policy
- regeneration policy
- available/depleted visuals
- competition model

These objects should connect to the existing T3B tool-capability system.

```text
required tool capability: mining
minimum capability tier or effectiveness: 2
```

The runtime should not require one hard-coded pickaxe item when a typed
capability can express the requirement.

### Processing Stations

Examples include cooking ranges, furnaces, anvils, looms, and workbenches.

Potential reusable concerns include:

- station capability
- interaction label
- recipe-set or crafting-domain reference
- modifiers or bonuses
- fuel requirement where applicable
- facing/animation behavior
- quest restrictions

Recipes and production rules belong to a crafting or processing domain, not
inside every individual range, furnace, or anvil definition.

### Traps And Challenge Objects

Examples include disarmable traps, locked objects, agility obstacles, and puzzle
components.

Potential concerns include:

- skill requirement
- tool/item requirement
- attempt duration
- success/failure resolution
- damage or status consequence
- XP/reward
- reset policy
- state scope
- visual transitions

Resolution should use typed server-authoritative services rather than generic
scripting.

### Containers And Searchable Objects

Examples include chests, cupboards, crates, barrels, and quest caches.

Potential concerns include:

- loot or reward source
- per-player versus shared availability
- lock/key/skill requirements
- refill/reset behavior
- quest conditions
- opened/closed visual states

These rewards should not be merged into mob guaranteed-drop authoring.

## Object State Model

Interactable objects need a narrow, typed state model. Possible initial state
vocabulary includes:

- `available`
- `depleted`
- `active`
- `inactive`
- `open`
- `closed`
- `locked`
- `disabled`

Not every object supports every state. Capabilities define legal transitions,
and visual state, collision, and available actions may depend on runtime state.
A general-purpose state-machine editor is deferred.

```text
Object state
    -> visual state
    -> collision behavior
    -> available interaction actions
```

## Multiplayer And State-Scope Concerns

Important runtime concerns remain unresolved until a dedicated runtime audit:

- simultaneous interaction attempts
- resource reservation
- whether gathering is shared or player-specific
- depletion visible to all players
- interruption by movement or combat
- disconnect recovery
- persistence across restart
- instance-specific state
- revisioned state publication
- map/chunk interest transitions
- linked-object atomicity

## Relationship To Existing Systems

### T3B Tools

Resource and challenge objects may require typed tool capabilities instead of
specific hard-coded item IDs.

### Skills And Discipline/Mastery

Gathering and processing may grant skill XP and later discipline/mastery
progress. This design does not lock formulas.

### Inventory And Items

Interactions may consume or produce items through existing authoritative
inventory services.

### Quests And Dialogue

Quest objects may evaluate quest state and emit quest events, but the object
framework must not embed the entire quest engine.

### Static World-Object Rendering

Production planning must first audit the MMO Project's existing static
world-object placement and rendering implementation.

### Runtime Activity Arbitration

Object interactions must eventually integrate with combat, movement, dialogue,
and other player activities rather than creating an independent conflicting
activity model.

## Cross-Repository Planning Note

Implementation will require a future coordinated audit of:

- MMO Content Studio
- MMO Project static-content importer
- MMO Project static world-object rendering
- runtime interaction/activity arbitration
- inventory and skill services
- Tiled object conventions
- quest-state and dialogue contracts

This planning task does not modify MMO Project.

## Explicit Non-Goals

This planning document does not define:

- final database tables
- final API contracts
- full gathering formulas
- full crafting or recipe schemas
- complete quest-state contracts
- arbitrary scripting
- boss/environment encounter scripting
- building or housing furniture systems
- destructible terrain
- player-created world objects
- hot reload behavior
- exact persistence policy

## Roadmap Placement

T6 should establish reusable interactable world-object definitions, preserve
Tiled placement, and add server-authoritative typed capability dispatch. T7
should build on that foundation for gathering resources and processing stations.

Ordering rationale:

- T5 establishes reusable noncombat actors and interaction foundations.
- T6 establishes non-actor world interactions.
- T7 adds skill-specific gathering and production behavior.
- Dialogue and Quest Studio then gain both NPC and world-object interaction
  targets.
