-- Source-only additive authoring extension. Apply once during an approved release.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_file='109_cooking_recipes_and_servings.sql') OR EXISTS(SELECT 1 FROM schema_migrations WHERE migration_file='110_food_preparation.sql')
 THEN RAISE EXCEPTION 'Migration 110 requires 109 and must be applied once'; END IF;
END $$;
CREATE OR REPLACE FUNCTION validate_cooking_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE r jsonb; ingredient jsonb; item text; seen text[] := '{}';
BEGIN
 r := NEW.published_recipe;
 IF r IS NULL THEN RETURN NEW; END IF;
 IF (jsonb_typeof(r->'ingredients')='array' AND (jsonb_array_length(r->'ingredients')>0 OR r->>'operation'='pick_wheat')
     AND (r->>'output_quantity')::integer>0 AND (r->>'required_cooking_level')::integer BETWEEN 1 AND 99
     AND (r->>'xp')::integer>=0 AND (r->>'success_low')::integer BETWEEN 0 AND 256
     AND (r->>'success_high')::integer BETWEEN (r->>'success_low')::integer AND 256) IS NOT TRUE
 THEN RAISE EXCEPTION 'Invalid published Cooking recipe %',NEW.recipe_id; END IF;
 IF coalesce(r->>'operation','cook') NOT IN ('cook','pick_wheat','mill','mix','shape','fill')
 OR coalesce((r->>'duration_ms')::integer,2400) NOT BETWEEN 600 AND 60000
 OR coalesce((r->>'duration_ms')::integer,2400)%600<>0
 OR (coalesce(r->>'operation','cook')='cook' AND (coalesce((r->>'duration_ms')::integer,2400)<>2400 OR (r->>'xp')::integer<=0 OR jsonb_array_length(coalesce(nullif(r->'additional_outputs','null'::jsonb),'[]'))>0))
 OR (coalesce(r->>'operation','cook')<>'cook' AND ((r->>'xp')::integer<>0 OR (r->>'required_cooking_level')::integer<>1 OR (r->>'success_low')::integer<>256 OR (r->>'success_high')::integer<>256 OR r->>'burnt_output_item_id' IS NOT NULL))
 OR (r->>'operation'='pick_wheat' AND jsonb_array_length(r->'ingredients')<>0)
 OR (r->>'operation' IN ('mix','shape') AND jsonb_array_length(r->'ingredients')<>2)
 THEN RAISE EXCEPTION 'Invalid preparation operation/timing'; END IF;
 FOR ingredient IN SELECT value FROM jsonb_array_elements(r->'ingredients') LOOP
  item := ingredient->>'item_id';
  IF item IS NULL OR item=ANY(seen) OR ((ingredient->>'quantity')::integer>0) IS NOT TRUE
  THEN RAISE EXCEPTION 'Invalid/duplicate Cooking ingredient'; END IF;
  seen := array_append(seen,item);
 END LOOP;
 seen := array_append(seen,r->>'output_item_id');
 FOR ingredient IN SELECT value FROM jsonb_array_elements(coalesce(nullif(r->'additional_outputs','null'::jsonb),'[]')) LOOP
  item := ingredient->>'item_id';
  IF item IS NULL OR ((ingredient->>'quantity')::integer>0) IS NOT TRUE OR item=r->>'output_item_id'
  OR (SELECT count(*) FROM jsonb_array_elements(r->'additional_outputs') o WHERE o->>'item_id'=item)<>1
  THEN RAISE EXCEPTION 'Invalid additional output'; END IF;
  seen := array_append(seen,item);
 END LOOP;
 IF r->>'burnt_output_item_id' IS NOT NULL THEN seen:=array_append(seen,r->>'burnt_output_item_id'); END IF;
 FOREACH item IN ARRAY seen LOOP
  PERFORM 1 FROM item_definitions WHERE item_id=item AND runtime_enabled FOR SHARE;
  IF NOT FOUND THEN RAISE EXCEPTION 'Unpublished Cooking item %',item; END IF;
 END LOOP;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION guard_cooking_item() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (TG_OP='DELETE' OR (OLD.runtime_enabled AND NOT NEW.runtime_enabled)) AND EXISTS(
   SELECT 1 FROM cooking_recipes WHERE published_recipe IS NOT NULL AND
   (EXISTS(SELECT 1 FROM jsonb_array_elements(coalesce(nullif(published_recipe->'additional_outputs','null'::jsonb),'[]')) o WHERE o->>'item_id'=OLD.item_id) OR published_recipe->>'output_item_id'=OLD.item_id OR published_recipe->>'burnt_output_item_id'=OLD.item_id OR
    EXISTS(SELECT 1 FROM jsonb_array_elements(published_recipe->'ingredients') i WHERE i->>'item_id'=OLD.item_id)))
 THEN RAISE EXCEPTION 'Disable dependent Cooking recipes before unpublishing item %',OLD.item_id; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END $$;

ALTER TABLE cooking_recipes ADD CONSTRAINT cooking_preparation_shape CHECK(published_recipe IS NULL OR coalesce(published_recipe->>'operation','cook') IN ('cook','pick_wheat','mill','mix','shape','fill'));
CREATE UNIQUE INDEX cooking_one_preparation_operation ON cooking_recipes ((published_recipe->>'operation'))
 WHERE published_recipe IS NOT NULL AND published_recipe->>'operation'<>'cook';
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('110_food_preparation.sql',110,'food_preparation','Guaranteed zero-XP preparation and atomic container returns; no player writes.');
COMMIT;
