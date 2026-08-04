#!/usr/bin/env python3
"""Source contracts for the T5B NPC schema and contract foundation."""

from __future__ import annotations

import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"
MIGRATION = ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "024_npc_authoring_schema.sql"


def _mmo_project_candidates() -> list[Path]:
    configured = os.environ.get("MMO_PROJECT_ROOT")
    candidates: list[Path] = []
    if configured:
        candidates.append(Path(configured))
    candidates.extend(
        [
            ROOT.parents[1],
            ROOT.parents[1] / "MMO-Project" / "MMO-Project",
            ROOT.parents[1] / "MMO Project",
        ]
    )
    return candidates


def _runtime_file(relative_path: Path) -> Path | None:
    for candidate in _mmo_project_candidates():
        path = candidate / relative_path
        if path.exists():
            return path
    return None


class T5BNpcSchemaContractsTests(unittest.TestCase):
    def test_migration_exists_and_declares_required_columns_and_constraints(self) -> None:
        self.assertTrue(MIGRATION.exists())
        source = MIGRATION.read_text()

        for token in (
            "CREATE TABLE IF NOT EXISTS npc_definitions",
            "npc_definition_id TEXT PRIMARY KEY",
            "display_name TEXT NOT NULL",
            "publication_state TEXT NOT NULL DEFAULT 'Draft'",
            "visual_texture_path TEXT NOT NULL",
            "source_width INTEGER NOT NULL",
            "source_height INTEGER NOT NULL",
            "visual_anchor_offset_x DOUBLE PRECISION NOT NULL DEFAULT 0",
            "visual_anchor_offset_y DOUBLE PRECISION NOT NULL DEFAULT 0",
            "visual_render_scale DOUBLE PRECISION NOT NULL DEFAULT 0.25",
            "footprint_width_tiles INTEGER NOT NULL DEFAULT 1",
            "footprint_height_tiles INTEGER NOT NULL DEFAULT 1",
            "movement_behavior TEXT NOT NULL DEFAULT 'static'",
            "wander_radius_tiles INTEGER NOT NULL DEFAULT 0",
            "tick_interval_ms INTEGER NOT NULL DEFAULT 600",
            "idle_chance DOUBLE PRECISION NOT NULL DEFAULT 0.15",
            "interaction_enabled BOOLEAN NOT NULL DEFAULT FALSE",
            "interaction_range_tiles INTEGER NOT NULL DEFAULT 1",
            "default_interaction TEXT NOT NULL DEFAULT 'talk'",
            "default_dialogue_id TEXT NULL",
            "notes TEXT NULL",
            "created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()",
            "updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()",
            "npc_definitions_id_format_check",
            "npc_definitions_publication_state_check",
            "npc_definitions_visual_numbers_check",
            "npc_definitions_initial_footprint_check",
            "npc_definitions_movement_behavior_check",
            "npc_definitions_movement_consistency_check",
            "npc_definitions_tick_interval_check",
            "npc_definitions_idle_chance_check",
            "npc_definitions_interaction_range_check",
            "npc_definitions_default_interaction_check",
            "npc_definitions_dialogue_reference_check",
            "npc_definitions_timestamp_order_check",
        ):
            self.assertIn(token, source)

    def test_migration_is_additive_and_excludes_placement_quest_service_and_combat_scope(self) -> None:
        source = MIGRATION.read_text()

        for token in (
            "DROP TABLE",
            "ALTER TABLE",
            "CREATE TRIGGER",
            "CREATE FUNCTION",
            "INSERT INTO",
            "spawn_id",
            "map_id",
            "region_id",
            "chunk_id",
            "tile_x",
            "tile_y",
            "facing",
            "mount_id",
            "patrol",
            "quest_id",
            "quest_stage",
            "quest_reward",
            "service_script",
            "shop",
            "bank",
            "trainer",
            "faction",
            "combat",
            "attack_",
            "defence",
            "JSONB",
        ):
            self.assertNotIn(token, source)

    def test_contracts_expose_complete_aggregate_draft_options_and_mutation_shapes(self) -> None:
        source = (HOST / "Contracts" / "NpcContracts.cs").read_text()

        for token in (
            "NpcDefinition",
            "NpcDefinitionSummary",
            "NpcCatalogResponse",
            "NpcOptionsResponse",
            "NpcDraft",
            "PreviewNpcRequest",
            "SaveNpcDraftRequest",
            "NpcPublicationRequest",
            "NpcDeleteRequest",
            "NpcPreviewResponse",
            "NpcMutationResponse",
            "NpcDeleteResponse",
            'JsonPropertyName("npc_definition_id")',
            'JsonPropertyName("display_name")',
            'JsonPropertyName("publication_state")',
            'JsonPropertyName("visual_texture_path")',
            'JsonPropertyName("source_width")',
            'JsonPropertyName("source_height")',
            'JsonPropertyName("visual_anchor_offset_x")',
            'JsonPropertyName("visual_anchor_offset_y")',
            'JsonPropertyName("visual_render_scale")',
            'JsonPropertyName("footprint_width_tiles")',
            'JsonPropertyName("footprint_height_tiles")',
            'JsonPropertyName("movement_behavior")',
            'JsonPropertyName("wander_radius_tiles")',
            'JsonPropertyName("tick_interval_ms")',
            'JsonPropertyName("idle_chance")',
            'JsonPropertyName("interaction_enabled")',
            'JsonPropertyName("interaction_range_tiles")',
            'JsonPropertyName("default_interaction")',
            'JsonPropertyName("default_dialogue_id")',
            'JsonPropertyName("notes")',
            'JsonPropertyName("created_at_utc")',
            'JsonPropertyName("updated_at_utc")',
            'JsonPropertyName("expected_updated_at_utc")',
            'JsonPropertyName("preview_signature")',
            'IReadOnlyList<AuthoringChange> Changes',
            'IReadOnlyList<ApiError> Messages',
            "NpcSupportedLimits",
            "NpcVisualAssetOptions",
            "NpcAuthoringDefaults",
        ):
            self.assertIn(token, source)

        for forbidden in (
            "SpawnId",
            "MapId",
            "RegionId",
            "ChunkId",
            "TileX",
            "TileY",
            "Facing",
            "MountId",
            "Patrol",
            "QuestId",
            "QuestDefinition",
            "ServiceScript",
            "Combat",
            "Faction",
        ):
            self.assertNotIn(forbidden, source)

    def test_domain_rules_and_registry_own_t5b_normalization_and_options(self) -> None:
        rules = (HOST / "Services" / "NpcDomainRules.cs").read_text()
        registry = (HOST / "Services" / "NpcAuthoringRegistry.cs").read_text()

        for token in (
            "NormalizeStableId",
            "NormalizeOptional",
            "NormalizePublicationState",
            "NormalizeMovementBehavior",
            "NormalizeInteractionType",
            "NormalizeDraft",
            "BuildSemanticComparisonInput",
            "movementBehavior == \"static\" ? 0 : draft.WanderRadiusTiles",
            "interactionEnabled ? NormalizeOptional(draft.DefaultDialogueId) : null",
        ):
            self.assertIn(token, rules)

        for token in (
            "MinimumTickIntervalMilliseconds = 600",
            "MinimumInteractionRangeTiles = 1",
            "InitialFootprintWidthTiles = 1",
            "InitialFootprintHeightTiles = 1",
            "DefaultMovementBehavior = \"static\"",
            "DefaultInteraction = \"talk\"",
            "LoadPublicationStates",
            "LoadMovementBehaviors",
            "LoadInteractionTypes",
            "LoadDialogueReferences() => []",
            "CanValidateDialogueReferences => false",
        ):
            self.assertIn(token, registry)

    def test_schema_provider_and_catalog_section_are_feature_owned(self) -> None:
        feature = (HOST / "Features" / "Npcs" / "NpcAuthoringFeature.cs").read_text()
        schema = (HOST / "Features" / "Npcs" / "NpcSchemaRequirements.cs").read_text()
        catalog = (HOST / "Features" / "Npcs" / "NpcCatalogSectionProvider.cs").read_text()
        aggregator = (HOST / "Features" / "AuthoringFeatureExtensions.cs").read_text()

        for token in (
            "AddSingleton<NpcAuthoringRegistry>()",
            "AddSingleton<IAuthoringSchemaRequirementProvider, NpcSchemaRequirements>()",
            "AddSingleton<IAuthoringCatalogSectionProvider, NpcCatalogSectionProvider>()",
        ):
            self.assertIn(token, feature)

        self.assertIn('FeatureId => "prototype-npc-authoring-v1"', schema)
        self.assertIn('AuthoringSchemaRequirement.Table("npc_definitions")', schema)
        self.assertIn('AuthoringSchemaRequirement.Column("npc_definitions", "updated_at_utc")', schema)
        self.assertIn('AuthoringSchemaRequirement.Constraint("npc_definitions_dialogue_reference_check")', schema)
        self.assertIn('ContentType => "npcs"', catalog)
        self.assertIn('"NPCs"', catalog)
        self.assertIn("NpcAuthoringService", catalog)
        self.assertIn("services.AddNpcAuthoring();", aggregator)

        for forbidden in (
            "content-studio/scripts/npc_editor.gd",
            "Quest",
            "ServiceScript",
        ):
            self.assertNotIn(forbidden, feature)

    def test_docs_mark_t5b_without_claiming_routes_or_godot_workspace(self) -> None:
        docs = "\n".join(
            [
                (ROOT / "README.md").read_text(),
                (ROOT / "docs" / "ROADMAP.md").read_text(),
                (ROOT / "docs" / "ARCHITECTURE.md").read_text(),
                (ROOT / "docs" / "T5_NPC_AUTHORING_PLAN.md").read_text(),
                (ROOT / "docs" / "T5_NPC_ACCEPTANCE.md").read_text(),
                (ROOT / "integrations" / "mmo-project" / "README.md").read_text(),
                (ROOT / "docs" / "SCHEMA_HEALTH_PROVIDERS.md").read_text(),
                (ROOT / "docs" / "FEATURE_CATALOG_PROVIDERS.md").read_text(),
            ]
        )

        for token in (
            "T5B NPC schema and contract foundation implemented",
            "024_npc_authoring_schema.sql",
            "T5E MMO Project runtime NPC catalog handoff implemented",
            "notes is authoring-only",
            "Dialogue-reference validation uses the configured file-backed MMO Project dialogue catalog",
        ):
            self.assertIn(token, docs)

        for forbidden in (
            "Quest authoring implemented",
        ):
            self.assertNotIn(forbidden, docs)

    def test_mmo_project_checkout_is_unchanged_except_nested_repo_pointer(self) -> None:
        runtime_root = _runtime_file(Path("prototype") / "server" / "Program.cs")
        if runtime_root is None:
            self.skipTest("MMO Project checkout is unavailable; git status check is skipped.")

        project = runtime_root.parents[2]
        result = subprocess.run(
            ["git", "status", "--short"],
            cwd=project,
            check=True,
            text=True,
            capture_output=True,
        )
        allowed_paths = (
            "docs/development/CONTENT_AUTHORING_GUIDE.md",
            "prototype/importer/",
            "prototype/server/features/README.md",
            "prototype/server/features/npcs/application/NpcRuntimeService.cs",
            "prototype/server/features/static_content/application/",
            "prototype/shared/maps/generated/starter_region/",
            "prototype/shared/maps/npcs/",
            "prototype/shared/maps/tiled/mmoproject.tmx",
            "prototype/shared/maps/tiled/regions/starter_region.tmj",
            "prototype/shared/maps/tiled/regions/starter_region.tmx",
            "prototype/sql/",
            "prototype/tests/MMO.Project.Prototype.MapPublisher.Tests/MapPublisher/NpcCatalogExporterTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/CombatActorRuntimeProviderTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/GeneratedRegionRuntimeAdapterTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/MapPublisher/",
            "prototype/tools/MapPublisher/",
            "tools/MMO-Content-Studio",
            "tools/mmoproject.tiled-session",
        )
        unexpected = []
        for line in result.stdout.splitlines():
            path = line[3:]
            if not path.startswith(allowed_paths):
                unexpected.append(line)
        self.assertEqual([], unexpected)


if __name__ == "__main__":
    unittest.main()
