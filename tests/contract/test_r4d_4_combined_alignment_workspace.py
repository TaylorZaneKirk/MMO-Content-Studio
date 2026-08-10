#!/usr/bin/env python3
"""Source contracts for the R4D.4 combined actor/item alignment workspace."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "content-studio" / "scripts"


class CombinedAlignmentWorkspaceTests(unittest.TestCase):
    def test_combined_canvas_owns_source_space_preview_math(self) -> None:
        source = (SCRIPTS / "actor_item_alignment_canvas.gd").read_text()
        math = (SCRIPTS / "actor_attachment_alignment.gd").read_text()

        for token in (
            "class_name ActorItemAlignmentCanvas",
            "signal socket_dragged",
            "signal grip_anchor_dragged",
            "_composition_bounds = _composition_bounds.merge",
            "_item_z_index < 0",
            "_draw_markers()",
            "source_to_preview",
            "preview_to_source",
            "RIGGED_PREVIEW_LAYOUT.quantize_source_pixel",
        ):
            self.assertIn(token, source)

        for token in (
            "class_name ActorAttachmentAlignment",
            "resolve_effective_grip_anchor",
            "resolve_item_position",
            "resolve_authored_grip_anchor",
            "mirror_effective_point",
        ):
            self.assertIn(token, math)

    def test_editor_keeps_socket_grip_and_overlay_ownership_separate(self) -> None:
        source = (SCRIPTS / "actor_socket_calibration_editor.gd").read_text()
        item = (SCRIPTS / "item_editor.gd").read_text()

        for token in (
            '"socket"',
            '"grip"',
            '"foreground_overlay"',
            "_on_alignment_socket_dragged",
            "_on_alignment_grip_anchor_dragged",
            "_state.set_override",
            "_set_current_grip_anchor",
            "_open_item_save_workflow",
            "item_grip_handoff_requested.emit",
            "_copy_current_value_to_target",
            "_mirror_current_value_to_target",
            "exact source widths and pose flip metadata",
        ):
            self.assertIn(token, source)

        self.assertIn("stage_grip_anchor_handoff", item)
        self.assertIn("Validate and save this complete item draft", item)
        self.assertNotIn("_save_calibration()", item)

    def test_normal_harness_and_docs_cover_combined_alignment(self) -> None:
        harness = (ROOT / "tools" / "test.sh").read_text()
        socket_docs = (ROOT / "docs" / "ACTOR_SOCKET_CALIBRATION_AUTHORING.md").read_text()
        grip_docs = (ROOT / "docs" / "EQUIPMENT_GRIP_ANCHOR_AUTHORING.md").read_text()
        overlay_docs = (ROOT / "docs" / "FOREGROUND_GRIP_OVERLAY_AUTHORING.md").read_text()

        self.assertIn("actor_item_alignment_fixture_test.gd", harness)
        self.assertIn("R4D.4", socket_docs)
        self.assertIn("Open Item Save Workflow", socket_docs)
        self.assertIn("Combined Actor Alignment Handoff", grip_docs)
        self.assertIn("Combined-workspace boundary", overlay_docs)


if __name__ == "__main__":
    unittest.main()
