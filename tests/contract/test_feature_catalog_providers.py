#!/usr/bin/env python3
"""Source contracts for additive feature-owned catalog sections."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
FEATURES = ROOT / "host" / "Features"


class FeatureCatalogProviderTests(unittest.TestCase):
    def test_shared_provider_contract_is_ordered_and_async(self) -> None:
        contract = (
            FEATURES / "Catalog" / "IAuthoringCatalogSectionProvider.cs"
        ).read_text()
        for token in (
            "interface IAuthoringCatalogSectionProvider",
            "string ContentType",
            "int SortOrder",
            "Task<ContentCatalogSection> LoadAsync",
            "PlannedCatalogSectionProvider",
        ):
            self.assertIn(token, contract)

    def test_each_implemented_feature_owns_its_catalog_projection(self) -> None:
        expectations = {
            "Items": ("ItemCatalogSectionProvider.cs", "items", "BasicItemAuthoringService", "item.ItemId"),
            "Consumables": (
                "ConsumableCatalogSectionProvider.cs",
                "consumables",
                "ConsumableItemAuthoringService",
                "item.HasConsumableProfile",
            ),
            "Equipment": (
                "EquipmentCatalogSectionProvider.cs",
                "equipment",
                "EquipmentItemAuthoringService",
                "item.Equippable",
            ),
            "HandEquipment": (
                "HandEquipmentCatalogSectionProvider.cs",
                "hand_equipment",
                "HandEquipmentAuthoringService",
                "EquipmentItemRepository.IsHandSlot",
            ),
            "Mobs": (
                "MobCatalogSectionProvider.cs",
                "mobs",
                'new ContentCatalogSection(',
                "false",
            ),
        }
        for feature, (file_name, *tokens) in expectations.items():
            source = (FEATURES / feature / file_name).read_text()
            for token in tokens:
                self.assertIn(token, source, feature)

    def test_feature_modules_register_their_catalog_provider(self) -> None:
        expectations = (
            ("Items", "ItemAuthoringFeature.cs", "ItemCatalogSectionProvider"),
            ("Consumables", "ConsumableAuthoringFeature.cs", "ConsumableCatalogSectionProvider"),
            ("Equipment", "EquipmentAuthoringFeature.cs", "EquipmentCatalogSectionProvider"),
            ("HandEquipment", "HandEquipmentAuthoringFeature.cs", "HandEquipmentCatalogSectionProvider"),
            ("Mobs", "MobAuthoringFeature.cs", "MobCatalogSectionProvider"),
        )
        for feature, file_name, provider in expectations:
            source = (FEATURES / feature / file_name).read_text()
            self.assertIn(
                f"IAuthoringCatalogSectionProvider, {provider}",
                source,
                feature,
            )

    def test_catalog_service_only_aggregates_providers(self) -> None:
        service = (ROOT / "host" / "Services" / "ContentCatalogService.cs").read_text()
        self.assertIn("IEnumerable<IAuthoringCatalogSectionProvider>", service)
        self.assertIn("provider.SortOrder", service)
        self.assertIn("provider.LoadAsync", service)
        self.assertIn("EnsureUniqueContentTypes", service)
        for forbidden in (
            "BasicItemAuthoringService",
            "ConsumableItemAuthoringService",
            "EquipmentItemAuthoringService",
            "HandEquipmentAuthoringService",
            "MobAuthoringRegistry",
            "item.HasConsumableProfile",
            "item.Equippable",
        ):
            self.assertNotIn(forbidden, service)

    def test_only_unimplemented_future_sections_are_planned_in_aggregator(self) -> None:
        aggregator = (FEATURES / "AuthoringFeatureExtensions.cs").read_text()
        self.assertIn('new PlannedCatalogSectionProvider("npcs", "NPCs", 500)', aggregator)
        self.assertNotIn('new PlannedCatalogSectionProvider("mobs", "Mobs", 400)', aggregator)
        self.assertIn("services.AddMobAuthoring();", aggregator)


if __name__ == "__main__":
    unittest.main()
