from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class MobCombatLevelPreviewTests(unittest.TestCase):
    def test_host_exposes_read_only_derived_mob_combat_level(self):
        contracts = (ROOT / "host" / "Contracts" / "MobContracts.cs").read_text()
        domain_rules = (ROOT / "host" / "Services" / "MobDomainRules.cs").read_text()
        service = (ROOT / "host" / "Services" / "MobAuthoringService.cs").read_text()

        self.assertIn('JsonPropertyName("derived_combat_level")', contracts)
        self.assertIn('JsonPropertyName("combat_level_diagnostics")', contracts)
        self.assertIn("CalculateDerivedCombatLevel", domain_rules)
        self.assertIn("CalculateCombatLevelDiagnostics", domain_rules)
        self.assertIn("CalculateEquivalentLevel", domain_rules)
        self.assertIn("10 * (defenceLevel + maxHealth) + 13 * (attackLevel + strengthLevel)", domain_rules)
        self.assertIn("CalculateDerivedCombatLevel(effective)", service)
        self.assertIn("CalculateCombatLevelDiagnostics(effective)", service)

    def test_godot_mob_editor_derives_combat_level_without_authoring_payload_field(self):
        editor = (ROOT / "content-studio" / "scripts" / "mob_editor.gd").read_text()

        self.assertIn('"Derived combat level"', editor)
        self.assertIn('"Innate-bonus diagnostics"', editor)
        self.assertIn("_update_derived_combat_level", editor)
        self.assertIn("_set_combat_level_diagnostics", editor)
        self.assertIn("_local_combat_level_diagnostics", editor)
        self.assertIn("read-only; derived from authored combat stats and health", editor)
        self.assertIn("diagnostics do not alter displayed combat level", editor)
        self.assertNotIn(
            '"derived_combat_level"',
            editor.split('func _payload() -> Dictionary:', 1)[1].split('func _combat_profile_payload()', 1)[0])
        self.assertNotIn(
            '"combat_level_diagnostics"',
            editor.split('func _payload() -> Dictionary:', 1)[1].split('func _combat_profile_payload()', 1)[0])


if __name__ == "__main__":
    unittest.main()
