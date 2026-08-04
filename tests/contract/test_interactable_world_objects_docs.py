#!/usr/bin/env python3
"""Source contracts for future interactable world-object planning docs."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class InteractableWorldObjectsDocumentationTests(unittest.TestCase):
    def test_design_document_locks_ownership_boundary(self) -> None:
        design = (ROOT / "docs" / "INTERACTABLE_WORLD_OBJECTS_DESIGN.md").read_text()

        self.assertIn(
            "Interactable world objects are reusable definitions with typed capabilities.",
            design,
        )
        self.assertIn(
            "Tiled owns placed instances and map-specific links.",
            design,
        )
        self.assertIn(
            "The authoritative runtime owns interaction execution and mutable state.",
            design,
        )
        for token in (
            "WorldObjectDefinition",
            "Tiled WorldObjectSpawn",
            "Runtime WorldObjectInstance",
            "Content Studio authors reusable definitions.",
            "Tiled authors placement.",
            "MMO Project composes both into authoritative runtime instances.",
        ):
            self.assertIn(token, design)

    def test_design_document_covers_capabilities_and_state_scope(self) -> None:
        design = (ROOT / "docs" / "INTERACTABLE_WORLD_OBJECTS_DESIGN.md").read_text()

        for token in (
            "Toggle And Linked Mechanisms",
            "Quest Interactions",
            "Gathering Resource Nodes",
            "Processing Stations",
            "Traps And Challenge Objects",
            "Containers And Searchable Objects",
            "`state_toggle`",
            "`linked_mechanism`",
            "`quest_interaction`",
            "`resource_gathering`",
            "`processing_station`",
            "`trap_disarm`",
            "`container_search`",
            "per-player",
            "globally shared",
            "temporarily shared",
            "party/instance scoped",
            "simultaneous interaction attempts",
            "revisioned state publication",
            "linked-object atomicity",
        ):
            self.assertIn(token, design)

    def test_design_document_records_integrations_and_deferred_decisions(self) -> None:
        design = (ROOT / "docs" / "INTERACTABLE_WORLD_OBJECTS_DESIGN.md").read_text()

        for token in (
            "T3B Tools",
            "Skills And Discipline/Mastery",
            "Inventory And Items",
            "Quests And Dialogue",
            "Static World-Object Rendering",
            "Runtime Activity Arbitration",
            "MMO Project static-content importer",
            "Tiled object conventions",
            "final database tables",
            "final API contracts",
            "arbitrary scripting",
            "hot reload behavior",
            "exact persistence policy",
        ):
            self.assertIn(token, design)

    def test_roadmap_places_dialogue_then_t6_and_t7_after_t5(self) -> None:
        roadmap = (ROOT / "docs" / "ROADMAP.md").read_text()
        t5_index = roadmap.index("## T5")
        dialogue_index = roadmap.index("## D - Dialogue Studio")
        t6_index = roadmap.index("## T6")
        t7_index = roadmap.index("## T7")
        later_index = roadmap.index("## Later Workspaces")

        self.assertLess(t5_index, dialogue_index)
        self.assertLess(dialogue_index, t6_index)
        self.assertLess(t6_index, t7_index)
        self.assertLess(t7_index, later_index)
        for token in (
            "Interactable World Objects Foundation",
            "Gathering Resources and Processing Stations",
            "without custom executable scripting",
            "without duplicating recipes or hard-coding specific tools",
            "proceeds before T6/T7",
            "they are not blockers\nfor D1-D5",
            "INTERACTABLE_WORLD_OBJECTS_DESIGN.md",
        ):
            self.assertIn(token, roadmap)

    def test_readme_links_planned_world_object_design_without_claiming_implementation(self) -> None:
        readme = (ROOT / "README.md").read_text()

        self.assertIn(
            "Future planned work includes reusable interactable world-object authoring",
            readme,
        )
        self.assertIn(
            "docs/INTERACTABLE_WORLD_OBJECTS_DESIGN.md",
            readme,
        )
        self.assertIn(
            "**T6 \u2014 Interactable world objects foundation**",
            readme,
        )
        self.assertIn(
            "**T7 \u2014 Gathering resources and processing stations**",
            readme,
        )


if __name__ == "__main__":
    unittest.main()
