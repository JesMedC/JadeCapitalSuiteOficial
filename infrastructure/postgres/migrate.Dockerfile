# syntax=docker/dockerfile:1.7
# Imagen efimera que corre la migracion SQL contra el Postgres de jade.
FROM postgres:16-alpine
WORKDIR /migrations
# COPY es relativo al CONTEXT, no al WORKDIR. Con context=./infrastructure/postgres
# y este Dockerfile en migrations/, el archivo esta en migrations/2026....sql.
COPY migrations/20260806_0001_InitialIdentitySchema.sql /migrations/20260806_0001_InitialIdentitySchema.sql
# Importante: como usamos CMD ["bash", "-c", ...] (no el entrypoint oficial),
# NO se propaga POSTGRES_PASSWORD -> PGPASSWORD automaticamente. Lo seteamos a mano.
CMD ["bash", "-c", "until pg_isready -h postgres -U \"$POSTGRES_USER\"; do sleep 2; done && PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0001_InitialIdentitySchema.sql && echo 'MIGRATION OK' || (echo 'MIGRATION FAILED — retrying because SQL may be idempotent and partial run left state'; sleep 3; PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0001_InitialIdentitySchema.sql && echo 'MIGRATION OK (retry)')"]
