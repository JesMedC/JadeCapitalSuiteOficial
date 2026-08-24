-- Post-write rollback rehearsal. Writers must be quiesced before execution.
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('audit.events:0040'));

CREATE TABLE audit.events_rollback_0040
    (LIKE audit.events_unpartitioned_0040 INCLUDING ALL);

LOCK TABLE audit.events, audit.events_unpartitioned_0040, audit.events_rollback_0040
    IN ACCESS EXCLUSIVE MODE;

DO $rollback$
BEGIN
    IF (SELECT relkind FROM pg_class WHERE oid=to_regclass('audit.events')) IS DISTINCT FROM 'p'
       OR (SELECT state FROM audit.migration_0040_state WHERE singleton) <> 'ACTIVE'
       OR to_regclass('audit.events_partitioned_0040') IS NOT NULL
       OR to_regclass('audit.events_unpartitioned_0040_initial') IS NOT NULL THEN
        RAISE EXCEPTION '0040 rollback catalog contradiction';
    END IF;
    IF EXISTS (SELECT id FROM audit.events GROUP BY id HAVING count(*) > 1) THEN
        RAISE EXCEPTION '0040 rollback violates prior id-only primary key';
    END IF;
END
$rollback$;

INSERT INTO audit.events_rollback_0040
    (id, entity_type, entity_id, action, tenant_id, user_id, changes_json, occurred_at)
SELECT id, entity_type, entity_id, action, tenant_id, user_id, changes_json, occurred_at
FROM audit.events;

DO $validate$
BEGIN
    IF (SELECT count(*) FROM audit.events) <> (SELECT count(*) FROM audit.events_rollback_0040)
       OR EXISTS (
          (SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events
           EXCEPT ALL
           SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events_rollback_0040)
          UNION ALL
          (SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events_rollback_0040
           EXCEPT ALL
           SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events)
       ) THEN
        RAISE EXCEPTION '0040 rollback exact-copy validation failed';
    END IF;
END
$validate$;

ALTER TABLE audit.events_unpartitioned_0040 RENAME TO events_unpartitioned_0040_initial;
ALTER TABLE audit.events RENAME TO events_partitioned_0040;
ALTER TABLE audit.events_rollback_0040 RENAME TO events;
UPDATE audit.migration_0040_state SET state='PREPARED' WHERE singleton;
COMMIT;
