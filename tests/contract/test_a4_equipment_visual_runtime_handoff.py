from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
MMO_PROJECT_ROOT = ROOT.parent.parent


class A4EquipmentVisualRuntimeHandoffTests(unittest.TestCase):
    def test_runtime_migration_matches_content_studio_handoff(self) -> None:
        studio_migration = (
            ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "028_item_equipped_visuals.sql"
        ).read_text()
        runtime_migration = (
            MMO_PROJECT_ROOT / "prototype" / "sql" / "028_item_equipped_visuals.sql"
        ).read_text()

        for token in (
            "CREATE TABLE IF NOT EXISTS item_equipped_visuals",
            "CREATE TABLE IF NOT EXISTS item_equipped_visual_pose_anchors",
            "asset_key TEXT NOT NULL",
            "rig_id TEXT NOT NULL",
            "binding_type TEXT NOT NULL",
            "render_layer_id TEXT NOT NULL",
            "socket_id TEXT NULL",
            "secondary_socket_id TEXT NULL",
            "nudge_x INTEGER NOT NULL",
            "nudge_y INTEGER NOT NULL",
            "direction IN ('N', 'E', 'S', 'W')",
            "frame >= 1 AND frame <= 4",
        ):
            self.assertIn(token, studio_migration)
            self.assertIn(token, runtime_migration)

    def test_runtime_publisher_uses_item_id_catalog_export(self) -> None:
        publisher = (ROOT / "host" / "Services" / "RuntimeCatalogPublisherService.cs").read_text()
        item_service = (ROOT / "host" / "Services" / "UnifiedItemAuthoringService.cs").read_text()

        self.assertIn("export-equipment-visual-catalog", publisher)
        self.assertIn("client/actors/appearance/data/equipped_visuals/published_catalog_v1.json", publisher)
        self.assertIn("verified.RuntimeEnabled", item_service)
        self.assertIn("PublishCatalogsAsync", item_service)


if __name__ == "__main__":
    unittest.main()
