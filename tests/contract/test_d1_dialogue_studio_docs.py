#!/usr/bin/env python3
"""Source contracts for the D1 Dialogue Studio audit and planning boundary."""

from __future__ import annotations

import os
import subprocess
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


def _runtime_root() -> Path | None:
    for candidate in _mmo_project_candidates():
        if (candidate / ".git").exists() and (
            candidate / "prototype" / "shared" / "dialogues" / "catalog.json"
        ).exists():
            return candidate
    return None


def _runtime_file(relative_path: Path) -> Path | None:
    root = _runtime_root()
    if root is None:
        return None
    path = root / relative_path
    return path if path.exists() else None


class D1DialogueStudioDocumentationTests(unittest.TestCase):
    def test_d1_documents_exist_and_capture_current_runtime_contract(self) -> None:
        audit = (ROOT / "docs" / "DIALOGUE_STUDIO_RUNTIME_AUDIT.md").read_text()
        domain = (ROOT / "docs" / "DIALOGUE_STUDIO_DOMAIN_MODEL.md").read_text()
        plan = (ROOT / "docs" / "DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md").read_text()
        acceptance = (ROOT / "docs" / "DIALOGUE_STUDIO_ACCEPTANCE.md").read_text()

        for token in (
            "prototype/shared/dialogues/catalog.json",
            "DialogueDefinitionCatalog",
            "DialogueSessionService",
            "NpcInteractionService",
            "DialogueDefinition",
            "DialogueEntryPoint",
            "DialogueNode",
            "DialogueChoiceDefinition",
            "speaker_text",
            "player_choice",
            "end",
            "test_npc_greeting",
            "test_npc",
            "future_flag",
            "dialogue_continue_request",
            "dialogue_choice_request",
            "dialogue_close_request",
            "dialogue_opened",
            "dialogue_node_presented",
            "dialogue_closed",
            "dialogue_command_failed",
            "Implemented production condition types:",
            "Implemented committed effect types:",
        ):
            self.assertIn(token, audit)

        for token in (
            "Dialogue Studio belongs inside MMO Content Studio as a first-class workspace.",
            "It is not a separate application",
            "Items\nMobs\nNPCs\nDialogue\nEnvironment",
            "DialogueDefinition",
            "dialogue_definition_id",
            "dialogue_entry_points",
            "dialogue_nodes",
            "dialogue_choices",
            "QV3 supports `quest_status`, `quest_step`, and `has_item` conditions",
        ):
            self.assertIn(token, domain)

        for token in (
            "D1 - Runtime Audit And Domain Lock",
            "D2 - Schema, Contracts, Repository, Validation, And API",
            "D3 - Godot Dialogue Studio",
            "D4 - MMO Project Runtime Catalog Handoff",
            "D5 - Hardening And Playthrough Verification",
            "/api/v1/dialogues",
        ):
            self.assertIn(token, plan + acceptance)

    def test_d1_updates_repository_indexes_and_workspace_boundary(self) -> None:
        readme = (ROOT / "README.md").read_text()
        roadmap = (ROOT / "docs" / "ROADMAP.md").read_text()
        architecture = (ROOT / "docs" / "ARCHITECTURE.md").read_text()
        workspace = (ROOT / "docs" / "GODOT_WORKSPACE_SUPPORT.md").read_text()
        integration = (ROOT / "integrations" / "mmo-project" / "README.md").read_text()

        for token in (
            "D1-D5 + QV3/QV4 — Dialogue Studio",
            "MMO Project quest foundations",
            "Dialogue Studio quest integration",
            "DIALOGUE_STUDIO_RUNTIME_AUDIT.md",
            "DIALOGUE_STUDIO_DOMAIN_MODEL.md",
            "DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md",
            "DIALOGUE_STUDIO_ACCEPTANCE.md",
        ):
            self.assertIn(token, readme)

        for token in (
            "## D - Dialogue Studio",
            "first-class workspace after NPCs and before Environment",
            "separate application",
            "D1-D5 author current runtime-compatible dialogue semantics",
            "not blockers\nfor D1-D5",
        ):
            self.assertIn(token, roadmap)

        self.assertIn("## D Dialogue Studio boundary", architecture)
        self.assertIn("dialogue graph/playthrough previews", architecture)
        self.assertIn("D3 Dialogue Studio uses the same support boundary", workspace)
        self.assertIn("## D Dialogue Studio runtime handoff plan", integration)

    def test_dialogue_schema_exposes_only_locked_qv3_qv4_semantics(self) -> None:
        domain = (ROOT / "docs" / "DIALOGUE_STUDIO_DOMAIN_MODEL.md").read_text()
        table_heading = "## Implemented Tables" if "## Implemented Tables" in domain else "## Proposed Tables"
        proposed_tables = domain.split(table_heading, 1)[1].split("## Identity Rules", 1)[0]

        forbidden_schema_tokens = (
            "quest_started",
            "quest_completed",
            "quest_stage",
            "quest_stage_equals",
            "objective_progress",
            "quest_rewards",
            "quest_variables",
            "quest_journal_data",
            "quest_specific_content_locks",
        )
        for token in forbidden_schema_tokens:
            self.assertNotIn(token, proposed_tables)

        self.assertIn("dialogue_entry_conditions", domain)
        self.assertIn("dialogue_choice_conditions", domain)
        self.assertIn("dialogue_choice_effects", domain)
        self.assertIn("quest_id", proposed_tables)
        self.assertIn("quest_status", proposed_tables)
        self.assertIn("quest_step", domain)
        self.assertIn("has_item", domain)
        for effect_type in (
            "start_quest",
            "advance_quest",
            "complete_quest",
            "grant_item",
            "remove_item",
            "grant_experience",
        ):
            self.assertIn(effect_type, domain)
        for forbidden in (
            "arbitrary scripts",
            "node-entry effects",
            "story flags",
            "objectives",
            "unlocks",
            "broader reward",
        ):
            self.assertIn(forbidden, domain)

    def test_godot_dialogue_editor_keeps_d1_runtime_boundary(self) -> None:
        main_scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        self.assertIn('[node name="Dialogue"', main_scene)
        self.assertIn("dialogue_editor.gd", main_scene)
        self.assertIn('const NODE_TYPE_SPEAKER_TEXT := "speaker_text"', editor)
        self.assertIn('const NODE_TYPE_PLAYER_CHOICE := "player_choice"', editor)
        self.assertIn('const NODE_TYPE_END := "end"', editor)
        self.assertIn('const CONDITION_TYPE_QUEST_STATUS := "quest_status"', editor)
        self.assertIn('const CONDITION_TYPE_QUEST_STEP := "quest_step"', editor)
        self.assertIn('const CONDITION_TYPE_HAS_ITEM := "has_item"', editor)
        self.assertNotIn("/api/v1/quests", editor)

    def test_runtime_checkout_confirms_current_dialogue_when_available(self) -> None:
        catalog_path = _runtime_file(Path("prototype") / "shared" / "dialogues" / "catalog.json")
        catalog_source_path = _runtime_file(
            Path("prototype")
            / "server"
            / "features"
            / "dialogue"
            / "application"
            / "DialogueDefinitionCatalog.cs"
        )
        session_path = _runtime_file(
            Path("prototype")
            / "server"
            / "features"
            / "dialogue"
            / "application"
            / "DialogueSessionService.cs"
        )
        npc_catalog_path = _runtime_file(Path("prototype") / "shared" / "maps" / "npcs" / "catalog.json")
        protocol_path = _runtime_file(Path("prototype") / "shared" / "protocol-v1.json")
        design_path = _runtime_file(
            Path("docs") / "design" / "OSRS_STYLE_NPC_CONVERSATIONS_AND_QUEST_GATES.md"
        )

        if None in (
            catalog_path,
            catalog_source_path,
            session_path,
            npc_catalog_path,
            protocol_path,
            design_path,
        ):
            self.skipTest("MMO Project checkout is unavailable; runtime source check is skipped.")

        catalog = catalog_path.read_text()
        catalog_source = catalog_source_path.read_text()
        session = session_path.read_text()
        npc_catalog = npc_catalog_path.read_text()
        protocol = protocol_path.read_text()
        design = design_path.read_text()

        for token in (
            '"dialogue_id": "test_npc_greeting"',
            '"node_type": "speaker_text"',
            '"node_type": "player_choice"',
            '"node_type": "end"',
            '"choice_id": "where_am_i"',
            '"choice_id": "goodbye"',
        ):
            self.assertIn(token, catalog)

        self.assertIn("DialogueCatalogDocument", catalog_source)
        self.assertIn("DialogueNodeTypes", catalog_source)
        self.assertIn("speaker_text", catalog_source)
        self.assertIn("player_choice", catalog_source)
        self.assertIn("end", catalog_source)
        self.assertIn("DialogueConditionTypes", catalog_source)
        self.assertIn("quest_status", catalog_source)
        self.assertIn("IDialogueConditionEvaluator", session)
        self.assertIn("BuildChoicePresentationsAsync", session)
        self.assertIn("EvaluateConditionSets", session)
        self.assertIn('"default_dialogue_id": "test_npc_greeting"', npc_catalog)
        self.assertIn("dialogue_continue_request", protocol)
        self.assertIn("dialogue_choice_request", protocol)
        self.assertIn("dialogue_close_request", protocol)
        self.assertIn("Dialogue Foundation V1 implemented", design)

    def test_runtime_checkout_remains_read_only_except_existing_parent_noise(self) -> None:
        root = _runtime_root()
        if root is None:
            self.skipTest("MMO Project checkout is unavailable; git status check is skipped.")

        result = subprocess.run(
            ["git", "status", "--short", "--untracked-files=no"],
            cwd=root,
            check=True,
            capture_output=True,
            text=True,
        )
        allowed = {
            "M tools/MMO-Content-Studio",
            "M tools/mmoproject.tiled-session",
            "M docs/development/CONTENT_AUTHORING_GUIDE.md",
            "M docs/design/DIALOGUE_FOUNDATION_V1.md",
            "M docs/modernization/CURRENT_HANDOFF.md",
            "M docs/modernization/DIALOGUE_QUEST_AND_CUTSCENE_ROADMAP.md",
            "M docs/design/OSRS_STYLE_NPC_CONVERSATIONS_AND_QUEST_GATES.md",
            "M prototype/importer/README.md",
            "M prototype/server/Program.cs",
            "M prototype/server/features/dialogue/application/DialogueChoiceEffectSettlementService.cs",
            "M prototype/server/features/dialogue/application/DialogueDefinitionCatalog.cs",
            "M prototype/server/features/dialogue/application/DialogueSessionService.cs",
            "M prototype/server/features/dialogue/host/DialogueChoiceEffectSettlementRecoveryWorker.cs",
            "M prototype/server/features/dialogue/host/DialogueCommandHandlers.cs",
            "M prototype/server/features/dialogue/persistence/DialogueChoiceEffectSettlementRepository.cs",
            "M prototype/server/features/inventory/persistence/CharacterInventoryRecord.cs",
            "M prototype/server/features/inventory/persistence/CharacterInventoryRepository.cs",
            "M prototype/server/features/quests/application/QuestDefinitionCatalog.cs",
            "M prototype/server/features/runtime/application/GameRuntimeEvent.cs",
            "M prototype/server/features/runtime/host/GameRuntimeEventProjector.cs",
            "M prototype/server/features/session/host/SessionHandshakeCoordinator.cs",
            "M prototype/shared/dialogues/catalog.json",
            "M prototype/sql/MODULE_OWNERSHIP.md",
            "M prototype/sql/027_seed_existing_dialogue_definitions.sql",
            "M prototype/sql/README.md",
            "M prototype/sql/043_quest_transition_evidence_lifecycle_delete.sql",
            "M prototype/tools/MapPublisher/Program.cs",
            "M prototype/tools/MapPublisher/DialogueCatalogExporter.cs",
            "M prototype/tests/MMO.Project.Prototype.MapPublisher.Tests/MapPublisher/DialogueCatalogExporterTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueDefinitionCatalogTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueChoiceEffectSettlementPathTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueEffectLifecycleLockContractTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueSessionServiceTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/GeneratedRegionRuntimeAdapterTests.cs",
            "M prototype/tests/MMO.Project.Prototype.Server.Tests/QuestStatePersistenceAcceptanceTests.cs",
            "m tools/MMO-Content-Studio"
        }
        unexpected = [line for line in result.stdout.splitlines() if line.strip() not in allowed]
        self.assertEqual([], unexpected)


if __name__ == "__main__":
    unittest.main()
