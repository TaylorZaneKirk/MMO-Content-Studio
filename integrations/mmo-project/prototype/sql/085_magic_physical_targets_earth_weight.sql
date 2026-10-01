-- Schema only. Existing Published Air rows are transitioned through Studio after
-- applying 085. Then VALIDATE magic_spells_published_shape_check before closeout.
-- NOT VALID skips existing rows only; every subsequent write enforces the new shape.
BEGIN;
ALTER TABLE magic_combat_spells
    ADD COLUMN matter_physical_weight INTEGER NULL
        CONSTRAINT magic_spells_matter_physical_weight_check CHECK (matter_physical_weight IS NULL OR matter_physical_weight > 0),
    DROP CONSTRAINT magic_spells_target_mode_check,
    ADD CONSTRAINT magic_spells_target_mode_check CHECK (target_mode IN ('mob', 'tile', 'physical')),
    DROP CONSTRAINT magic_spells_matter_shape_check,
    ADD CONSTRAINT magic_spells_matter_shape_check CHECK (COALESCE(impact_effect = 'earth_matter', false) OR (manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL AND matter_physical_weight IS NULL)),
    DROP CONSTRAINT magic_spells_published_shape_check,
    ADD CONSTRAINT magic_spells_published_shape_check CHECK (publication_state <> 'Published' OR COALESCE(
        (cast_mode = 'selected_combat' AND target_mode = 'mob' AND impact_effect IS NULL AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL AND manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL)
        OR (cast_mode = 'explicit_technique' AND target_mode = 'physical' AND element = 'air' AND impact_effect = 'air_displacement' AND successful_hit_min_damage = 0 AND base_max_hit = 0 AND manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL)
        OR (cast_mode = 'explicit_technique' AND target_mode = 'tile' AND element = 'earth' AND impact_effect = 'earth_matter' AND successful_hit_min_damage = 0 AND base_max_hit = 0 AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL AND manifestation_base_success_percent IS NOT NULL AND manifestation_magic_levels_per_step IS NOT NULL AND manifestation_success_percent_per_step IS NOT NULL AND matter_lifetime_milliseconds IS NOT NULL AND matter_capacity_magic_levels_per_step IS NOT NULL AND matter_max_active IS NOT NULL AND matter_physical_weight IS NOT NULL AND matter_visual_texture_path IS NOT NULL AND matter_visual_render_scale IS NOT NULL), false)) NOT VALID;
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('085_magic_physical_targets_earth_weight.sql', 85, 'magic_physical_targets_earth_weight',
    'Physical Air targets and Earth weight; publish existing Air targets via Studio, then validate Published shape.');
COMMIT;
