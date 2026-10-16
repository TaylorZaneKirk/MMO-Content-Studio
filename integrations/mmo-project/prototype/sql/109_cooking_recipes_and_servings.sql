-- Additive Cooking authoring and explicit Empty consumables. Schema only; no player/content writes.
CREATE TABLE cooking_recipes (
 recipe_id text PRIMARY KEY CHECK(recipe_id ~ '^[a-z][a-z0-9_]*$'),
 draft jsonb NOT NULL,
 published_recipe jsonb,
 publication_state text NOT NULL DEFAULT 'Draft' CHECK(publication_state IN ('Draft','Published','Disabled')),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 CONSTRAINT cooking_recipe_identity CHECK(draft->>'recipe_id'=recipe_id AND (published_recipe IS NULL OR published_recipe->>'recipe_id'=recipe_id)),
 CONSTRAINT cooking_publication_shape CHECK((publication_state <> 'Published' OR published_recipe IS NOT NULL) AND (publication_state <> 'Disabled' OR published_recipe IS NULL))
);

-- Publication validates every authored ingredient/output. Retained published
-- snapshots remain protected while a newer draft is edited.
CREATE FUNCTION validate_cooking_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE r jsonb; ingredient jsonb; item text; seen text[] := '{}';
BEGIN
 r := NEW.published_recipe;
 IF r IS NULL THEN RETURN NEW; END IF;
 IF (jsonb_typeof(r->'ingredients')='array' AND jsonb_array_length(r->'ingredients')>0
     AND (r->>'output_quantity')::integer>0 AND (r->>'required_cooking_level')::integer BETWEEN 1 AND 99
     AND (r->>'xp')::integer>0 AND (r->>'success_low')::integer BETWEEN 0 AND 256
     AND (r->>'success_high')::integer BETWEEN (r->>'success_low')::integer AND 256) IS NOT TRUE
 THEN RAISE EXCEPTION 'Invalid published Cooking recipe %',NEW.recipe_id; END IF;
 FOR ingredient IN SELECT value FROM jsonb_array_elements(r->'ingredients') LOOP
  item := ingredient->>'item_id';
  IF item IS NULL OR item=ANY(seen) OR ((ingredient->>'quantity')::integer>0) IS NOT TRUE
  THEN RAISE EXCEPTION 'Invalid/duplicate Cooking ingredient'; END IF;
  seen := array_append(seen,item);
 END LOOP;
 seen := array_append(seen,r->>'output_item_id');
 IF r->>'burnt_output_item_id' IS NOT NULL THEN seen:=array_append(seen,r->>'burnt_output_item_id'); END IF;
 FOREACH item IN ARRAY seen LOOP
  PERFORM 1 FROM item_definitions WHERE item_id=item AND runtime_enabled FOR SHARE;
  IF NOT FOUND THEN RAISE EXCEPTION 'Unpublished Cooking item %',item; END IF;
 END LOOP;
 RETURN NEW;
END $$;
CREATE TRIGGER cooking_publication_guard BEFORE INSERT OR UPDATE ON cooking_recipes
FOR EACH ROW EXECUTE FUNCTION validate_cooking_publication();
CREATE FUNCTION guard_cooking_item() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (TG_OP='DELETE' OR (OLD.runtime_enabled AND NOT NEW.runtime_enabled)) AND EXISTS(
   SELECT 1 FROM cooking_recipes WHERE published_recipe IS NOT NULL AND
   (published_recipe->>'output_item_id'=OLD.item_id OR published_recipe->>'burnt_output_item_id'=OLD.item_id OR
    EXISTS(SELECT 1 FROM jsonb_array_elements(published_recipe->'ingredients') i WHERE i->>'item_id'=OLD.item_id)))
 THEN RAISE EXCEPTION 'Disable dependent Cooking recipes before unpublishing item %',OLD.item_id; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER cooking_item_guard BEFORE UPDATE OF runtime_enabled OR DELETE ON item_definitions
FOR EACH ROW EXECUTE FUNCTION guard_cooking_item();
ALTER TABLE item_consumable_profiles DROP CONSTRAINT item_consumable_profiles_use_action_check;
ALTER TABLE item_consumable_profiles ADD CONSTRAINT item_consumable_profiles_use_action_check
 CHECK(use_action IN ('eat','drink','use','empty'));
ALTER TABLE item_consumable_profiles ADD CONSTRAINT item_consumable_empty_shape
 CHECK(use_action<>'empty' OR (result_item_id IS NOT NULL AND consume_quantity=1 AND cooldown_ms=0));

CREATE FUNCTION guard_cooking_recipe_delete() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF OLD.published_recipe IS NOT NULL THEN RAISE EXCEPTION 'Disable Cooking recipe before deleting it'; END IF;
 RETURN OLD;
END $$;
CREATE TRIGGER cooking_recipe_delete_guard BEFORE DELETE ON cooking_recipes
FOR EACH ROW EXECUTE FUNCTION guard_cooking_recipe_delete();
