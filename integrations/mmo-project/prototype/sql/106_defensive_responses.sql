-- PREPARED ONLY. Requires separately approved stopped/drained game and Studio.
-- Explicit per-item eligibility; no character, requirements, identity or prior bonus changes.
BEGIN;
LOCK TABLE item_definitions, item_combat_profiles, item_combat_bonuses IN SHARE ROW EXCLUSIVE MODE;
DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_number=105)
 OR EXISTS (SELECT 1 FROM schema_migrations WHERE migration_number=106) THEN
  RAISE EXCEPTION 'Reconcile migration 105 baseline; apply 106 once only';
 END IF;
 IF (SELECT count(*) FROM item_definitions d JOIN item_combat_profiles p USING(item_id)
     WHERE d.runtime_enabled AND d.equipment_slot_id='right_hand' AND p.attack_type='melee'
     AND d.item_id IN ('bronze_dagger','inventory_9_dagger','inventory_125_gold_sword','inventory_3_broad_sword',
                      'inventory_120_war_hammer','inventory_153_gold_staff','inventory_442_bo')) <> 7
 OR (SELECT count(*) FROM item_definitions WHERE runtime_enabled AND equipment_slot_id='left_hand'
     AND item_id IN ('inventory_37_small_shield','inventory_209_champions_shield')) <> 2 THEN
  RAISE EXCEPTION 'Approved defensive-response equipment baseline differs';
 END IF;
END $$;
CREATE TABLE defensive_responses_106_bonus_backup AS SELECT * FROM item_combat_bonuses;
ALTER TABLE item_combat_bonuses
 ADD COLUMN parry_base_chance_basis_points integer NOT NULL DEFAULT 0,
 ADD COLUMN block_base_chance_basis_points integer NOT NULL DEFAULT 0,
 ADD CONSTRAINT item_combat_bonuses_parry_chance_check CHECK (parry_base_chance_basis_points BETWEEN 0 AND 1000),
 ADD CONSTRAINT item_combat_bonuses_block_chance_check CHECK (block_base_chance_basis_points BETWEEN 0 AND 1000);

INSERT INTO item_combat_bonuses(item_id, parry_base_chance_basis_points)
SELECT item_id,500 FROM item_definitions WHERE item_id IN
 ('bronze_dagger','inventory_9_dagger','inventory_125_gold_sword','inventory_3_broad_sword',
  'inventory_120_war_hammer','inventory_153_gold_staff','inventory_442_bo')
ON CONFLICT(item_id) DO UPDATE SET parry_base_chance_basis_points=excluded.parry_base_chance_basis_points;
INSERT INTO item_combat_bonuses(item_id, block_base_chance_basis_points)
VALUES ('inventory_37_small_shield',500),('inventory_209_champions_shield',500)
ON CONFLICT(item_id) DO UPDATE SET block_base_chance_basis_points=excluded.block_base_chance_basis_points;
-- Parent timestamps identify complete authored item revisions in Studio.
UPDATE item_definitions SET updated_at=clock_timestamp() WHERE item_id IN
 ('bronze_dagger','inventory_9_dagger','inventory_125_gold_sword','inventory_3_broad_sword',
  'inventory_120_war_hammer','inventory_153_gold_staff','inventory_442_bo',
  'inventory_37_small_shield','inventory_209_champions_shield');
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES ('106_defensive_responses.sql',106,'defensive_responses',
 'Explicit passive Player block/parry chances on nine approved items; all prior bonuses, requirements and character data retained.');
COMMIT;
