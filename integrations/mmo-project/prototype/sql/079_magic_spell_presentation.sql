-- Optional authored spell presentation; existing gameplay facts remain unchanged.
BEGIN;
ALTER TABLE magic_combat_spells
    ADD COLUMN icon_texture_path TEXT NULL,
    ADD COLUMN projectile_animation_fps DOUBLE PRECISION NULL,
    ADD COLUMN projectile_render_scale DOUBLE PRECISION NULL,
    ADD COLUMN projectile_rotates_to_travel BOOLEAN NOT NULL DEFAULT TRUE,
    ADD COLUMN cast_sound_path TEXT NULL,
    ADD COLUMN impact_animation_fps DOUBLE PRECISION NULL,
    ADD COLUMN impact_render_scale DOUBLE PRECISION NULL,
    ADD COLUMN impact_sound_path TEXT NULL,
    ADD COLUMN splash_animation_fps DOUBLE PRECISION NULL,
    ADD COLUMN splash_render_scale DOUBLE PRECISION NULL,
    ADD COLUMN splash_sound_path TEXT NULL;
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_icon_texture_path_check CHECK (icon_texture_path IS NULL OR length(btrim(icon_texture_path)) > 0);
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_projectile_animation_fps_check CHECK (projectile_animation_fps IS NULL OR (projectile_animation_fps > 0 AND projectile_animation_fps < 'Infinity'::double precision));
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_projectile_render_scale_check CHECK (projectile_render_scale IS NULL OR (projectile_render_scale > 0 AND projectile_render_scale < 'Infinity'::double precision));
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_cast_sound_path_check CHECK (cast_sound_path IS NULL OR length(btrim(cast_sound_path)) > 0);
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_impact_animation_fps_check CHECK (impact_animation_fps IS NULL OR (impact_animation_fps > 0 AND impact_animation_fps < 'Infinity'::double precision));
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_impact_render_scale_check CHECK (impact_render_scale IS NULL OR (impact_render_scale > 0 AND impact_render_scale < 'Infinity'::double precision));
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_impact_sound_path_check CHECK (impact_sound_path IS NULL OR length(btrim(impact_sound_path)) > 0);
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_splash_animation_fps_check CHECK (splash_animation_fps IS NULL OR (splash_animation_fps > 0 AND splash_animation_fps < 'Infinity'::double precision));
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_splash_render_scale_check CHECK (splash_render_scale IS NULL OR (splash_render_scale > 0 AND splash_render_scale < 'Infinity'::double precision));
ALTER TABLE magic_combat_spells ADD CONSTRAINT magic_spells_splash_sound_path_check CHECK (splash_sound_path IS NULL OR length(btrim(splash_sound_path)) > 0);
CREATE TABLE magic_spell_presentation_frames (
    spell_id TEXT NOT NULL REFERENCES magic_combat_spells(spell_id) ON DELETE CASCADE,
    phase TEXT NOT NULL CHECK (phase IN ('projectile', 'impact', 'splash')),
    frame_order INTEGER NOT NULL CHECK (frame_order >= 0),
    texture_path TEXT NOT NULL CHECK (length(btrim(texture_path)) > 0),
    PRIMARY KEY (spell_id, phase, frame_order)
);
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('079_magic_spell_presentation.sql', 79, 'magic_spell_presentation',
    'Optional spell icon, ordered projectile/impact/splash frames and audio; no gameplay changes.');
COMMIT;
