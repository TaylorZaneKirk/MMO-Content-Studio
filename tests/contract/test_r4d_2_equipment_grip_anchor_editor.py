#!/usr/bin/env python3
"""Source contracts for R4D.2 equipment grip-anchor authoring hardening."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class EquipmentGripAnchorEditorTests(unittest.TestCase):
    def test_item_workspace_uses_explicit_grip_anchor_terms_and_context_gate(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()

        for token in (
            "Grip Anchor X/Y",
            "Actor Socket (gold, read-only)",
            "Item Grip Anchor (pink, draggable)",
            "func _can_edit_grip_anchor()",
            "_grip_pose_art_available",
            "Item art: unavailable for %s/F%d",
            "_appearance_grip_row.visible = socket_binding",
            "_appearance_grip_actions.visible = socket_binding",
        ):
            self.assertIn(token, editor)

    def test_preview_separates_exact_calibration_from_presentation_fallback(self) -> None:
        preview = (ROOT / "content-studio" / "scripts" / "paper_doll_preview.gd").read_text()

        for token in (
            "grip_anchor_authoring: bool = false",
            "var exact_selected_item_pose := grip_anchor_authoring and selected_visual",
            "func _load_texture(layer_id: String, asset_key: String, frame: int, direction: String, exact_only: bool = false)",
            "var frames := [frame] if exact_only else _frame_fallbacks(frame, direction)",
            "Item art unavailable for %s/F%d",
            '"clamp_grip_to_texture": _grip_anchor_authoring',
            "x = clampi(x, 0, int(texture_size.x) - 1)",
            "y = clampi(y, 0, int(texture_size.y) - 1)",
        ):
            self.assertIn(token, preview)

    def test_normal_harness_and_documentation_cover_grip_authoring(self) -> None:
        harness = (ROOT / "tools" / "test.sh").read_text()
        guide = ROOT / "docs" / "EQUIPMENT_GRIP_ANCHOR_AUTHORING.md"

        self.assertIn("equipment_grip_anchor_fixture_test.gd", harness)
        self.assertTrue(guide.exists())
        self.assertIn("Exact Art Required for Calibration", guide.read_text())


if __name__ == "__main__":
    unittest.main()
