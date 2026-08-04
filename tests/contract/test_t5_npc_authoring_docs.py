#!/usr/bin/env python3
"""Source contracts for the T5 NPC authoring audit and planning boundary."""

from __future__ import annotations

import os
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def _mmo_project_candidates() -> list[Path]:
    configured = os.environ.get("MMO_PROJECT_ROOT")
    candidates: list[Path] = []
    if configured:
        candidates.append(Path(configured))
    candidates.extend(
        [
            ROOT.parents[1],
            ROOT.parents[1] / "MMO-Project" / "MMO-Project",
            ROOT.parents[1] / "MMO Project",
        ]
    )
    return candidates


def _runtime_file(relative_path: Path) -> Path | None:
    for candidate in _mmo_project_candidates():
        path = candidate / relative_path
        if path.exists():
            return path
    return None


class T5NpcAuthoringDocumentationTests(unittest.TestCase):
    def test_t5_documents_exist_and_lock_reusable_definition_boundary(self) -> None:
        audit = (ROOT / "docs" / "T5_NPC_DOMAIN_AUDIT.md").read_text()
        plan = (ROOT / "docs" / "T5_NPC_AUTHORING_PLAN.md").read_text()
        acceptance = (ROOT / "docs" / "T5_NPC_ACCEPTANCE.md").read_text()

        for token in (
            "docs/development/CONTENT_AUTHORING_GUIDE.md",
            "## Existing Manual NPC-Authoring Workflow",
            "NpcRuntimeService.ResolveGeneratedNpcTexturePath",
            "Tiled continues to own spawn placement",
            "Content Studio owns reusable NPC identity",
            "DialogueSessionService",
            "DialogueDefinitionCatalog",
            "PlayerInteractionController",
            "StaticNpc",
            "WorldSnapshotNpcPayload",
        ):
            self.assertIn(token, audit)

        for token in (
            "Canonical linkage",
            "NpcSpawn.object_name",
            "NpcSpawn.npc_definition_id",
            "Published NPC definition",
            "npc_definitions",
            "GET /api/v1/npcs/options",
            "POST /api/v1/npcs/{npcDefinitionId}/preview",
            "default_interaction = 'talk'",
            "T5 Phase 4 - MMO Project runtime catalog handoff",
            "Quest-Foundation Handoff",
        ):
            self.assertIn(token, plan)

        for token in (
            "T5A - Audit And Domain Lock",
            "T5 Phase 1 - Schema And Contracts",
            "T5 Phase 4 - MMO Project Runtime Handoff",
            "No Content Studio ownership of spawn placement",
        ):
            self.assertIn(token, acceptance)

    def test_t5_docs_exclude_unsupported_runtime_scope(self) -> None:
        plan = (ROOT / "docs" / "T5_NPC_AUTHORING_PLAN.md").read_text()
        acceptance = (ROOT / "docs" / "T5_NPC_ACCEPTANCE.md").read_text()

        for token in (
            "map ID, region ID, chunk ID, spawn ID, source coordinates",
            "shops, banking, training services, quest starts",
            "NPC combat stats",
            "No dialogue graph editor.",
            "No quest authoring.",
            "No Content Studio ownership of spawn placement.",
        ):
            self.assertIn(token, plan + acceptance)

    def test_roadmap_readme_architecture_and_integration_index_t5a(self) -> None:
        roadmap = (ROOT / "docs" / "ROADMAP.md").read_text()
        readme = (ROOT / "README.md").read_text()
        architecture = (ROOT / "docs" / "ARCHITECTURE.md").read_text()
        integration = (ROOT / "integrations" / "mmo-project" / "README.md").read_text()

        for token in (
            "T5A runtime audit and domain lock documented",
            "T5_NPC_DOMAIN_AUDIT.md",
            "T5_NPC_AUTHORING_PLAN.md",
            "T5_NPC_ACCEPTANCE.md",
        ):
            self.assertIn(token, roadmap)

        self.assertIn("T5A audits the current MMO Project NPC runtime", readme)
        self.assertIn("## T5 NPC-definition boundary", architecture)
        self.assertIn("## T5 NPC-authoring runtime handoff", integration)
        self.assertIn("T5F runtime handoff hardening and reference safety implemented", integration)

    def test_t5d_adds_godot_editor_without_runtime_expansion(self) -> None:
        editor_path = ROOT / "content-studio" / "scripts" / "npc_editor.gd"
        self.assertTrue(editor_path.exists())
        editor = editor_path.read_text()

        for forbidden in (
            "export-npc-catalog",
            "dialogue graph editor",
            "quest_id",
            "service_script",
        ):
            self.assertNotIn(forbidden, editor.lower())

    def test_runtime_checkout_confirms_npc_catalog_handoff_when_available(self) -> None:
        guide_path = _runtime_file(Path("docs") / "development" / "CONTENT_AUTHORING_GUIDE.md")
        runtime_path = _runtime_file(
            Path("prototype")
            / "server"
            / "features"
            / "npcs"
            / "application"
            / "NpcRuntimeService.cs"
        )
        importer_path = _runtime_file(Path("prototype") / "importer" / "import_tiled_region.py")
        tiled_path = _runtime_file(
            Path("prototype")
            / "shared"
            / "maps"
            / "tiled"
            / "regions"
            / "starter_region.tmj"
        )
        npc_catalog_path = _runtime_file(Path("prototype") / "shared" / "maps" / "npcs" / "catalog.json")
        npc_migration_path = _runtime_file(Path("prototype") / "sql" / "024_npc_authoring_schema.sql")
        npc_seed_path = _runtime_file(Path("prototype") / "sql" / "025_seed_existing_npc_definitions.sql")
        dialogue_path = _runtime_file(Path("prototype") / "shared" / "dialogues" / "catalog.json")

        if None in (
            guide_path,
            runtime_path,
            importer_path,
            tiled_path,
            npc_catalog_path,
            npc_migration_path,
            npc_seed_path,
            dialogue_path,
        ):
            self.skipTest("MMO Project checkout is unavailable; runtime source check is skipped.")

        guide = guide_path.read_text()
        runtime = runtime_path.read_text()
        importer = importer_path.read_text()
        tiled = tiled_path.read_text()
        npc_catalog = npc_catalog_path.read_text()
        npc_migration = npc_migration_path.read_text()
        npc_seed = npc_seed_path.read_text()
        dialogue = dialogue_path.read_text()

        self.assertIn("## Adding a New NPC", guide)
        self.assertIn("ResolveNpcDefinition", runtime)
        self.assertNotIn("ResolveGeneratedNpcTexturePath", runtime)
        self.assertIn("NPC_DEFINITION_CATALOG_RELATIVE_PATH", importer)
        self.assertIn('object_type != NPC_SPAWN_CLASS', importer)
        self.assertIn('"NPC Spawns"', tiled)
        self.assertIn('"npc_definition_id"', tiled)
        self.assertIn('"definition_id": "test_npc"', npc_catalog)
        self.assertIn("CREATE TABLE IF NOT EXISTS npc_definitions", npc_migration)
        self.assertIn("'test_npc'", npc_seed)
        self.assertIn('"dialogue_id": "test_npc_greeting"', dialogue)


if __name__ == "__main__":
    unittest.main()
