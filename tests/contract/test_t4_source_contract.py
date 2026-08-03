#!/usr/bin/env python3
"""Source contracts for the T4B mob authoring host boundary."""

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
            'JsonPropertyName("attack_speed_unit_milliseconds")',
            'JsonPropertyName("supported_limits")',
            'JsonPropertyName("factions")',
            'JsonPropertyName("published_drop_items")',
            'JsonPropertyName("visual_assets")',
            'JsonPropertyName("editable_in_mobs")',
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

    def test_mob_feature_owns_registration_and_routes(self) -> None:
        feature = (ROOT / "host" / "Features" / "Mobs" / "MobAuthoringFeature.cs").read_text()
        aggregator = (ROOT / "host" / "Features" / "AuthoringFeatureExtensions.cs").read_text()

        for token in (
            "AddSingleton<MobRepository>()",
            "AddSingleton<MobAuthoringRegistry>()",
            "AddSingleton<MobDefinitionValidator>()",
            "AddSingleton<MobAuthoringService>()",
            "AddSingleton<IAuthoringSchemaRequirementProvider, MobSchemaRequirements>()",
            "AddSingleton<IAuthoringCatalogSectionProvider, MobCatalogSectionProvider>()",
            "MapMobAuthoring(",
            'MapGroup($"{AuthoringApi.RoutePrefix}/mobs")',
            'MapGet("/options"',
            'MapGet(string.Empty',
            'MapGet("/{mobDefinitionId}"',
            'MapPost("/{mobDefinitionId}/preview"',
            'MapPut("/{mobDefinitionId}/draft"',
            'MapPost("/{mobDefinitionId}/publish"',
            'MapPost("/{mobDefinitionId}/disable"',
            "AuthoringHttpResults.FromOperation",
            "CancellationToken cancellationToken",
        ):
            self.assertIn(token, feature)

        self.assertIn("services.AddMobAuthoring();", aggregator)
        self.assertIn("endpoints.MapMobAuthoring();", aggregator)
        self.assertNotIn('new PlannedCatalogSectionProvider("mobs"', aggregator)

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

    def test_mob_repository_service_and_validator_own_t4b_behavior(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "MobRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "MobAuthoringService.cs").read_text()
        validator = (ROOT / "host" / "Services" / "MobDefinitionValidator.cs").read_text()

        for token in (
            "class MobRepository",
            "BeginTransactionAsync",
            "for update",
            "EnsureExpectedVersion",
            "ReplaceCombatProfileAsync",
            "ReplaceCombatBonusesAsync",
            "ReplaceGuaranteedDropsAsync",
            "LoadFactionsAsync",
            "LoadDropItemsAsync",
            "CommitAsync",
            "updated_at = now()",
        ):
            self.assertIn(token, repository)

        for token in (
            "class MobAuthoringService",
            "ComputePreviewSignature",
            "SHA256.HashData",
            "IsMatchingPreview",
            "preview_signature_mismatch",
            "mob_version_conflict",
            "Equivalent(",
            "reload-and-verify",
            "unsaved_mob_changes",
            "mob_spawn_reference_guard_deferred",
            "SetPublicationAsync",
        ):
            self.assertIn(token, service)

        for token in (
            "class MobDefinitionValidator",
            "ValidateIdentity",
            "ValidateVisuals",
            "ResolveGameAssetPng",
            "ValidateFactionAndTargeting",
            "ValidateCombatProfile",
            "ValidateGuaranteedDrops",
            "unpublished_mob_drop_item",
            "mob_combat_profile_required",
        ):
            self.assertIn(token, validator)

    def test_t4b_does_not_add_godot_or_runtime_mob_editor(self) -> None:
        self.assertFalse((ROOT / "content-studio" / "scripts" / "mob_editor.gd").exists())
        self.assertFalse((ROOT / "content-studio" / "scenes" / "MobEditor.tscn").exists())
        self.assertFalse(
            (MMO_PROJECT / "prototype" / "sql" / "019_mob_authoring_schema.sql").exists()
        )

    def test_mob_repository_keeps_spawn_and_random_drop_fields_out(self) -> None:
        source = "\n".join(
            [
                (ROOT / "host" / "Persistence" / "MobRepository.cs").read_text(),
                (ROOT / "host" / "Services" / "MobAuthoringService.cs").read_text(),
                (ROOT / "host" / "Services" / "MobDefinitionValidator.cs").read_text(),
                (ROOT / "host" / "Contracts" / "MobContracts.cs").read_text(),
            ]
        )
        for forbidden in (
            "spawn_id",
            "region_id",
            "map_id",
            "home_tile",
            "leash_radius",
            "respawn",
            "patrol",
            "probability",
            "weight",
            "drop_chance",
            "random_quantity",
            "roll_group",
            "script_id",
            "script_body",
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
