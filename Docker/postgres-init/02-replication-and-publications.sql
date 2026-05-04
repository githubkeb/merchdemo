DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'replicator') THEN
        CREATE ROLE replicator WITH REPLICATION LOGIN PASSWORD 'replicator';
    END IF;
END
$$;

CREATE PUBLICATION merchantdemo_all FOR ALL TABLES;

\connect merchantaggregates

CREATE PUBLICATION merchantaggregates_all FOR ALL TABLES;
