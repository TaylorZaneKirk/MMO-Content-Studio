#!/usr/bin/env python3
"""Source contracts for the R4D.1A actor socket calibration seam."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"
SCRIPTS = ROOT / "content-studio" / "scripts"


class ActorSocketCalibrationAuthoringTests(unittest.TestCase):
    def test_actor_appearance_feature_owns_the_narrow_route_family(self) -> None:
        feature = (HOST / "Features" / "ActorAppearance" / "ActorAppearanceAuthoringFeature.cs").read_text()
        for token in (
            'MapGroup($"{AuthoringApi.RoutePrefix}/actor-appearance")',
            'MapGet("/calibrations/{calibrationId}"',
            'MapPut("/calibrations/{calibrationId}"',
            'MapPost("/calibration-frames"',
            "ActorRigCalibrationAuthoringService",
            "ActorCalibrationFrameResolver",
        ):
            self.assertIn(token, feature)

        aggregate = (HOST / "Features" / "AuthoringFeatureExtensions.cs").read_text()
        self.assertIn("AddActorAppearanceAuthoring();", aggregate)
        self.assertIn("MapActorAppearanceAuthoring();", aggregate)

    def test_calibration_service_preserves_file_backed_safety_contract(self) -> None:
        service = (HOST / "Services" / "ActorRigCalibrationAuthoringService.cs").read_text()
        for token in (
            "actor_calibration_catalog_conflict",
            "expected_catalog_hash",
            "CoordinateLimit = 4096",
            "TryGetDecimal(out var numericValue)",
            "decimal.Truncate(numericValue) != numericValue",
            "decimal.ToInt32(numericValue)",
            "TryParseSocketOverrides",
            "WriteTemporaryFile",
            "ReadCurrentCatalogBytes",
            "FileOptions.WriteThrough",
            "File.Move(temporaryPath, catalog.Value.Path, true)",
            "CanonicalizeSockets",
            "CalibrationIdPattern",
        ):
            self.assertIn(token, service)

    def test_godot_client_and_layout_expose_only_thin_future_ui_seams(self) -> None:
        client = (SCRIPTS / "authoring_host_client.gd").read_text()
        for token in (
            "actor_calibration_received",
            "actor_calibration_saved",
            "actor_calibration_frames_received",
            "load_actor_calibration",
            "save_actor_calibration",
            "load_actor_calibration_frames",
        ):
            self.assertIn(token, client)

        layout = (SCRIPTS / "rigged_sprite_preview_layout.gd").read_text()
        for token in (
            "preview_transform",
            "source_to_preview",
            "preview_to_source",
            "quantize_source_pixel",
        ):
            self.assertIn(token, layout)

        for editor in ("npc_editor.gd", "mob_editor.gd"):
            content = (SCRIPTS / editor).read_text()
            self.assertNotIn("load_actor_calibration(", content)
            self.assertNotIn("save_actor_calibration(", content)

    def test_documentation_records_the_authoring_boundary(self) -> None:
        documentation = (ROOT / "docs" / "ACTOR_SOCKET_CALIBRATION_AUTHORING.md").read_text()
        for token in (
            "game_client_assets",
            "expected_catalog_hash",
            "-4096",
            "R4D.1B",
            "R4D.2",
            "R4D.3",
            "exact source art",
        ):
            self.assertIn(token, documentation)


if __name__ == "__main__":
    unittest.main()
