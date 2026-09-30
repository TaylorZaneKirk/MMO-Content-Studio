-- Temporary Earth matter authoring only; no spell or asset is seeded.
BEGIN;
ALTER TABLE magic_combat_spells
    ADD COLUMN target_mode TEXT NOT NULL DEFAULT 'mob'
        CONSTRAINT magic_spells_target_mode_check CHECK (target_mode IN ('mob', 'tile')),
    ADD COLUMN manifestation_base_success_percent INTEGER NULL
        CONSTRAINT magic_spells_manifestation_base_success_percent_check CHECK (manifestation_base_success_percent IS NULL OR (manifestation_base_success_percent BETWEEN 0 AND 100)),
    ADD COLUMN manifestation_magic_levels_per_step INTEGER NULL
        CONSTRAINT magic_spells_manifestation_magic_levels_per_step_check CHECK (manifestation_magic_levels_per_step IS NULL OR (manifestation_magic_levels_per_step > 0)),
    ADD COLUMN manifestation_success_percent_per_step INTEGER NULL
        CONSTRAINT magic_spells_manifestation_success_percent_per_step_check CHECK (manifestation_success_percent_per_step IS NULL OR (manifestation_success_percent_per_step BETWEEN 0 AND 100)),
    ADD COLUMN matter_lifetime_milliseconds INTEGER NULL
        CONSTRAINT magic_spells_matter_lifetime_milliseconds_check CHECK (matter_lifetime_milliseconds IS NULL OR (matter_lifetime_milliseconds > 0)),
    ADD COLUMN matter_capacity_magic_levels_per_step INTEGER NULL
        CONSTRAINT magic_spells_matter_capacity_magic_levels_per_step_check CHECK (matter_capacity_magic_levels_per_step IS NULL OR (matter_capacity_magic_levels_per_step > 0)),
    ADD COLUMN matter_max_active INTEGER NULL
        CONSTRAINT magic_spells_matter_max_active_check CHECK (matter_max_active IS NULL OR (matter_max_active > 0)),
    ADD COLUMN matter_visual_texture_path TEXT NULL
        CONSTRAINT magic_spells_matter_visual_texture_path_check CHECK (matter_visual_texture_path IS NULL OR (matter_visual_texture_path ~ '^res://assets/[^:]+\.png$' AND substring(matter_visual_texture_path from 14) !~ '(^|/)(\.\.?|)(/|$)' AND position(chr(92) in matter_visual_texture_path) = 0)),
    ADD COLUMN matter_visual_render_scale DOUBLE PRECISION NULL
        CONSTRAINT magic_spells_matter_visual_render_scale_check CHECK (matter_visual_render_scale IS NULL OR (matter_visual_render_scale > 0 AND matter_visual_render_scale < 'Infinity'::double precision)),
    DROP CONSTRAINT magic_spells_impact_effect_check,
    ADD CONSTRAINT magic_spells_impact_effect_check CHECK (impact_effect IS NULL OR impact_effect IN ('air_displacement', 'earth_matter')),
    DROP CONSTRAINT magic_spells_force_shape_check,
    ADD CONSTRAINT magic_spells_force_shape_check CHECK (
        (impact_effect IS NOT NULL AND impact_effect = 'air_displacement' AND force IS NOT NULL AND force_falloff_per_tile IS NOT NULL AND max_displacement_tiles IS NOT NULL)
        OR ((impact_effect IS NULL OR impact_effect = 'earth_matter') AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL)),
    ADD CONSTRAINT magic_spells_matter_shape_check CHECK (COALESCE(impact_effect = 'earth_matter', false) OR (manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL)),
    ADD CONSTRAINT magic_spells_published_shape_check CHECK (publication_state <> 'Published' OR COALESCE(
        (cast_mode = 'selected_combat' AND target_mode = 'mob' AND impact_effect IS NULL AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL AND manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL)
        OR (cast_mode = 'explicit_technique' AND target_mode = 'mob' AND element = 'air' AND impact_effect = 'air_displacement' AND successful_hit_min_damage = 0 AND base_max_hit = 0 AND manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL)
        OR (cast_mode = 'explicit_technique' AND target_mode = 'tile' AND element = 'earth' AND impact_effect = 'earth_matter' AND successful_hit_min_damage = 0 AND base_max_hit = 0 AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL AND manifestation_base_success_percent IS NOT NULL AND manifestation_magic_levels_per_step IS NOT NULL AND manifestation_success_percent_per_step IS NOT NULL AND matter_lifetime_milliseconds IS NOT NULL AND matter_capacity_magic_levels_per_step IS NOT NULL AND matter_max_active IS NOT NULL AND matter_visual_texture_path IS NOT NULL AND matter_visual_render_scale IS NOT NULL), false));
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('083_magic_raise_boulder_earth_matter.sql', 83, 'magic_raise_boulder_earth_matter',
    'Authored tile techniques, manifestation, capacity and persistent Earth matter visuals.');
COMMIT;
