#!/usr/bin/env python3
"""Fast source-level checks for the T1 basic-item authoring slice."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class T1SourceContractTests(unittest.TestCase):
    def test_t1_item_routes_exist(self) -> None:
        feature = (
            ROOT / "host" / "Features" / "Items" / "ItemAuthoringFeature.cs"
        ).read_text()
        for route in (
            'MapGet("/assets/items"',
            'MapPost("/assets/items/import"',
            'MapGroup("/items")',
            'MapGet("/{itemId}"',
            'MapPost("/{itemId}/preview"',
            'MapPut("/{itemId}/draft"',
            'MapPost("/{itemId}/publish"',
            'MapPost("/{itemId}/disable"',
        ):
            self.assertIn(route, feature)

    def test_basic_item_writes_are_transactional_and_reload_verified(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "BasicItemRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "BasicItemAuthoringService.cs").read_text()
        self.assertGreaterEqual(repository.count("BeginTransactionAsync"), 2)
        self.assertGreaterEqual(repository.count("CommitAsync"), 2)
        self.assertIn("for update", repository.lower())
        self.assertIn("reload-and-verify", service)

    def test_basic_items_never_write_equipment_metadata(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "BasicItemRepository.cs").read_text()
        self.assertIn("equipment_slot_id", repository)
        self.assertIn("null,", repository)
        self.assertIn("BasicItemKindConflictException", repository)
        self.assertNotIn("insert into item_combat_profiles", repository.lower())
        self.assertNotIn("insert into item_combat_bonuses", repository.lower())

    def test_draft_and_publication_are_explicit(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "BasicItemRepository.cs").read_text()
        validator = (ROOT / "host" / "Services" / "BasicItemValidator.cs").read_text()
        self.assertIn("runtime_enabled", repository)
        self.assertIn("false", repository)
        self.assertIn("ValidForPublication", validator)
        self.assertIn("item_icon_unavailable", validator)

    def test_preview_rejects_unknown_operations(self) -> None:
        service = (ROOT / "host" / "Services" / "BasicItemAuthoringService.cs").read_text()
        self.assertIn("invalid_target_operation", service)
        self.assertIn('"save_draft" => "save_draft"', service)
        self.assertIn("_ => null", service)

    def test_godot_requires_preview_before_apply(self) -> None:
        main = (ROOT / "content-studio" / "scripts" / "main.gd").read_text()
        self.assertIn("_preview_signature", main)
        self.assertIn("Preview the operation again", main)
        self.assertIn("Apply Previewed Operation", main)

    def test_godot_has_no_database_driver_or_sql(self) -> None:
        godot_sources = "\n".join(
            path.read_text()
            for path in (ROOT / "content-studio").rglob("*.gd")
        ).lower()
        self.assertNotIn("npgsql", godot_sources)
        self.assertNotIn("insert into", godot_sources)
        self.assertNotIn("update item_definitions", godot_sources)

    def test_asset_import_is_guarded_and_non_overwriting(self) -> None:
        service = (ROOT / "host" / "Services" / "ItemAssetAuthoringService.cs").read_text()
        self.assertIn("PngSignature", service)
        self.assertIn("MaximumPngBytes", service)
        self.assertIn("asset_name_conflict", service)
        self.assertIn("FilesMatchAsync", service)
        self.assertIn("File.Move", service)

    def test_asset_paths_are_contained_under_configured_root(self) -> None:
        service = (ROOT / "host" / "Services" / "ItemAssetService.cs").read_text()
        self.assertIn('ItemResourcePrefix = "res://assets/items/"', service)
        self.assertIn("resolves outside the configured asset root", service)
        self.assertIn("File.Exists", service)

    def test_existing_item_mutations_require_concurrency_token(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "BasicItemRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "BasicItemAuthoringService.cs").read_text()
        self.assertIn("expectedUpdatedAtUtc is null", repository)
        self.assertIn("expected is null", service)
        self.assertIn("item_version_conflict", service)

    def test_disabling_respects_live_gameplay_references(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "BasicItemRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "BasicItemAuthoringService.cs").read_text()
        schema = (
            ROOT / "host" / "Features" / "Items" / "ItemSchemaRequirements.cs"
        ).read_text()
        self.assertIn("HasLiveReferencesAsync", repository)
        for table in ("character_inventory", "character_equipment", "ground_items"):
            self.assertIn(table, repository)
            self.assertIn(table, schema)
        self.assertIn("item_has_live_references", service)
        self.assertIn("IsLiveReferenceGuard", service)
        self.assertIn("item_definitions_runtime_disable_guard", schema)
        validator = (ROOT / "host" / "Services" / "BasicItemValidator.cs").read_text()
        self.assertIn("static_content_references_not_checked", validator)

    def test_godot_functions_are_not_duplicated(self) -> None:
        main = (ROOT / "content-studio" / "scripts" / "main.gd").read_text()
        function_names = [
            line.split("func ", 1)[1].split("(", 1)[0]
            for line in main.splitlines()
            if line.startswith("func ")
        ]
        self.assertEqual(len(function_names), len(set(function_names)))

    def test_godot_scene_exposes_every_unique_node_used_by_main_script(self) -> None:
        import re

        main = (ROOT / "content-studio" / "scripts" / "main.gd").read_text()
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        unique_names = re.findall(r"= %(\w+)", main)
        self.assertGreater(len(unique_names), 0)
        for name in unique_names:
            node_marker = f'[node name="{name}"'
            self.assertIn(node_marker, scene, name)
            node_start = scene.index(node_marker)
            next_node = scene.find("\n[node ", node_start + 1)
            node_block = scene[node_start:] if next_node < 0 else scene[node_start:next_node]
            self.assertIn("unique_name_in_owner = true", node_block, name)

    def test_authoring_host_matches_mmo_server_runtime_baseline(self) -> None:
        project = (ROOT / "host" / "MMO.ContentStudio.AuthoringHost.csproj").read_text()
        self.assertIn("<TargetFramework>net10.0</TargetFramework>", project)
        self.assertIn('PackageReference Include="Npgsql" Version="10.0.3"', project)


if __name__ == "__main__":
    unittest.main()
