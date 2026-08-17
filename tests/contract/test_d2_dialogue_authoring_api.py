#!/usr/bin/env python3
"""Source contracts for the D2 dialogue host API boundary."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"


class D2DialogueAuthoringApiTests(unittest.TestCase):
    def test_contracts_repository_validator_and_services_exist(self) -> None:
        contracts = (HOST / "Contracts" / "DialogueContracts.cs").read_text()
        repository = (HOST / "Persistence" / "DialogueRepository.cs").read_text()
        validator = (HOST / "Services" / "DialogueDefinitionValidator.cs").read_text()
        analyzer = (HOST / "Services" / "DialogueGraphAnalyzer.cs").read_text()
        playthrough = (HOST / "Services" / "DialoguePlaythroughService.cs").read_text()
        service = (HOST / "Services" / "DialogueAuthoringService.cs").read_text()

        for token in (
            "DialogueDefinition",
            "DialogueDefinitionSummary",
            "DialogueCatalogResponse",
            "DialogueOptionsResponse",
            "DialogueEntryPoint",
            "DialogueNode",
            "DialogueChoice",
            "DialogueDraft",
            "PreviewDialogueRequest",
            "PreviewDialoguePlaythroughRequest",
            "DialogueMutationRequest",
            "DialogueDeleteRequest",
            "DialoguePreviewResponse",
            "DialoguePlaythroughResponse",
            "DialogueReferenceSummary",
            'JsonPropertyName("conditions")',
        ):
            self.assertIn(token, contracts)

        for token in (
            "public interface IDialogueRepository",
            "ListAsync",
            "LoadAsync",
            "LoadForUpdateAsync",
            "InsertDraftAsync",
            "ReplaceDraftAsync",
            "SetPublicationAsync",
            "DeleteAsync",
            "LoadNpcReferencesAsync",
            "BeginTransactionAsync",
            "for update",
            "DeleteChildrenAsync",
            "ReplaceChildrenAsync",
            "updated_at_utc = now()",
            "DialogueDefinitionConcurrencyException",
            "LoadQuestReferencesAsync",
            "LoadItemReferencesAsync",
            "LoadPublishedQuestConditionOptionsAsync",
            "LoadRuntimeItemConditionOptionsAsync",
            "dialogue_entry_conditions",
            "dialogue_choice_conditions",
        ):
            self.assertIn(token, repository)

        for token in (
            "DialogueDefinitionValidator",
            "ValidateNodeSemantics",
            "dialogue_unsupported_node_type",
            "dialogue_unsupported_condition",
            "dialogue_transition_target_missing",
            "dialogue_publish_blocked",
        ):
            self.assertIn(token, validator)

        self.assertIn("DialogueGraphAnalyzer", analyzer)
        self.assertIn("withoutTerminalPath", analyzer)
        self.assertIn("DialoguePlaythroughService", playthrough)
        self.assertIn("dialogue_playthrough_invalid_state", playthrough)
        self.assertIn("ComputePreviewSignature", service)
        self.assertIn("dialogue_preview_mismatch", service)
        self.assertIn("dialogue_unsaved_changes", service)

    def test_route_family_and_feature_registration_exist(self) -> None:
        feature = (HOST / "Features" / "Dialogues" / "DialogueAuthoringFeature.cs").read_text()
        aggregator = (HOST / "Features" / "AuthoringFeatureExtensions.cs").read_text()

        for token in (
            "AddSingleton<IDialogueRepository, DialogueRepository>()",
            "AddSingleton<DialogueDefinitionValidator>()",
            "AddSingleton<DialogueAuthoringService>()",
            "AddSingleton<IAuthoringSchemaRequirementProvider, DialogueSchemaRequirements>()",
            "AddSingleton<IAuthoringCatalogSectionProvider, DialogueCatalogSectionProvider>()",
            'MapGroup($"{AuthoringApi.RoutePrefix}/dialogues")',
            'MapGet("/options"',
            "MapGet(string.Empty",
            'MapGet("/{dialogueDefinitionId}"',
            'MapPost("/{dialogueDefinitionId}/preview"',
            'MapPost("/{dialogueDefinitionId}/playthrough"',
            'MapPut("/{dialogueDefinitionId}/draft"',
            'MapPost("/{dialogueDefinitionId}/publish"',
            'MapPost("/{dialogueDefinitionId}/disable"',
            'MapPost("/{dialogueDefinitionId}/delete"',
        ):
            self.assertIn(token, feature)

        self.assertIn("services.AddDialogueAuthoring();", aggregator)
        self.assertIn("endpoints.MapDialogueAuthoring();", aggregator)

    def test_options_schema_health_and_catalog_are_feature_owned(self) -> None:
        registry = (HOST / "Services" / "DialogueAuthoringRegistry.cs").read_text()
        schema = (HOST / "Features" / "Dialogues" / "DialogueSchemaRequirements.cs").read_text()
        catalog = (HOST / "Features" / "Dialogues" / "DialogueCatalogSectionProvider.cs").read_text()

        for token in (
            "supports_runtime_dialogue_catalog",
            "supports_conditions",
            "supports_effects",
            "supports_quest_conditions",
            "supports_quest_effects",
            "supports_localization",
            "supports_portraits",
            "supports_hot_reload",
        ):
            self.assertIn(token, registry + (HOST / "Contracts" / "DialogueContracts.cs").read_text())

        self.assertIn("LoadConditionTypes() => ConditionTypes", registry)
        self.assertIn("LoadEffectTypes() => EffectTypes", registry)
        self.assertIn('JsonPropertyName("quest_references")', (HOST / "Contracts" / "DialogueContracts.cs").read_text())
        self.assertIn('JsonPropertyName("item_references")', (HOST / "Contracts" / "DialogueContracts.cs").read_text())
        self.assertIn("quest_status", registry)
        self.assertIn("quest_step", registry)
        self.assertIn("has_item", registry)
        for effect_type in (
            "start_quest",
            "advance_quest",
            "complete_quest",
            "grant_item",
            "remove_item",
            "grant_experience",
        ):
            self.assertIn(effect_type, registry)
        self.assertIn("dialogue_definitions", schema)
        self.assertIn("dialogue_entry_points", schema)
        self.assertIn("dialogue_nodes", schema)
        self.assertIn("dialogue_choices", schema)
        self.assertIn("dialogue_entry_conditions", schema)
        self.assertIn("dialogue_choice_conditions", schema)
        self.assertIn("dialogue_entry_conditions_shape_check", schema)
        self.assertIn("dialogue_choice_conditions_shape_check", schema)
        self.assertIn("DialogueAuthoringService", catalog)
        self.assertIn("dialogues.ListAsync", catalog)

    def test_godot_dialogue_workspace_uses_d2_routes_without_quest_routes(self) -> None:
        self.assertTrue((ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").exists())
        main_scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()
        host_text = "\n".join(path.read_text() for path in (HOST).rglob("*.cs"))

        self.assertIn("dialogue_editor.gd", main_scene)
        self.assertIn('[node name="Dialogue"', main_scene)
        for route in (
            "/api/v1/dialogues/options",
            "/api/v1/dialogues%s",
            "/api/v1/dialogues/%s",
            "/api/v1/dialogues/%s/preview",
            "/api/v1/dialogues/%s/playthrough",
            "/api/v1/dialogues/%s/draft",
            "/api/v1/dialogues/%s/publish",
            "/api/v1/dialogues/%s/disable",
            "/api/v1/dialogues/%s/delete",
        ):
            self.assertIn(route, (ROOT / "content-studio" / "scripts" / "authoring_host_client.gd").read_text())
        self.assertNotIn("/api/v1/quests", editor)
        for token in (
            "start_quest",
            "advance_quest",
            "complete_quest",
            "grant_item",
            "remove_item",
            "grant_experience",
        ):
            self.assertIn(token, host_text)
        for token in (
            "quest_stage",
            "quest_rewards",
            "objective_progress",
            "arbitrary_script",
        ):
            self.assertNotIn(token, host_text)

    def test_documentation_marks_d2_complete_without_claiming_deferred_work(self) -> None:
        docs = "\n".join(
            [
                (ROOT / "README.md").read_text(),
                (ROOT / "docs" / "ROADMAP.md").read_text(),
                (ROOT / "docs" / "API_V1.md").read_text(),
                (ROOT / "docs" / "ARCHITECTURE.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_DOMAIN_MODEL.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_ACCEPTANCE.md").read_text(),
                (ROOT / "docs" / "SCHEMA_HEALTH_PROVIDERS.md").read_text(),
                (ROOT / "docs" / "FEATURE_CATALOG_PROVIDERS.md").read_text(),
                (ROOT / "integrations" / "mmo-project" / "README.md").read_text(),
            ]
        )

        for token in (
            "D3 Godot Dialogue Studio implemented",
            "026_dialogue_authoring_schema.sql",
            "/api/v1/dialogues",
            "supports_runtime_dialogue_catalog = true",
            "typed read-only condition",
            "locked QV4 effect registry",
            "Published NPC references block disable",
            "any NPC reference blocks delete",
            "Godot Dialogue Studio",
            "D4 MMO Project runtime catalog handoff implemented",
            "D1-D5 Dialogue Studio authoring",
            "QV3 typed read-only quest/item predicates",
            "QV4 typed choice effects",
        ):
            self.assertIn(token, docs)

        for forbidden in (
            "D4 complete implemented",
            "D5 complete implemented",
            "D5 hardening and playthrough verification remain pending",
            "Quest authoring implemented",
            "D4 MMO Project runtime catalog handoff remains pending",
        ):
            self.assertNotIn(forbidden, docs)


if __name__ == "__main__":
    unittest.main()
