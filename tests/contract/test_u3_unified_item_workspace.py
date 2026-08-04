"""Source contracts for the U3 unified Godot Items workspace."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class U3UnifiedItemWorkspaceTests(unittest.TestCase):
    def test_unified_item_editor_exists_and_owns_complete_workspace(self) -> None:
        editor_path = ROOT / "content-studio" / "scripts" / "item_editor.gd"
        self.assertTrue(editor_path.exists())
        editor = editor_path.read_text()
        for token in (
            "class_name UnifiedItemEditor",
            "Identity and Inventory",
            "Consumable Behavior",
            "Equipability",
            "Equipped Appearance",
            "Requirements and Skill Modifiers",
            "Combat Bonuses",
            "Weapon Profile",
            "Tool Capabilities",
            "Complete Item Definition",
        ):
            self.assertIn(token, editor)

    def test_normal_navigation_uses_one_items_tab(self) -> None:
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        self.assertEqual(scene.count('[node name="Items" type="HBoxContainer" parent="Margin/Root/Tabs"]'), 1)
        self.assertIn('path="res://scripts/item_editor.gd"', scene)
        self.assertIn('script = ExtResource("3_items")', scene)
        self.assertNotIn('[node name="Consumables" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)
        self.assertNotIn('[node name="Equipment" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)
        self.assertNotIn('[node name="Weapons & Tools" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)

    def test_legacy_editors_remain_as_u4_cleanup_candidates_only(self) -> None:
        for relative in (
            "content-studio/scripts/consumable_editor.gd",
            "content-studio/scripts/equipment_editor.gd",
            "content-studio/scripts/hand_equipment_editor.gd",
        ):
            self.assertTrue((ROOT / relative).exists(), relative)
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        self.assertNotIn("consumable_editor.gd", scene)
        self.assertNotIn("equipment_editor.gd", scene)
        self.assertNotIn("hand_equipment_editor.gd", scene)

    def test_editor_uses_unified_client_and_workspace_support_without_direct_transport(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        for token in (
            "AuthoringHostClient",
            "WORKSPACE_SUPPORT_SCRIPT",
            "_workspace_support.accept_preview",
            "_workspace_support.clear_preview",
            "_workspace_support.can_apply",
            "_workspace_support.render_changes",
            "_workspace_support.render_validation",
        ):
            self.assertIn(token, editor)
        for forbidden in (
            "HTTPRequest",
            "AuthoringHttpTransport",
            "JSON.parse_string",
            "/api/v1/consumables",
            "/api/v1/equipment",
            "/api/v1/hand-equipment",
            "insert into",
            "update item_",
            "npgsql",
        ):
            self.assertNotIn(forbidden.lower(), editor.lower())

    def test_client_exposes_unified_item_route_family_and_startup(self) -> None:
        client = (ROOT / "content-studio" / "scripts" / "authoring_host_client.gd").read_text()
        for token in (
            "signal item_options_received",
            "signal item_catalog_received",
            "signal item_definition_received",
            "signal item_preview_received",
            "signal item_mutation_completed",
            "signal item_delete_completed",
            "func load_item_options",
            "func search_item_catalog",
            "func load_item_definition",
            "func preview_item_operation",
            "func save_complete_item_draft",
            '"/api/v1/items/options"',
            '"/api/v1/items%s"',
            '"/api/v1/items/%s"',
            '"/api/v1/items/%s/preview"',
            '"/api/v1/items/%s/draft"',
            '"/api/v1/items/%s/publish"',
            '"/api/v1/items/%s/disable"',
            '"/api/v1/items/%s/delete"',
            '"preview_signature": preview_signature',
        ):
            self.assertIn(token, client)
        startup = client.split("OP_ITEM_ASSETS:", 1)[1].split("OP_CONSUMABLE_OPTIONS:", 1)[0]
        self.assertIn('"/api/v1/items/options"', startup)
        self.assertIn('"/api/v1/items"', startup)
        self.assertNotIn("/api/v1/consumables", startup)
        self.assertNotIn("/api/v1/equipment", startup)
        self.assertNotIn("/api/v1/hand-equipment", startup)

    def test_complete_aggregate_payload_preserves_independent_specializations(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        payload = editor.split("func _payload() -> Dictionary:", 1)[1].split("func _preview()", 1)[0]
        for token in (
            '"display_name": _display_name.text',
            '"icon_texture_path": _selected_metadata(_icon)',
            '"consumable_behavior": _consumable_payload() if _consumable_enabled.button_pressed else null',
            '"equipment": _equipment_payload() if _equipable.button_pressed else null',
            '"tool_capabilities": _collect_tool_capabilities()',
            '"expected_updated_at_utc": _current_item.get("updated_at_utc", null)',
            '"preview_signature": null',
        ):
            self.assertIn(token, payload)
        self.assertIn('"requirements": _collect_consumable_requirements()', editor)
        self.assertIn('"effects": _collect_consumable_effects()', editor)
        self.assertIn('"requirements": _collect_requirements()', editor)
        self.assertIn('"skill_modifiers": _collect_modifiers()', editor)
        self.assertIn('"combat_bonuses": _collect_bonuses()', editor)
        self.assertIn('"weapon_profile": _weapon_profile_payload()', editor)

    def test_tool_capabilities_stay_independent_of_equipability(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        self.assertIn("Tool capabilities work from inventory or equipment. Equipability is optional.", editor)
        self.assertIn("_tool_section.visible = true", editor)
        self.assertIn('"tool_capabilities": _collect_tool_capabilities()', editor)
        self.assertNotIn("_collect_tool_capabilities() if _equipable", editor)
        self.assertNotIn("_collect_tool_capabilities() if equippable", editor)
        self.assertIn("Disabling equipability removes equipment requirements, modifiers, combat bonuses, and weapon profile. Tool capabilities and consumable behavior remain.", editor)

    def test_contextual_weapon_profile_and_combat_bonus_contract(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        self.assertIn("DEFAULT_WEAPON_CAPABLE_SLOTS", editor)
        self.assertIn("_is_weapon_capable_slot", editor)
        self.assertIn("not _is_weapon_capable_slot(_selected_metadata(_equipment_slot))", editor)
        self.assertIn("attack units x", editor)
        for field in (
            "attack_thrust",
            "attack_slash",
            "attack_crush",
            "attack_ranged",
            "attack_magic",
            "strength_melee",
            "strength_ranged",
            "strength_magic",
            "defence_thrust",
            "defence_slash",
            "defence_crush",
            "defence_ranged",
            "defence_magic",
        ):
            self.assertIn(field, editor)

    def test_preview_apply_lifecycle_uses_signature_concurrency_reload_and_catalog(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        for token in (
            'payload["target_operation"] = _selected_metadata(_operation)',
            "payload.erase(\"preview_signature\")",
            "preview_signature: String = _workspace_support.preview_signature",
            "_workspace_support.can_apply(operation, preview_signature)",
            'payload["preview_signature"] = preview_signature',
            "_client.save_complete_item_draft(item_id, payload)",
            "_client.publish_item(item_id, expected, preview_signature)",
            "_client.disable_item(item_id, expected, preview_signature)",
            "_client.delete_item(item_id, expected, preview_signature)",
            "_reload_item_id = item_id",
            "_client.search_item_catalog(_search.text)",
            "_current_item.get(\"updated_at_utc\", null)",
            "_item_id.editable = true",
        ):
            self.assertIn(token, editor)

    def test_no_runtime_tool_resolution_or_mmo_project_paths_are_added(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        for forbidden in (
            "CharacterToolCapabilityResolver",
            "prototype/server",
            "MMO Project/prototype",
            "WorldStaticContentSnapshot",
        ):
            self.assertNotIn(forbidden, editor)


if __name__ == "__main__":
    unittest.main()
