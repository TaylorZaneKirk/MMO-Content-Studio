#!/usr/bin/env python3
"""Source contracts for the D4 MMO Project dialogue runtime handoff."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class D4DialogueRuntimeHandoffTests(unittest.TestCase):
    def test_integration_artifacts_include_schema_and_seed(self) -> None:
        schema = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "026_dialogue_authoring_schema.sql"
        ).read_text()
        seed = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "027_seed_existing_dialogue_definitions.sql"
        ).read_text()

        self.assertIn("CREATE TABLE IF NOT EXISTS dialogue_definitions", schema)
        self.assertIn("CREATE TABLE IF NOT EXISTS dialogue_nodes", schema)
        self.assertIn("test_npc_greeting", seed)
        self.assertIn("'Published'", seed)
        self.assertIn("'speaker_text'", seed)
        self.assertIn("'player_choice'", seed)
        self.assertIn("'end'", seed)

    def test_seed_preserves_existing_dialogue_aggregate_on_rerun(self) -> None:
        seed = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "027_seed_existing_dialogue_definitions.sql"
        ).read_text()
        collapsed = " ".join(seed.split())

        self.assertIn(
            "IF NOT EXISTS ( SELECT 1 FROM dialogue_definitions "
            "WHERE dialogue_definition_id = 'test_npc_greeting' ) THEN",
            collapsed,
        )
        self.assertIn("END IF;", seed)
        self.assertNotIn("ON CONFLICT", seed)
        self.assertNotIn("DO UPDATE", seed)
        self.assertNotIn("DELETE FROM dialogue_choices", seed)
        self.assertNotIn("DELETE FROM dialogue_entry_points", seed)
        self.assertNotIn("DELETE FROM dialogue_nodes", seed)
        self.assertNotIn("updated_at_utc = NOW()", seed)

    def test_runtime_catalog_capability_is_enabled_without_adjacent_features(self) -> None:
        registry = (ROOT / "host" / "Services" / "DialogueAuthoringRegistry.cs").read_text()

        self.assertIn("public DialogueOperationCapabilities LoadCapabilities() => new(", registry)
        self.assertIn("true,\n        true,\n        false,\n        true,\n        false,\n        false,\n        false,\n        false", registry)

    def test_docs_describe_d4_handoff_without_hot_reload_or_quest_scope(self) -> None:
        docs = "\n".join(
            [
                (ROOT / "README.md").read_text(),
                (ROOT / "docs" / "ROADMAP.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_ACCEPTANCE.md").read_text(),
                (ROOT / "integrations" / "mmo-project" / "README.md").read_text(),
            ]
        )

        self.assertIn("D4 MMO Project runtime catalog handoff implemented", docs)
        self.assertIn("export-dialogue-catalog", docs)
        self.assertIn("only Published definitions", docs)
        self.assertIn("D1-D5 Dialogue Studio authoring", docs)
        self.assertIn("QV3 typed read-only quest/item predicates", docs)
        self.assertIn("hot reload", docs)
        self.assertNotIn("Quest authoring implemented", docs)
        self.assertNotIn("D5 hardening and playthrough verification remain pending", docs)


if __name__ == "__main__":
    unittest.main()
