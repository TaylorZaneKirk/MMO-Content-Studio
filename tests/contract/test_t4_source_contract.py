#!/usr/bin/env python3
"""Source contracts for the T4A mob schema and host contract foundation."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MMO_PROJECT = ROOT.parents[1]


class T4MobSourceContractTests(unittest.TestCase):
    def test_mob_migration_is_handoff_artifact_not_runtime_copy(self) -> None:
        migration = ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "019_mob_authoring_schema.sql"
        self.assertTrue(migration.exists())

        source = migration.read_text()
        for token in (
            "CREATE TABLE IF NOT EXISTS mob_factions",
            "CREATE TABLE IF NOT EXISTS mob_faction_dispositions",
            "CREATE TABLE IF NOT EXISTS mob_definitions",
            "CREATE TABLE IF NOT EXISTS mob_combat_profiles",
            "CREATE TABLE IF NOT EXISTS mob_combat_bonuses",
            "CREATE TABLE IF NOT EXISTS mob_drops",
            "publication_state IN ('Draft', 'Published', 'Disabled')",
            "attack_type IN ('melee')",
            "accuracy_style IS NULL OR accuracy_style IN ('thrust', 'slash', 'crush')",
            "attack_speed_units BETWEEN 1 AND 60",
            "mob_definitions_proactive_targeting_check",
            "mob_drops_unique_item_per_mob",
            "REFERENCES item_definitions(item_id)",
            "ISFINITE(",
            "ON DELETE RESTRICT",
            "ON DELETE CASCADE",
        ):
            self.assertIn(token, source)

        for forbidden in (
            "CREATE TRIGGER",
            "INSERT INTO mob_",
            "probability",
            "weight",
            "drop_chance",
            "roll_group",
            "respawn",
            "patrol",
            "spawn_id",
            "map_id",
            "region_id",
            "leash",
            "dialogue",
            "shop",
            "quest",
            "script_id",
            "script_body",
            "JSONB",
        ):
            self.assertNotIn(forbidden, source)

        self.assertFalse(
            (MMO_PROJECT / "prototype" / "sql" / "019_mob_authoring_schema.sql").exists()
        )

    def test_mob_contracts_cover_foundation_without_placement(self) -> None:
        source = (ROOT / "host" / "Contracts" / "MobContracts.cs").read_text()
        for token in (
            "MobCatalogResponse",
            "MobDefinitionSummary",
            "MobDefinition",
            "SaveMobDraftRequest",
            "MobPreviewRequest",
            "MobPublicationRequest",
            "MobValidationResponse",
            "MobMutationResponse",
            "MobAuthoringOptionsResponse",
            'JsonPropertyName("mob_definition_id")',
            'JsonPropertyName("publication_state")',
            'JsonPropertyName("visual_texture_path")',
            'JsonPropertyName("primary_combat_profile")',
            'JsonPropertyName("combat_bonuses")',
            'JsonPropertyName("guaranteed_drops")',
            'JsonPropertyName("preview_signature")',
            "EquipmentCombatBonusDefinition? CombatBonuses",
        ):
            self.assertIn(token, source)

        self.assertNotIn("MobCombatBonusDefinition", source)
        for forbidden in (
            "spawn_id",
            "region_id",
            "map_id",
            "home_tile",
            "leash",
            "respawn",
            "patrol",
            "probability",
            "weight",
            "script",
        ):
            self.assertNotIn(forbidden, source)

    def test_mob_feature_registers_foundation_without_routes(self) -> None:
        feature = (ROOT / "host" / "Features" / "Mobs" / "MobAuthoringFeature.cs").read_text()
        aggregator = (ROOT / "host" / "Features" / "AuthoringFeatureExtensions.cs").read_text()

        for token in (
            "AddSingleton<MobAuthoringRegistry>()",
            "AddSingleton<IAuthoringSchemaRequirementProvider, MobSchemaRequirements>()",
            "AddSingleton<IAuthoringCatalogSectionProvider, MobCatalogSectionProvider>()",
            "MapMobAuthoring(",
        ):
            self.assertIn(token, feature)

        self.assertIn("services.AddMobAuthoring();", aggregator)
        self.assertIn("endpoints.MapMobAuthoring();", aggregator)
        self.assertNotIn('new PlannedCatalogSectionProvider("mobs"', aggregator)

        for forbidden in ("MapGroup", "MapGet", "MapPost", "MapPut", "/mobs"):
            self.assertNotIn(forbidden, feature)

    def test_mob_schema_requirements_are_feature_owned(self) -> None:
        source = (ROOT / "host" / "Features" / "Mobs" / "MobSchemaRequirements.cs").read_text()
        for token in (
            'FeatureId => "prototype-mob-authoring-v1"',
            'AuthoringSchemaRequirement.Table("mob_definitions")',
            'AuthoringSchemaRequirement.Column("mob_definitions", "mob_definition_id")',
            'AuthoringSchemaRequirement.Column("mob_definitions", "combat_faction_id")',
            'AuthoringSchemaRequirement.Column("mob_combat_profiles", "attack_speed_units")',
            'AuthoringSchemaRequirement.Column("mob_combat_bonuses", "attack_thrust")',
            'AuthoringSchemaRequirement.Column("mob_drops", "stack_count")',
            'AuthoringSchemaRequirement.Constraint("mob_definitions_proactive_targeting_check")',
            'AuthoringSchemaRequirement.Constraint("mob_combat_profiles_level_bounds_check")',
            'AuthoringSchemaRequirement.Constraint("mob_drops_unique_item_per_mob")',
        ):
            self.assertIn(token, source)

        for forbidden in (
            "AuthoringSchemaRequirement.Index",
            "AuthoringSchemaRequirement.ForeignKey",
            "spawn",
            "respawn",
            "patrol",
        ):
            self.assertNotIn(forbidden, source)

    def test_content_studio_guide_is_a_reference_not_a_copied_runtime_guide(self) -> None:
        guide = (ROOT / "docs" / "CONTENT_AUTHORING_GUIDE.md").read_text()
        self.assertIn("Content Authoring Guide Reference", guide)
        self.assertIn("MMO-Project/docs/development/CONTENT_AUTHORING_GUIDE.md", guide)
        self.assertIn("Adding a New Mob", guide)
        self.assertIn("T4_MOB_DOMAIN_AUDIT.md", guide)
        self.assertLess(len(guide.splitlines()), 40)
        self.assertNotIn("## Adding a Consumable Item", guide)
        self.assertNotIn("prototype/sql/*.sql", guide)


if __name__ == "__main__":
    unittest.main()
