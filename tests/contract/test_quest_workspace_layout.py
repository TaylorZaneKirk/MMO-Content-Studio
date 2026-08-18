"""Source contracts for the Godot Quest workspace."""

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

    def test_transition_formatting_normalizes_nullable_step_endpoints(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "quest_editor.gd").read_text()
        formatter = editor.split("func _format_transitions(", 1)[1].split("func _selected_operation(", 1)[0]
        parser = editor.split("func _optional(", 1)[1].split("func _format_optional(", 1)[0]

        self.assertIn('func _format_optional(value: Variant) -> String:', editor)
        self.assertIn('_format_optional(item.get("source_step_id", null))', formatter)
        self.assertIn('_format_optional(item.get("target_step_id", null))', formatter)
        self.assertIn('return "" if value == null else str(value)', editor)
        self.assertIn("return null if text.is_empty() else text", parser)
        self.assertNotIn('"<null>"', editor)


if __name__ == "__main__":
    unittest.main()
