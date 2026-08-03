#!/usr/bin/env python3
"""Source contracts for the host feature-module boundary."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"


class HostFeatureModuleTests(unittest.TestCase):
    def test_program_is_composition_only(self) -> None:
        program = (HOST / "Program.cs").read_text()
        self.assertIn("AddAuthoringFeatures()", program)
        self.assertIn("MapAuthoringFeatures()", program)
        self.assertNotIn('MapGet($"{AuthoringApi.RoutePrefix}/items', program)
        self.assertNotIn('MapGet($"{AuthoringApi.RoutePrefix}/consumables', program)
        self.assertNotIn('MapGet($"{AuthoringApi.RoutePrefix}/equipment', program)
        self.assertLess(len(program.splitlines()), 130)

    def test_feature_aggregator_registers_and_maps_each_workspace(self) -> None:
        feature = (HOST / "Features" / "AuthoringFeatureExtensions.cs").read_text()
        for token in (
            "AddItemAuthoring()",
            "AddConsumableAuthoring()",
            "AddEquipmentAuthoring()",
            "MapItemAuthoring()",
            "MapConsumableAuthoring()",
            "MapEquipmentAuthoring()",
        ):
            self.assertIn(token, feature)

    def test_each_feature_owns_registration_and_routes(self) -> None:
        expectations = {
            "Items/ItemAuthoringFeature.cs": (
                "BasicItemRepository",
                'MapGroup("/items")',
                'MapPost("/assets/items/import"',
            ),
            "Consumables/ConsumableAuthoringFeature.cs": (
                "ConsumableItemRepository",
                'MapGroup($"{AuthoringApi.RoutePrefix}/consumables")',
                'MapPost("/{itemId}/preview"',
            ),
            "Equipment/EquipmentAuthoringFeature.cs": (
                "EquipmentItemRepository",
                'MapGroup($"{AuthoringApi.RoutePrefix}/equipment")',
                'MapPost("/{itemId}/preview"',
            ),
            "Mobs/MobAuthoringFeature.cs": (
                "MobRepository",
                'MapGroup($"{AuthoringApi.RoutePrefix}/mobs")',
                'MapPost("/{mobDefinitionId}/preview"',
            ),
        }
        for relative_path, tokens in expectations.items():
            content = (HOST / "Features" / relative_path).read_text()
            for token in tokens:
                self.assertIn(token, content)

    def test_http_result_mapping_is_shared(self) -> None:
        results = (HOST / "Http" / "AuthoringHttpResults.cs").read_text()
        for token in (
            "item_not_found",
            "item_version_conflict",
            "database_unavailable",
            "Status503ServiceUnavailable",
        ):
            self.assertIn(token, results)

        program = (HOST / "Program.cs").read_text()
        self.assertNotIn("static IResult ToHttpResult", program)
        self.assertFalse(program.rstrip().endswith("ExpectedUpdatedAtUtc);"))

    def test_publication_request_is_a_shared_contract(self) -> None:
        publication = (HOST / "Contracts" / "PublicationContracts.cs").read_text()
        self.assertIn("PublicationMutationRequest", publication)
        self.assertIn('JsonPropertyName("expected_updated_at_utc")', publication)


if __name__ == "__main__":
    unittest.main()
