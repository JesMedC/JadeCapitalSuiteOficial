-- Stateful, rerunnable conversion of audit.events to UTC monthly partitions.
-- Preparation commits separately so a failed activation remains PREPARED and is safely recopied.
BEGIN;

SELECT pg_advisory_xact_lock(hashtext('audit.events:0040'));

CREATE TABLE IF NOT EXISTS audit.migration_0040_state (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    state text NOT NULL CHECK (state IN ('PREPARED', 'ACTIVE')),
    partition_anchor timestamptz NOT NULL,
    lower_bound date NOT NULL,
    upper_bound date NOT NULL
);

DO $migration$
DECLARE
    migration_state text;
    canonical_kind "char";
    shadow_kind "char";
    anchor date;
    lower_month date;
    upper_month date;
    month_start date;
BEGIN
    SELECT state, partition_anchor AT TIME ZONE 'UTC', lower_bound, upper_bound
      INTO migration_state, anchor, lower_month, upper_month
      FROM audit.migration_0040_state WHERE singleton FOR UPDATE;
    SELECT relkind INTO canonical_kind FROM pg_class WHERE oid = to_regclass('audit.events');
    SELECT relkind INTO shadow_kind FROM pg_class WHERE oid = to_regclass('audit.events_partitioned_0040');

    IF migration_state IS NULL THEN
        IF canonical_kind IS DISTINCT FROM 'r' OR shadow_kind IS NOT NULL
           OR to_regclass('audit.events_unpartitioned_0040') IS NOT NULL THEN
            RAISE EXCEPTION '0040 ABSENT catalog contradiction';
        END IF;

        anchor := date_trunc('month', transaction_timestamp() AT TIME ZONE 'UTC')::date;
        SELECT coalesce(date_trunc('month', min(occurred_at) AT TIME ZONE 'UTC')::date, anchor),
               greatest(
                   coalesce((date_trunc('month', max(occurred_at) AT TIME ZONE 'UTC') + interval '1 month')::date,
                            (anchor + interval '2 months')::date),
                   (anchor + interval '2 months')::date)
          INTO lower_month, upper_month FROM audit.events;

        CREATE TABLE audit.events_partitioned_0040
            (LIKE audit.events INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING CONSTRAINTS INCLUDING COMMENTS)
            PARTITION BY RANGE (occurred_at);
        ALTER TABLE audit.events_partitioned_0040
            ADD CONSTRAINT events_partitioned_0040_pkey PRIMARY KEY (id, occurred_at);

        month_start := lower_month;
        WHILE month_start < upper_month LOOP
            EXECUTE format(
                'CREATE TABLE audit.%I PARTITION OF audit.events_partitioned_0040 FOR VALUES FROM (%L) TO (%L)',
                'events_' || to_char(month_start, 'YYYYMM'), month_start,
                (month_start + interval '1 month')::date);
            month_start := (month_start + interval '1 month')::date;
        END LOOP;
        CREATE TABLE audit.events_default PARTITION OF audit.events_partitioned_0040 DEFAULT;
        CREATE INDEX ix_audit_events_entity_0040 ON audit.events_partitioned_0040 (entity_type, entity_id);
        CREATE INDEX ix_audit_events_tenant_time_0040 ON audit.events_partitioned_0040 (tenant_id, occurred_at DESC);
        CREATE INDEX ix_audit_events_user_0040 ON audit.events_partitioned_0040 (user_id);

        INSERT INTO audit.migration_0040_state(singleton, state, partition_anchor, lower_bound, upper_bound)
        VALUES (true, 'PREPARED', anchor AT TIME ZONE 'UTC', lower_month, upper_month);
    ELSIF migration_state = 'PREPARED' THEN
        IF canonical_kind IS DISTINCT FROM 'r' OR shadow_kind IS DISTINCT FROM 'p'
           OR anchor IS NULL OR lower_month > anchor OR upper_month < (anchor + interval '2 months')::date THEN
            RAISE EXCEPTION '0040 PREPARED catalog contradiction';
        END IF;
    ELSIF migration_state = 'ACTIVE' THEN
        IF canonical_kind IS DISTINCT FROM 'p' OR shadow_kind IS NOT NULL THEN
            RAISE EXCEPTION '0040 ACTIVE catalog contradiction';
        END IF;
    ELSE
        RAISE EXCEPTION '0040 unknown state';
    END IF;
