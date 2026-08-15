#!/usr/bin/env python3
"""Source contracts for RNG-L2 loot-table authoring and EV tooling."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"
SCRIPTS = ROOT / "content-studio" / "scripts"
SCENES = ROOT / "content-studio" / "scenes"


class LootTableAuthoringContractTests(unittest.TestCase):
    def test_host_feature_exposes_normalized_loot_table_routes(self) -> None:
        feature = (
            HOST / "Features" / "LootTables" / "LootTableAuthoringFeature.cs"
        ).read_text()
        for token in (
            'MapGroup($"{AuthoringApi.RoutePrefix}/loot-tables")',
            'MapGet("/options"',
            'MapPost("/{lootTableId}/preview"',
            'MapPut("/{lootTableId}/draft"',
            'MapPost("/{lootTableId}/publish"',
            'MapPost("/{lootTableId}/disable"',
            'MapPost("/{lootTableId}/delete"',
            "AddSingleton<ILootTableRepository, LootTableRepository>()",
            "LootTableExpectedValueCalculator",
        ):
            self.assertIn(token, feature)

    def test_contracts_expose_pre_roll_success_and_exact_ev(self) -> None:
        contracts = (HOST / "Contracts" / "LootTableContracts.cs").read_text()
        for token in (
            "PreRollSuccessSequenceBehavior",
            "PreRollSuccessMainBehavior",
            "LootExpectedValueReport",
            "LootExpectedItemTotal",
            "LootExpectedPathContribution",
            "LootExactValue",
            "CurrencyInjectionConfigured",
        ):
            self.assertIn(token, contracts)

    def test_mob_authoring_exposes_root_binding_without_runtime_consumption(self) -> None:
        mob_contracts = (HOST / "Contracts" / "MobContracts.cs").read_text()
        mob_service = (HOST / "Services" / "MobAuthoringService.cs").read_text()
        mob_validator = (HOST / "Services" / "MobDefinitionValidator.cs").read_text()
        for source in (mob_contracts, mob_service, mob_validator):
            self.assertIn("RootLootTableId", source)
        self.assertIn("LoadLootTableOptionsAsync", mob_service)
        self.assertIn("published_mob_legacy_and_root_loot_conflict", mob_validator)

    def test_godot_client_and_scene_include_loot_tables_workspace(self) -> None:
        client = (SCRIPTS / "authoring_host_client.gd").read_text()
        editor = (SCRIPTS / "loot_table_editor.gd").read_text()
        scene = (SCENES / "Main.tscn").read_text()
        main = (SCRIPTS / "main.gd").read_text()
        for token in (
            "loot_table_options_received",
            "load_loot_tables",
            "preview_loot_table",
            "save_loot_table_draft",
            "publish_loot_table",
            "disable_loot_table",
            "delete_loot_table",
        ):
            self.assertIn(token, client)
        for token in ("Groups JSON", "expected_value", "Apply Previewed Operation"):
            self.assertIn(token, editor)
        self.assertIn("loot_table_editor.gd", scene)
        self.assertIn("loot_table_editor", main)
        self.assertIn('"loot_tables"', main)


if __name__ == "__main__":
    unittest.main()
