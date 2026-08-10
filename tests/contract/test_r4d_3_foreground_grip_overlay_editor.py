#!/usr/bin/env python3
"""Source contracts for the R4D.3 shared foreground grip overlay editor."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "content-studio" / "scripts"
HOST = ROOT / "host"


class ForegroundGripOverlayEditorTests(unittest.TestCase):
    def test_shared_state_tracks_socket_and_overlay_lifecycles(self) -> None:
        state = (SCRIPTS / "actor_socket_calibration_state.gd").read_text()
        for token in (
            "foreground_overlay_overrides",
            "resolve_effective_rectangle",
            "set_foreground_overlay_override",
            "revert_foreground_overlay_override",
            "_saved_foreground_overlay_overrides",
            '"foreground_overlay_overrides": foreground_overlay_overrides.duplicate(true)',
        ):
            self.assertIn(token, state)

    def test_shared_editor_and_canvas_own_rectangle_geometry(self) -> None:
        editor = (SCRIPTS / "actor_socket_calibration_editor.gd").read_text()
        canvas = (SCRIPTS / "actor_socket_calibration_canvas.gd").read_text()
        for token in (
            "Foreground Grip Overlay",
            "Create Actor Override",
            "Inherited Rig Rectangle",
            "No Rectangle for This Pose",
            "_on_rectangle_changed",
            "actor_kind",
            "visual_texture_path",
        ):
            self.assertIn(token, editor)
        for token in (
            "rectangle_changed",
            "set_rectangle",
            "top_left",
            "top_right",
            "bottom_left",
            "bottom_right",
            "_update_rectangle_drag",
        ):
            self.assertIn(token, canvas)

    def test_host_keeps_overlay_contract_in_existing_route_and_catalog(self) -> None:
        contracts = (HOST / "Contracts" / "ActorAppearanceContracts.cs").read_text()
        service = (HOST / "Services" / "ActorRigCalibrationAuthoringService.cs").read_text()
        self.assertIn('JsonPropertyName("foreground_overlay_overrides")', contracts)
        self.assertIn("TryParseForegroundOverlayOverrides", service)
        self.assertIn("MergeForegroundOverlayOverrides", service)
        self.assertIn("foreground_overlay_rectangle_out_of_bounds", service)
        self.assertIn("ActorCalibrationFrameResolver", service)


if __name__ == "__main__":
    unittest.main()
