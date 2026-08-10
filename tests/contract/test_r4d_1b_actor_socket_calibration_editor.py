#!/usr/bin/env python3
"""Source contracts for the R4D.1B shared actor socket calibration editor."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "content-studio" / "scripts"


class ActorSocketCalibrationEditorTests(unittest.TestCase):
    def test_shared_editor_and_canvas_own_calibration_behavior(self) -> None:
        editor = (SCRIPTS / "actor_socket_calibration_editor.gd").read_text()
        canvas = (SCRIPTS / "actor_socket_calibration_canvas.gd").read_text()
        state = (SCRIPTS / "actor_socket_calibration_state.gd").read_text()

        for token in (
            "class_name ActorSocketCalibrationEditor",
            "load_actor_calibration_frames",
            "Load / Create Calibration",
            "Use This Calibration for Actor",
            "Revert to Rig Default",
            "Save Calibration",
            "Reload Calibration",
            "actor_calibration_catalog_conflict",
            "Unsaved calibration changes",
            "Socket is outside this source frame.",
        ):
            self.assertIn(token, editor)

        for token in (
            "class_name ActorSocketCalibrationCanvas",
            "TEXTURE_FILTER_NEAREST",
            "quantize_source_pixel",
            "clampf(source_point.x, 0.0",
            "_draw_pixel_grid",
            "PADDING := 64.0",
        ):
            self.assertIn(token, canvas)

        for token in (
            "resolve_effective_point",
            "socket_overrides.duplicate(true)",
            "revert_override",
            "save_payload",
            "COORDINATE_LIMIT := 4096",
        ):
            self.assertIn(token, state)

    def test_npc_and_mob_are_thin_context_adapters(self) -> None:
        for name, actor_kind in (("npc_editor.gd", "npc"), ("mob_editor.gd", "mob")):
            editor = (SCRIPTS / name).read_text()
            self.assertIn('preload("res://scripts/actor_socket_calibration_editor.gd")', editor)
            self.assertIn("Actor Socket Calibration", editor)
            self.assertIn("_refresh_socket_calibration_editor", editor)
            self.assertIn(f'"actor_kind": "{actor_kind}"', editor)
            self.assertIn("_on_use_socket_calibration_for_actor", editor)
            self.assertIn("Validate and apply", editor)

            for forbidden in ("HTTPRequest", "Npgsql", "SELECT ", "INSERT INTO", "UPDATE "):
                self.assertNotIn(forbidden, editor)

    def test_normal_harness_runs_the_new_godot_fixture(self) -> None:
        test_script = (ROOT / "tools" / "test.sh").read_text()
        fixture = ROOT / "content-studio" / "tests" / "actor_socket_calibration_fixture_test.gd"

        self.assertTrue(fixture.exists())
        self.assertIn("actor_socket_calibration_fixture_test.gd", test_script)
        self.assertIn("complete socket override dictionary", fixture.read_text())

