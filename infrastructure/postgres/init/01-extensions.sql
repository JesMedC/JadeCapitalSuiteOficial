-- Activar extensiones útiles para NUMERIC y UUIDs.
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Schema dedicado (alineado con JadeCapitalDbContext).
CREATE SCHEMA IF NOT EXISTS jade;

-- Las migraciones de EF Core crearán las tablas. Este init sólo garantiza
-- que el esquema y extensiones existan incluso si la app arranca antes
-- que las migraciones.