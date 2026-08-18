"""Source contracts for the Godot Quest workspace layout."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class QuestWorkspaceLayoutTests(unittest.TestCase):
    def test_definition_grid_and_line_fields_expand_horizontally(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "quest_editor.gd").read_text()
        build_definition = editor.split('var grid := GridContainer.new()', 1)[1].split('_heading(editor, "Steps"', 1)[0]
        line_helper = editor.split("func _line(", 1)[1].split("func _value(", 1)[0]

        self.assertIn("grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL", build_definition)
        self.assertIn("edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL", line_helper)
        self.assertNotIn("edit.custom_minimum_size", line_helper)


if __name__ == "__main__":
    unittest.main()
