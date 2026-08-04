"""Source contracts for U1 item-level tool capability independence."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
MMO_ROOT = ROOT.parents[1]


class U1ToolCapabilityIndependenceTests(unittest.TestCase):
    def test_u1_migration_is_mirrored_and_only_removes_obsolete_guards(self) -> None:
        content_studio = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "023_item_tool_capability_independence.sql"
        )
        runtime = MMO_ROOT / "prototype" / "sql" / "023_item_tool_capability_independence.sql"

        self.assertTrue(content_studio.exists())
        self.assertTrue(runtime.exists())
        self.assertEqual(content_studio.read_text(), runtime.read_text())

        migration = content_studio.read_text()
        for token in (
            "DROP TRIGGER IF EXISTS item_tool_capabilities_hand_slot_guard",
            "DROP TRIGGER IF EXISTS item_definitions_tool_capability_slot_guard",
            "DROP FUNCTION IF EXISTS ensure_item_tool_capabilities_hand_slot()",
            "DROP FUNCTION IF EXISTS prevent_non_hand_slot_with_tool_capabilities()",
        ):
            self.assertIn(token, migration)
        for forbidden in (
            "DROP TABLE",
            "DELETE FROM ITEM_TOOL_CAPABILITIES",
            "CREATE TABLE",
            "INSERT INTO ITEM_TOOL_CAPABILITIES",
        ):
            self.assertNotIn(forbidden, migration.upper())

    def test_schema_health_requires_old_hand_slot_guards_to_be_absent(self) -> None:
        requirement_model = (ROOT / "host" / "Health" / "AuthoringSchemaRequirement.cs").read_text()
        inspector = (ROOT / "host" / "Health" / "SchemaHealthInspector.cs").read_text()
        item_requirements = (
            ROOT / "host" / "Features" / "Items" / "ItemSchemaRequirements.cs"
        ).read_text()

        self.assertIn("AbsentTrigger", requirement_model)
        self.assertIn("AuthoringSchemaRequirementKind.AbsentTrigger", inspector)
        self.assertIn("Obsolete trigger", inspector)
        self.assertIn("023_item_tool_capability_independence.sql", inspector)
        self.assertIn(
            'AbsentTrigger("item_tool_capabilities", "item_tool_capabilities_hand_slot_guard")',
            item_requirements,
        )
        self.assertIn(
            'AbsentTrigger("item_definitions", "item_definitions_tool_capability_slot_guard")',
            item_requirements,
        )

    def test_repository_preserves_tool_rows_when_equipment_metadata_is_cleared(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "UnifiedItemRepository.cs").read_text()

        self.assertIn("ReplaceEquipmentAsync", repository)
        self.assertIn("await ReplaceToolCapabilitiesAsync(connection, transaction, itemId, draft.ToolCapabilities, cancellationToken);", repository)
        cleanup = repository.split("private static async Task ReplaceEquipmentAsync", 1)[1]
        cleanup = cleanup.split("private static async Task ReplaceRequirementsAsync", 1)[0]
        self.assertNotIn('"item_tool_capabilities"', cleanup)

    def test_host_validation_allows_non_equipable_and_wearable_tools(self) -> None:
        validator = (ROOT / "host" / "Services" / "UnifiedItemValidator.cs").read_text()
        domain = (ROOT / "host" / "Services" / "UnifiedItemDomainRules.cs").read_text()

        self.assertIn("ValidateToolCapabilitiesAsync(draft.ToolCapabilities", validator)
        self.assertIn("ValidateToolCapabilitiesAsync(", validator)
        self.assertIn("NormalizeToolCapabilities", domain)
        self.assertIn("equipment is null", domain)
        self.assertNotIn("or tool capabilities.", validator)

    def test_godot_sends_tool_capabilities_independent_of_equipability(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()

        self.assertIn('"tool_capabilities": _collect_tool_capabilities()', editor)
        self.assertIn("_add_tool_row", editor)
        self.assertIn("Tool capabilities work from inventory or equipment. Equipability is optional.", editor)
        self.assertNotIn("_collect_tool_capabilities() if equippable and hand_slot else []", editor)
        self.assertNotIn("combat bonuses, and tool capabilities.", editor)
        self.assertNotIn("weapon profile and tool capability rows.", editor)

    def test_docs_record_u1_scope_without_claiming_unified_workspace(self) -> None:
        readme = (ROOT / "README.md").read_text()
        roadmap = (ROOT / "docs" / "ROADMAP.md").read_text()
        api = (ROOT / "docs" / "API_V1.md").read_text()
        integration = (ROOT / "integrations" / "mmo-project" / "README.md").read_text()
        runtime_readme = (MMO_ROOT / "prototype" / "sql" / "README.md").read_text()
        runtime_ownership = (MMO_ROOT / "prototype" / "sql" / "MODULE_OWNERSHIP.md").read_text()
        api_inline = api.replace("\n", " ")

        for document in (readme, roadmap, api, integration, runtime_readme, runtime_ownership):
            self.assertIn("tool capabilities", document.lower())
        self.assertIn("U1 tool-capability independence and metadata safety is implemented", readme)
        self.assertIn("U2 added the unified item host aggregate and temporary compatibility adapters", roadmap)
        self.assertIn("U3 replaced specialization tabs with one contextual Items workspace", roadmap)
        self.assertIn("U5 runtime tool resolution implemented in MMO Project", roadmap)
        self.assertIn("do not require equipability or a hand slot", api_inline)
        self.assertIn("023_item_tool_capability_independence.sql", integration)
        self.assertIn("023_item_tool_capability_independence.sql", runtime_readme)
        self.assertIn("023_item_tool_capability_independence.sql", runtime_ownership)


if __name__ == "__main__":
    unittest.main()
