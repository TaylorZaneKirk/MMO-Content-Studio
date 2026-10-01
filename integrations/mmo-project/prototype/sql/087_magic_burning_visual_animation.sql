-- Ordered looping persistent Fire frames replace the single static Fire texture.
BEGIN;
CREATE FUNCTION magic_spell_burning_visual_frames_valid(frames TEXT[]) RETURNS BOOLEAN
LANGUAGE SQL IMMUTABLE AS $$
    SELECT COALESCE(bool_and(
        path ~ '^res://assets/[^:]+\.png$' AND
        substring(path from 14) !~ '(^|/)(\.\.?|)(/|$)' AND
        position(chr(92) in path) = 0), TRUE)
    FROM unnest(frames) AS fire_frame(path);
$$;
ALTER TABLE magic_combat_spells
    DROP CONSTRAINT magic_spells_burning_shape_check,
    DROP CONSTRAINT magic_spells_published_shape_check,
    DROP CONSTRAINT magic_spells_burning_visual_texture_path_check,
    ADD COLUMN burning_visual_frames TEXT[] NULL,
    ADD COLUMN burning_visual_animation_fps DOUBLE PRECISION NULL
        CONSTRAINT magic_spells_burning_visual_animation_fps_check
        CHECK (burning_visual_animation_fps IS NULL OR
            (burning_visual_animation_fps > 0 AND burning_visual_animation_fps < 'Infinity'::double precision));
UPDATE magic_combat_spells
SET burning_visual_frames = ARRAY[burning_visual_texture_path]
WHERE burning_visual_texture_path IS NOT NULL;
ALTER TABLE magic_combat_spells
    DROP COLUMN burning_visual_texture_path,
    ADD CONSTRAINT magic_spells_burning_visual_frames_check
        CHECK (burning_visual_frames IS NULL OR
            (cardinality(burning_visual_frames) <= 64 AND array_position(burning_visual_frames, NULL) IS NULL AND
                magic_spell_burning_visual_frames_valid(burning_visual_frames))),
    ADD CONSTRAINT magic_spells_burning_shape_check CHECK (COALESCE(impact_effect = 'burning_terrain', false) OR (ignition_base_success_percent IS NULL AND ignition_magic_levels_per_step IS NULL AND ignition_success_percent_per_step IS NULL AND burning_lifetime_milliseconds IS NULL AND burning_capacity_magic_levels_per_step IS NULL AND burning_max_active IS NULL AND burning_min_damage IS NULL AND burning_max_damage IS NULL AND burning_hazard_cooldown_milliseconds IS NULL AND COALESCE(cardinality(burning_visual_frames), 0) = 0 AND burning_visual_animation_fps IS NULL AND burning_visual_render_scale IS NULL)),
    ADD CONSTRAINT magic_spells_published_shape_check CHECK (publication_state <> 'Published' OR COALESCE(
        (cast_mode = 'selected_combat' AND target_mode = 'mob' AND impact_effect IS NULL AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL AND manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL)
        OR (cast_mode = 'explicit_technique' AND target_mode = 'physical' AND element = 'air' AND impact_effect = 'air_displacement' AND successful_hit_min_damage = 0 AND base_max_hit = 0 AND manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL)
        OR (cast_mode = 'explicit_technique' AND target_mode = 'tile' AND element = 'earth' AND impact_effect = 'earth_matter' AND successful_hit_min_damage = 0 AND base_max_hit = 0 AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL AND manifestation_base_success_percent IS NOT NULL AND manifestation_magic_levels_per_step IS NOT NULL AND manifestation_success_percent_per_step IS NOT NULL AND matter_lifetime_milliseconds IS NOT NULL AND matter_capacity_magic_levels_per_step IS NOT NULL AND matter_max_active IS NOT NULL AND matter_physical_weight IS NOT NULL AND matter_visual_texture_path IS NOT NULL AND matter_visual_render_scale IS NOT NULL)
        OR (cast_mode = 'explicit_technique' AND target_mode = 'tile' AND element = 'fire' AND impact_effect = 'burning_terrain' AND successful_hit_min_damage = 0 AND base_max_hit = 0 AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL AND force_mastery_magic_levels_per_step IS NULL AND force_mastery_force_per_step IS NULL AND force_mastery_max_force IS NULL AND displacement_mastery_magic_levels_per_step IS NULL AND displacement_mastery_tiles_per_step IS NULL AND displacement_mastery_max_tiles IS NULL AND manifestation_base_success_percent IS NULL AND manifestation_magic_levels_per_step IS NULL AND manifestation_success_percent_per_step IS NULL AND matter_lifetime_milliseconds IS NULL AND matter_capacity_magic_levels_per_step IS NULL AND matter_max_active IS NULL AND matter_physical_weight IS NULL AND matter_visual_texture_path IS NULL AND matter_visual_render_scale IS NULL AND ignition_base_success_percent IS NOT NULL AND ignition_magic_levels_per_step IS NOT NULL AND ignition_success_percent_per_step IS NOT NULL AND burning_lifetime_milliseconds IS NOT NULL AND burning_capacity_magic_levels_per_step IS NOT NULL AND burning_max_active IS NOT NULL AND burning_min_damage IS NOT NULL AND burning_max_damage IS NOT NULL AND burning_hazard_cooldown_milliseconds IS NOT NULL AND cardinality(burning_visual_frames) >= 2 AND burning_visual_animation_fps IS NOT NULL AND burning_visual_render_scale IS NOT NULL), false));
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('087_magic_burning_visual_animation.sql', 87, 'magic_burning_visual_animation',
    'Ordered looping persistent Fire frames and authored FPS replace the static Fire texture.');
COMMIT;
