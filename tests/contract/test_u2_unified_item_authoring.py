"""Source contracts for U2 unified item host aggregate."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class U2UnifiedItemAuthoringTests(unittest.TestCase):
    def test_unified_contract_repository_service_and_validator_exist(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "ItemContracts.cs").read_text()
        for token in (
            "public sealed record ItemDefinition(",
            "public sealed record ItemDefinitionSummary(",
            "public sealed record ItemCatalogResponse(",
            "public sealed record ItemOptionsResponse(",
            "public sealed record ItemConsumableBehaviorDefinition(",
            "public sealed record ItemEquipmentMetadataDefinition(",
            "public sealed record SaveItemDraftRequest(",
            "public sealed record PreviewItemRequest(",
            "public sealed record ItemPreviewResponse(",
            "public sealed record ItemMutationResponse(",
            'JsonPropertyName("preview_signature")',
            'JsonPropertyName("editable_in_basic_items")',
        ):
            self.assertIn(token, contracts)

        for path in (
            ROOT / "host" / "Persistence" / "UnifiedItemRepository.cs",
            ROOT / "host" / "Services" / "UnifiedItemDomainRules.cs",
            ROOT / "host" / "Services" / "UnifiedItemValidator.cs",
            ROOT / "host" / "Services" / "UnifiedItemAuthoringService.cs",
        ):
            self.assertTrue(path.exists(), path)

    def test_unified_repository_owns_complete_transactional_replacement(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "UnifiedItemRepository.cs").read_text()
        for token in (
            "BeginTransactionAsync",
            "for update of i",
            "EnsureExpectedVersion",
            "ReplaceConsumableAsync",
            "ReplaceEquipmentAsync",
            "ReplaceWeaponProfileAsync",
            "ReplaceToolCapabilitiesAsync",
            "updated_at = now()",
            "LoadAggregateAsync(connection, transaction",
            "CommitAsync",
        ):
            self.assertIn(token, repository)
        cleanup = repository.split("private static async Task ReplaceEquipmentAsync", 1)[1]
        cleanup = cleanup.split("private static async Task ReplaceRequirementsAsync", 1)[0]
        self.assertNotIn('"item_tool_capabilities"', cleanup)

    def test_unified_service_owns_signature_and_compatibility_adapters(self) -> None:
        service = (ROOT / "host" / "Services" / "UnifiedItemAuthoringService.cs").read_text()
        for token in (
            "ComputePreviewSignature",
            "NormalizedItemDraft",
            "EquivalentDraft",
            "PreviewBasicAsync",
            "SaveBasicDraftAsync",
            "PreviewConsumableAsync",
            "SaveConsumableDraftAsync",
            "PreviewEquipmentAsync",
            "SaveEquipmentDraftAsync",
            "PreviewHandEquipmentAsync",
            "SaveHandEquipmentDraftAsync",
            "ApplyConsumable",
            "ApplyEquipment",
            "ApplyHandEquipment",
            "current.ConsumableBehavior",
            "current.ToolCapabilities",
            "preview_signature_mismatch",
        ):
            self.assertIn(token, service)

    def test_routes_expose_unified_items_and_keep_legacy_adapters(self) -> None:
        items = (ROOT / "host" / "Features" / "Items" / "ItemAuthoringFeature.cs").read_text()
        consumables = (ROOT / "host" / "Features" / "Consumables" / "ConsumableAuthoringFeature.cs").read_text()
        equipment = (ROOT / "host" / "Features" / "Equipment" / "EquipmentAuthoringFeature.cs").read_text()
        hand = (ROOT / "host" / "Features" / "HandEquipment" / "HandEquipmentAuthoringFeature.cs").read_text()

        for token in (
            "UnifiedItemRepository",
            "UnifiedItemValidator",
            "UnifiedItemAuthoringService",
            'items.MapGet("/options"',
            "JsonElement request",
            "IsUnifiedItemRequest",
            "PreviewItemRequest",
            "SaveItemDraftRequest",
            "PreviewBasicAsync",
            "SaveBasicDraftAsync",
            "HandEquipmentPublicationRequest",
        ):
            self.assertIn(token, items)

        self.assertIn("PreviewConsumableAsync", consumables)
        self.assertIn("SaveConsumableDraftAsync", consumables)
        self.assertIn("PreviewEquipmentAsync", equipment)
        self.assertIn("SaveEquipmentDraftAsync", equipment)
        self.assertIn("PreviewHandEquipmentAsync", hand)
        self.assertIn("SaveHandEquipmentDraftAsync", hand)
        self.assertNotIn("await service.SaveDraftAsync(itemId, request, cancellationToken)", consumables + equipment + hand)

    def test_godot_workspace_is_consolidated_after_u3(self) -> None:
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        main = (ROOT / "content-studio" / "scripts" / "main.gd").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        for token in ("Items", "Mobs", "Environment"):
            self.assertIn(token, scene + main)
        self.assertIn("UnifiedItemEditor", editor)
        self.assertNotIn('[node name="Consumables" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)
        self.assertNotIn('[node name="Equipment" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)
        self.assertNotIn('[node name="Weapons & Tools" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)

    def test_docs_record_u2_and_u3_without_claiming_u4_or_runtime_tool_resolution(self) -> None:
        readme = (ROOT / "README.md").read_text()
        roadmap = (ROOT / "docs" / "ROADMAP.md").read_text()
        api = (ROOT / "docs" / "API_V1.md").read_text()
        architecture = (ROOT / "docs" / "ARCHITECTURE.md").read_text()
        acceptance = (ROOT / "docs" / "UNIFIED_ITEM_AUTHORING_ACCEPTANCE.md").read_text()

        for document in (readme, roadmap, api, architecture, acceptance):
            self.assertIn("U2", document)
        self.assertIn("U3 unified Godot Items workspace implemented", roadmap)
        self.assertIn("obsolete route/tab retirement and runtime tool resolution remain pending", roadmap)
        self.assertIn("U3 consolidates the Godot item workflow", api)
        self.assertIn("unified routes require server-issued", api)
        self.assertIn("uniformly require the new signature field", api)
        self.assertIn("Legacy Basic Items payloads and the Consumables, Equipment, and Weapons &", acceptance)


if __name__ == "__main__":
    unittest.main()
