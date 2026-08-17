#!/usr/bin/env python3
"""Source contracts for U4 unified item retirement."""

from __future__ import annotations

import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"
SCRIPTS = ROOT / "content-studio" / "scripts"


class U4UnifiedItemRetirementTests(unittest.TestCase):
    def test_legacy_item_route_groups_are_not_registered(self) -> None:
        sources = "\n".join(path.read_text() for path in (HOST / "Features").rglob("*.cs"))
        for route in (
            "/api/v1/consumables",
            "/api/v1/equipment",
            "/api/v1/hand-equipment",
            'MapGroup($"{AuthoringApi.RoutePrefix}/consumables")',
            'MapGroup($"{AuthoringApi.RoutePrefix}/equipment")',
            'MapGroup($"{AuthoringApi.RoutePrefix}/hand-equipment")',
        ):
            self.assertNotIn(route, sources)

    def test_unified_item_routes_are_the_only_item_mutation_surface(self) -> None:
        feature = (HOST / "Features" / "Items" / "ItemAuthoringFeature.cs").read_text()
        for token in (
            'MapGroup("/items")',
            'MapGet("/options"',
            "PreviewItemRequest request",
            "SaveItemDraftRequest request",
            "ItemPublicationRequest request",
            'MapPost("/{itemId}/delete"',
            "UnifiedItemAuthoringService",
        ):
            self.assertIn(token, feature)
        for token in ("JsonElement request", "IsUnifiedItemRequest", "PreviewBasicAsync", "SaveBasicDraftAsync"):
            self.assertNotIn(token, feature)

    def test_obsolete_repositories_services_and_editors_are_removed(self) -> None:
        for relative in (
            "host/Persistence/BasicItemRepository.cs",
            "host/Persistence/ConsumableItemRepository.cs",
            "host/Persistence/EquipmentItemRepository.cs",
            "host/Persistence/HandEquipmentRepository.cs",
            "host/Services/BasicItemAuthoringService.cs",
            "host/Services/ConsumableItemAuthoringService.cs",
            "host/Services/EquipmentItemAuthoringService.cs",
            "host/Services/HandEquipmentAuthoringService.cs",
            "content-studio/scripts/consumable_editor.gd",
            "content-studio/scripts/equipment_editor.gd",
            "content-studio/scripts/hand_equipment_editor.gd",
        ):
            self.assertFalse((ROOT / relative).exists(), relative)

    def test_unified_item_repository_and_validator_are_authoritative(self) -> None:
        feature = (HOST / "Features" / "Items" / "ItemAuthoringFeature.cs").read_text()
        self.assertIn("IUnifiedItemRepository, UnifiedItemRepository", feature)
        self.assertIn("UnifiedItemValidator", feature)
        self.assertIn("UnifiedItemAuthoringService", feature)
        for token in (
            "BasicItemRepository",
            "ConsumableItemRepository",
            "EquipmentItemRepository",
            "HandEquipmentRepository",
            "BasicItemAuthoringService",
            "ConsumableItemAuthoringService",
            "EquipmentItemAuthoringService",
            "HandEquipmentAuthoringService",
        ):
            self.assertNotIn(token, feature)

    def test_client_and_scene_reference_only_current_editors(self) -> None:
        scene_sources = "\n".join(path.read_text() for path in (ROOT / "content-studio").rglob("*.tscn"))
        client = (SCRIPTS / "authoring_host_client.gd").read_text()
        for token in ("consumable_editor", "equipment_editor", "hand_equipment_editor"):
            self.assertNotIn(token, scene_sources)
            self.assertNotIn(token, client)
        for route in ("/api/v1/consumables", "/api/v1/equipment", "/api/v1/hand-equipment"):
            self.assertNotIn(route, client)

    def test_one_item_catalog_and_schema_boundary_remain(self) -> None:
        catalog = (HOST / "Features" / "Items" / "ItemCatalogSectionProvider.cs").read_text()
        schema = (HOST / "Features" / "Items" / "ItemSchemaRequirements.cs").read_text()
        self.assertIn("UnifiedItemAuthoringService", catalog)
        for token in (
            "item_definitions",
            "item_consumable_profiles",
            "item_skill_requirements",
            "item_combat_profiles",
            "item_tool_capabilities",
        ):
            self.assertIn(token, schema)

    def test_u5_runtime_tool_resolution_is_status_only_in_content_studio(self) -> None:
        contracts = (HOST / "Contracts" / "ItemContracts.cs").read_text()
        service = (HOST / "Services" / "UnifiedItemAuthoringService.cs").read_text()
        validator = (HOST / "Services" / "UnifiedItemValidator.cs").read_text()
        self.assertIn('supports_runtime_tool_resolution', contracts)
        self.assertIn("UnifiedItemDomainRules.MaximumPowerTier,\n                    true,", service)
        self.assertIn("runtime_tool_execution_deferred", validator)

    def test_mmo_project_checkout_changes_are_limited_to_u5_tool_resolution(self) -> None:
        parent = ROOT.parents[1]
        if not (parent / ".git").exists():
            self.skipTest("MMO Project checkout is unavailable.")
        result = subprocess.run(
            ["git", "status", "--short"],
            cwd=parent,
            check=True,
            capture_output=True,
            text=True,
        )
        allowed_paths = (
            "docs/development/CONTENT_AUTHORING_GUIDE.md",
            "docs/design/DIALOGUE_FOUNDATION_V1.md",
            "docs/design/OSRS_STYLE_NPC_CONVERSATIONS_AND_QUEST_GATES.md",
            "docs/modernization/CURRENT_HANDOFF.md",
            "docs/modernization/DIALOGUE_QUEST_AND_CUTSCENE_ROADMAP.md",
            "docs/modernization/GAMEPLAY_SYSTEM_ROADMAP.md",
            "prototype/importer/",
            "prototype/server/Program.cs",
            "prototype/server/features/README.md",
            "prototype/server/features/dialogue/application/DialogueConditionEvaluator.cs",
            "prototype/server/features/dialogue/application/DialogueChoiceEffectSettlementService.cs",
            "prototype/server/features/dialogue/application/DialogueDefinitionCatalog.cs",
            "prototype/server/features/dialogue/application/DialogueSessionService.cs",
            "prototype/server/features/dialogue/host/DialogueCommandHandlers.cs",
            "prototype/server/features/dialogue/host/DialogueChoiceEffectSettlementRecoveryWorker.cs",
            "prototype/server/features/dialogue/persistence/",
            "prototype/server/features/inventory/persistence/CharacterInventoryRepository.cs",
            "prototype/server/features/inventory/persistence/CharacterInventoryRecord.cs",
            "prototype/server/features/npcs/application/NpcRuntimeService.cs",
            "prototype/server/features/quests/application/QuestDefinitionCatalog.cs",
            "prototype/server/features/runtime/application/GameRuntimeEvent.cs",
            "prototype/server/features/runtime/host/GameRuntimeEventProjector.cs",
            "prototype/server/features/session/host/SessionHandshakeCoordinator.cs",
            "prototype/server/features/static_content/application/",
            "prototype/server/features/tools/",
            "prototype/shared/dialogues/catalog.json",
            "prototype/shared/maps/generated/starter_region/",
            "prototype/shared/maps/npcs/",
            "prototype/shared/maps/tiled/mmoproject.tmx",
            "prototype/shared/maps/tiled/regions/starter_region.tmj",
            "prototype/shared/maps/tiled/regions/starter_region.tmx",
            "prototype/sql/MODULE_OWNERSHIP.md",
            "prototype/sql/README.md",
            "prototype/sql/024_npc_authoring_schema.sql",
            "prototype/sql/025_seed_existing_npc_definitions.sql",
            "prototype/sql/026_dialogue_authoring_schema.sql",
            "prototype/sql/027_seed_existing_dialogue_definitions.sql",
            "prototype/sql/043_quest_transition_evidence_lifecycle_delete.sql",
            "prototype/sql/045_typed_dialogue_conditions_v1.sql",
            "prototype/tests/MMO.Project.Prototype.MapPublisher.Tests/MapPublisher/DialogueCatalogExporterTests.cs",
            "prototype/tests/MMO.Project.Prototype.MapPublisher.Tests/MapPublisher/NpcCatalogExporterTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/CombatActorRuntimeProviderTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/CharacterToolPossessionRepositoryContractTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/CharacterToolResolverTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueCatalogExporterConditionTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueChoiceEffectSettlement",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueEffectLifecycleLockContractTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueConditionEvaluatorTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueDefinitionCatalogTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueSeedMigrationTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueSessionServiceTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/QuestStatePersistenceAcceptanceTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/GeneratedRegionRuntimeAdapterTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/MapPublisher/",
            "prototype/tools/MapPublisher/",
            "tools/MMO-Content-Studio",
            "tools/mmoproject.tiled-session",
        )
        unrelated = []
        for line in result.stdout.splitlines():
            path = line[3:]
            if not path.startswith(allowed_paths):
                unrelated.append(line)
        self.assertEqual([], unrelated)


if __name__ == "__main__":
    unittest.main()