END
$migration$;

COMMIT;

DO $checkpoint$
BEGIN
    IF current_setting('audit.migration_0040_stop_after_prepare', true) = 'on' THEN
        RAISE EXCEPTION '0040 test checkpoint after PREPARED';
    END IF;
END
$checkpoint$;

BEGIN;
SELECT pg_advisory_xact_lock(hashtext('audit.events:0040'));

DO $activation$
DECLARE
    migration_state text;
    canonical_kind "char";
    source_count bigint;
    shadow_count bigint;
BEGIN
    SELECT state INTO migration_state
      FROM audit.migration_0040_state WHERE singleton FOR UPDATE;
    SELECT relkind INTO canonical_kind FROM pg_class WHERE oid = to_regclass('audit.events');

    IF migration_state = 'PREPARED' THEN
        IF canonical_kind IS DISTINCT FROM 'r'
           OR (SELECT relkind FROM pg_class WHERE oid=to_regclass('audit.events_partitioned_0040')) IS DISTINCT FROM 'p'
           OR to_regclass('audit.events_unpartitioned_0040') IS NOT NULL THEN
            RAISE EXCEPTION '0040 activation catalog contradiction';
        END IF;

        LOCK TABLE audit.events, audit.events_partitioned_0040 IN ACCESS EXCLUSIVE MODE;
        TRUNCATE audit.events_partitioned_0040;
        INSERT INTO audit.events_partitioned_0040
            (id, entity_type, entity_id, action, tenant_id, user_id, changes_json, occurred_at)
        SELECT id, entity_type, entity_id, action, tenant_id, user_id, changes_json, occurred_at
          FROM audit.events;

        SELECT count(*) INTO source_count FROM audit.events;
        SELECT count(*) INTO shadow_count FROM audit.events_partitioned_0040;
        IF source_count <> shadow_count OR EXISTS (
            (SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events
             EXCEPT ALL
             SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events_partitioned_0040)
            UNION ALL
            (SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events_partitioned_0040
             EXCEPT ALL
             SELECT id,entity_type,entity_id,action,tenant_id,user_id,changes_json,occurred_at FROM audit.events)
        ) THEN
            RAISE EXCEPTION '0040 exact-copy validation failed';
        END IF;

        ALTER TABLE audit.events RENAME TO events_unpartitioned_0040;
        ALTER TABLE audit.events_partitioned_0040 RENAME TO events;
        UPDATE audit.migration_0040_state SET state = 'ACTIVE' WHERE singleton;
    ELSIF migration_state <> 'ACTIVE' THEN
        RAISE EXCEPTION '0040 cannot activate unknown state';
    END IF;
END
$activation$;

DO $active_validation$
DECLARE
    anchor date;
BEGIN
    SELECT partition_anchor AT TIME ZONE 'UTC' INTO anchor
      FROM audit.migration_0040_state WHERE singleton AND state='ACTIVE';
    IF anchor IS NULL
       OR (SELECT relkind FROM pg_class WHERE oid=to_regclass('audit.events')) IS DISTINCT FROM 'p'
       OR (SELECT relkind FROM pg_class WHERE oid=to_regclass('audit.events_unpartitioned_0040')) IS DISTINCT FROM 'r'
       OR to_regclass('audit.events_default') IS NULL
       OR to_regclass('audit.events_' || to_char(anchor, 'YYYYMM')) IS NULL
       OR to_regclass('audit.events_' || to_char(anchor + interval '1 month', 'YYYYMM')) IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM pg_index i
           WHERE i.indrelid='audit.events'::regclass AND i.indisprimary
             AND (SELECT array_agg(a.attname ORDER BY k.ordinality)
                    FROM unnest(i.indkey) WITH ORDINALITY k(attnum, ordinality)
                    JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum)
                 = ARRAY['id','occurred_at']::name[])
    THEN
        RAISE EXCEPTION '0040 ACTIVE invariant failed';
    END IF;
END
$active_validation$;

COMMIT;
