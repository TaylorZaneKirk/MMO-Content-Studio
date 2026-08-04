"""Source contracts for the unified item-authoring planning boundary."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class UnifiedItemAuthoringDocsTests(unittest.TestCase):
    def test_audit_documents_current_overlap_and_tool_coupling(self):
        audit = (ROOT / "docs" / "UNIFIED_ITEM_AUTHORING_AUDIT.md").read_text()

        for required in (
            "# Unified Item Authoring Audit",
            "Current Feature Ownership",
            "Tool-Capability Dependency",
            "Metadata Deletion Behavior",
            "Publication Lifecycle",
            "item_tool_capabilities_hand_slot_guard",
            "item_definitions_tool_capability_slot_guard",
            "DeleteAllEquipmentMetadataAsync",
            "ValidateNotEquippable",
            "hand_equipment_editor.gd",
            "CharacterCombatProfileResolver.WeaponSlotId",
            "FoodRestoreDefinitions",
            "no runtime path consumes",
        ):
            self.assertIn(required, audit)

    def test_plan_locks_unified_aggregate_and_safe_migration(self):
        plan = (ROOT / "docs" / "UNIFIED_ITEM_AUTHORING_PLAN.md").read_text()

        for required in (
            "# Unified Item Authoring Plan",
            "Tool capability does not require equipability.",
            "Removing equipability must preserve tool capabilities.",
            "One preview signature covers the complete normalized item draft.",
            "Drop `item_tool_capabilities_hand_slot_guard`.",
            "completed adapter retirement",
            "/api/v1/items/options",
            "Weapon Profile shows only when a weapon-capable slot is selected.",
            "Rank by higher `power_tier`",
            "prefer equipped items.",
            "U1 - Domain And Schema Correction",
            "U5 - Runtime Tool-Resolution Integration",
            "two_handed",
        ):
            self.assertIn(required, plan)

    def test_project_docs_link_plan_without_claiming_implementation(self):
        readme = (ROOT / "README.md").read_text()
        roadmap = (ROOT / "docs" / "ROADMAP.md").read_text()
        architecture = (ROOT / "docs" / "ARCHITECTURE.md").read_text()

        for document in (readme, roadmap, architecture):
            self.assertIn("UNIFIED_ITEM_AUTHORING_AUDIT.md", document)
            self.assertIn("UNIFIED_ITEM_AUTHORING_PLAN.md", document)

        self.assertIn("unified item-authoring boundary", architecture.lower())
        self.assertIn("Status:** U4 obsolete route/tab retirement implemented", roadmap)
        self.assertIn("one contextual Godot Items", readme)


if __name__ == "__main__":
    unittest.main()
