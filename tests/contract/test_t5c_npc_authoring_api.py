#!/usr/bin/env python3
"""Source contracts for the T5C NPC repository, validation, and API boundary."""

from __future__ import annotations

import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"


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


class T5CNpcAuthoringApiTests(unittest.TestCase):
    def test_repository_validator_and_service_exist_with_locked_scope(self) -> None:
        repository = (HOST / "Persistence" / "NpcRepository.cs").read_text()
        validator = (HOST / "Services" / "NpcDefinitionValidator.cs").read_text()
        service = (HOST / "Services" / "NpcAuthoringService.cs").read_text()

        for token in (
            "public interface INpcRepository",
            "ListAsync",
            "LoadAsync",
            "LoadForUpdateAsync",
            "SaveDraftAsync",
            "SetPublicationAsync",
            "DeleteAsync",
            "LoadKnownSpawnReferencesAsync",
            "BeginTransactionAsync",
            "for update",
            "updated_at_utc = now()",
            "NpcDefinitionConcurrencyException",
            "NpcDefinitionDeleteRequiresDisabledException",
        ):
            self.assertIn(token, repository)

        for token in (
            "NpcDefinitionValidator",
            "ValidateAsync",
            "ValidateVisuals",
            "ValidateMovement",
            "ValidateInteractionAsync",
            "npc_visual_unresolved",
            "npc_dialogue_reference_invalid",
            "npc_dialogue_reference_validation_incomplete",
            "TryReadPngDimensions",
        ):
            self.assertIn(token, validator)

        for token in (
            "NpcAuthoringService",
            "LoadOptionsAsync",
            "PreviewAsync",
            "ComputePreviewSignature",
            "IsMatchingPreview",
            "EquivalentDraft",
            "ReloadVerificationFailure",
            "npc_preview_mismatch",
            "npc_version_conflict",
            "npc_reload_verification_failed",
        ):
            self.assertIn(token, service)

        forbidden = repository + validator + service
        for token in (
            "quest_id",
            "QuestDefinition",
            "ServiceScript",
            "shop_id",
            "bank",
            "trainer",
            "combat_profile",
            "spawn_id",
            "tile_x",
            "tile_y",
            "MapNpc",
        ):
            self.assertNotIn(token, forbidden)

    def test_feature_maps_complete_route_family_and_registers_dependencies(self) -> None:
        feature = (HOST / "Features" / "Npcs" / "NpcAuthoringFeature.cs").read_text()
        aggregator = (HOST / "Features" / "AuthoringFeatureExtensions.cs").read_text()

        for token in (
            "AddSingleton<INpcRepository, NpcRepository>()",
            "AddSingleton<NpcDefinitionValidator>()",
            "AddSingleton<NpcAuthoringService>()",
            "AddSingleton<NpcDialogueReferenceProvider>()",
            "MapNpcAuthoring",
            'MapGroup($"{AuthoringApi.RoutePrefix}/npcs")',
            'MapGet("/options"',
            "MapGet(string.Empty",
            'MapGet("/{npcDefinitionId}"',
            'MapPost("/{npcDefinitionId}/preview"',
            'MapPut("/{npcDefinitionId}/draft"',
            'MapPost("/{npcDefinitionId}/publish"',
            'MapPost("/{npcDefinitionId}/disable"',
            'MapPost("/{npcDefinitionId}/delete"',
            "PreviewNpcRequest",
            "SaveNpcDraftRequest",
            "NpcPublicationRequest",
            "NpcDeleteRequest",
        ):
            self.assertIn(token, feature)

        self.assertIn("endpoints.MapNpcAuthoring();", aggregator)

    def test_options_contract_exposes_capabilities_and_reference_summary(self) -> None:
        contracts = (HOST / "Contracts" / "NpcContracts.cs").read_text()
        registry = (HOST / "Services" / "NpcAuthoringRegistry.cs").read_text()
        dialogue_provider = (HOST / "Services" / "NpcDialogueReferenceProvider.cs").read_text()

        for token in (
            "NpcOperationCapabilities",
            'JsonPropertyName("supports_runtime_npc_catalog")',
            'JsonPropertyName("supports_complete_dialogue_reference_validation")',
            'JsonPropertyName("supports_multiple_interactions")',
            'JsonPropertyName("supports_quest_authoring")',
            "NpcReferenceSummary",
            'JsonPropertyName("known_reference_count")',
            'JsonPropertyName("reference_check_complete")',
            'JsonPropertyName("reference_summary")',
        ):
            self.assertIn(token, contracts)

        self.assertIn("MaxNotesLength", registry)
        self.assertIn("catalog.json", dialogue_provider)
        self.assertIn("dialogue_id", dialogue_provider)

    def test_catalog_provider_is_repository_backed(self) -> None:
        catalog = (HOST / "Features" / "Npcs" / "NpcCatalogSectionProvider.cs").read_text()

        self.assertIn("NpcAuthoringService", catalog)
        self.assertIn("npcs.ListAsync", catalog)
        self.assertIn("npc.NpcDefinitionId", catalog)
        self.assertIn("true", catalog)
        self.assertNotIn("false", catalog)

    def test_compiled_tests_cover_t5c_behavior(self) -> None:
        validator_tests = (ROOT / "tests" / "host" / "MMO.ContentStudio.AuthoringHost.Tests" / "NpcDefinitionValidatorTests.cs").read_text()
        service_tests = (ROOT / "tests" / "host" / "MMO.ContentStudio.AuthoringHost.Tests" / "NpcAuthoringServiceTests.cs").read_text()

        for token in (
            "ValidPublishAcceptsKnownDialogueAndResolvedVisual",
            "MissingVisualIsDraftWarningAndPublicationError",
            "DimensionMismatchBlocksPublication",
            "UnknownDialogueReferenceIsRejectedWhenCatalogIsComplete",
            "DialogueValidationFallsBackToSyntaxOnlyWhenCatalogIsMissing",
        ):
            self.assertIn(token, validator_tests)

        for token in (
            "NewSaveAndExistingSaveAdvanceRootTimestamp",
            "StaleSaveAndSignatureMismatchFail",
            "PublishUsesSavedAggregateAndRejectsUnsavedPreviewChanges",
            "DisableAndDeleteAreBlockedByKnownReferences",
            "DeleteRequiresDisabledAndThenRemovesAggregate",
            "ReloadVerificationFailureReturnsStructuredError",
        ):
            self.assertIn(token, service_tests)

    def test_docs_mark_t5c_without_claiming_godot_or_runtime_handoff(self) -> None:
        docs = "\n".join(
            [
                (ROOT / "README.md").read_text(),
                (ROOT / "docs" / "ROADMAP.md").read_text(),
                (ROOT / "docs" / "ARCHITECTURE.md").read_text(),
                (ROOT / "docs" / "API_V1.md").read_text(),
                (ROOT / "docs" / "T5_NPC_AUTHORING_PLAN.md").read_text(),
                (ROOT / "docs" / "T5_NPC_ACCEPTANCE.md").read_text(),
                (ROOT / "integrations" / "mmo-project" / "README.md").read_text(),
                (ROOT / "docs" / "FEATURE_CATALOG_PROVIDERS.md").read_text(),
            ]
        )

        for token in (
            "T5C NPC repository, validation, and API implemented; Godot workspace, runtime handoff, and verification remain pending",
            "reference_check_complete = false",
            "supports_runtime_npc_catalog = false",
            "supports_quest_authoring = false",
            "No Godot NPC workspace is implemented yet",
        ):
            self.assertIn(token, docs)

        for forbidden in (
            "Godot NPC workspace implemented",
            "runtime NPC catalog export implemented",
            "Quest authoring implemented",
        ):
            self.assertNotIn(forbidden, docs)

    def test_no_godot_npc_editor_or_mmo_project_changes(self) -> None:
        self.assertFalse((ROOT / "content-studio" / "scripts" / "npc_editor.gd").exists())

        runtime_root = _runtime_file(Path("prototype") / "server" / "Program.cs")
        if runtime_root is None:
            self.skipTest("MMO Project checkout is unavailable; git status check is skipped.")

        project = runtime_root.parents[2]
        result = subprocess.run(
            ["git", "status", "--short"],
            cwd=project,
            check=True,
            text=True,
            capture_output=True,
        )
        unexpected = [
            line
            for line in result.stdout.splitlines()
            if not line.endswith("tools/MMO-Content-Studio")
        ]
        self.assertEqual([], unexpected)


if __name__ == "__main__":
    unittest.main()
