#!/usr/bin/env python3
"""Source contracts for the D2 dialogue authoring migration."""

from __future__ import annotations

import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MIGRATION = ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "026_dialogue_authoring_schema.sql"


class D2DialogueSchemaContractTests(unittest.TestCase):
    def test_migration_exists_with_expected_tables_columns_and_constraints(self) -> None:
        sql = MIGRATION.read_text()

        for token in (
            "CREATE TABLE IF NOT EXISTS dialogue_definitions",
            "dialogue_definition_id TEXT PRIMARY KEY",
            "display_name TEXT NOT NULL",
            "publication_state TEXT NOT NULL DEFAULT 'Draft'",
            "schema_version INTEGER NOT NULL DEFAULT 1",
            "metadata_description TEXT NULL",
            "notes TEXT NULL",
            "created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()",
            "updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()",
            "CREATE TABLE IF NOT EXISTS dialogue_entry_points",
            "entry_id TEXT NOT NULL",
            "priority INTEGER NOT NULL",
            "entry_order INTEGER NOT NULL",
            "CREATE TABLE IF NOT EXISTS dialogue_nodes",
            "node_type TEXT NOT NULL",
            "speaker TEXT NULL",
            "next_node_id TEXT NULL",
            "dismissible BOOLEAN NOT NULL",
            "canvas_x DOUBLE PRECISION NOT NULL",
            "canvas_y DOUBLE PRECISION NOT NULL",
            "editor_notes TEXT NULL",
            "CREATE TABLE IF NOT EXISTS dialogue_choices",
            "choice_id TEXT NOT NULL",
            "target_node_id TEXT NOT NULL",
            "choice_order INTEGER NOT NULL",
            "dialogue_definitions_id_format_check",
            "dialogue_definitions_publication_state_check",
            "dialogue_definitions_schema_version_positive_check",
            "dialogue_entry_points_entry_id_format_check",
            "dialogue_nodes_supported_node_type_check",
            "dialogue_nodes_canvas_finite_check",
            "dialogue_choices_choice_id_format_check",
            "dialogue_choices_text_nonblank_check",
        ):
            self.assertIn(token, sql)

    def test_migration_keeps_d2_scope_narrow_and_additive(self) -> None:
        sql = MIGRATION.read_text()
        forbidden = (
            "quest_id",
            "quest_stage",
            "objective",
            "reward",
            "effect_",
            "script_payload",
            "script_expression",
            "portrait",
            "audio",
            "localization",
        )
        for token in forbidden:
            self.assertNotIn(token, sql)

        self.assertNotIn("DROP TABLE", sql)
        self.assertNotIn("ALTER TABLE item_", sql)
        self.assertNotIn("ALTER TABLE mob_", sql)
        self.assertNotIn("ALTER TABLE npc_", sql)
        self.assertIn("touch_dialogue_definition_updated_at", sql)
        self.assertIn("dialogue_nodes_touch_definition_updated_at", sql)

    def test_layout_fields_are_authoring_only_node_columns(self) -> None:
        sql = MIGRATION.read_text()
        root_section = sql.split("CREATE TABLE IF NOT EXISTS dialogue_definitions", 1)[1].split(
            "CREATE TABLE IF NOT EXISTS dialogue_nodes", 1
        )[0]
        node_section = sql.split("CREATE TABLE IF NOT EXISTS dialogue_nodes", 1)[1].split(
            "CREATE TABLE IF NOT EXISTS dialogue_choices", 1
        )[0]

        self.assertNotIn("canvas_x", root_section)
        self.assertNotIn("canvas_y", root_section)
        self.assertIn("canvas_x", node_section)
        self.assertIn("canvas_y", node_section)
        self.assertIn("editor_notes", node_section)

    def test_mmo_project_checkout_was_not_modified(self) -> None:
        result = subprocess.run(
            ["git", "status", "--short", "--untracked-files=no"],
            cwd=ROOT.parents[1],
            check=True,
            capture_output=True,
            text=True,
        )
        allowed = {
            "M tools/MMO-Content-Studio",
            "M tools/mmoproject.tiled-session",
            "M docs/development/CONTENT_AUTHORING_GUIDE.md",
            "M docs/design/DIALOGUE_FOUNDATION_V1.md",
            "M docs/design/OSRS_STYLE_NPC_CONVERSATIONS_AND_QUEST_GATES.md",
            "M prototype/importer/README.md",
            "M prototype/server/features/dialogue/application/DialogueDefinitionCatalog.cs",
            "M prototype/server/features/dialogue/application/DialogueSessionService.cs",
            "M prototype/shared/dialogues/catalog.json",
            "M prototype/sql/MODULE_OWNERSHIP.md",
            "M prototype/sql/027_seed_existing_dialogue_definitions.sql",
            "M prototype/sql/README.md",
            "M prototype/tools/MapPublisher/Program.cs",
            "M prototype/tools/MapPublisher/DialogueCatalogExporter.cs",
            "M prototype/tests/MMO.Project.Prototype.MapPublisher.Tests/MapPublisher/DialogueCatalogExporterTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueDefinitionCatalogTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueSessionServiceTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/GeneratedRegionRuntimeAdapterTests.cs",
            "m tools/MMO-Content-Studio"
        }
        unexpected = [line for line in result.stdout.splitlines() if line.strip() not in allowed]
        self.assertEqual([], unexpected)


if __name__ == "__main__":
    unittest.main()
