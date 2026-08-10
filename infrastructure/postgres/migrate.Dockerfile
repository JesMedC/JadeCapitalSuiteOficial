# syntax=docker/dockerfile:1.7
# Imagen efimera que corre la migracion SQL contra el Postgres de jade.
FROM postgres:16-alpine
WORKDIR /migrations
# COPY es relativo al CONTEXT, no al WORKDIR. Con context=./infrastructure/postgres
# y este Dockerfile en migrations/, los archivos esta en migrations/2026....sql.
COPY migrations/20260806_0001_InitialIdentitySchema.sql         /migrations/20260806_0001_InitialIdentitySchema.sql
COPY migrations/20260806_0002_TradingSchema.sql                  /migrations/20260806_0002_TradingSchema.sql
COPY migrations/20260806_0003_TradingConfigurationSchema.sql     /migrations/20260806_0003_TradingConfigurationSchema.sql
COPY migrations/20260806_0004_AccountMarketType.sql              /migrations/20260806_0004_AccountMarketType.sql
# Importante: como usamos CMD ["bash", "-c", ...] (no el entrypoint oficial),
# NO se propaga POSTGRES_PASSWORD -> PGPASSWORD automaticamente. Lo seteamos a mano.
CMD ["bash", "-c", "until pg_isready -h postgres -U \"$POSTGRES_USER\"; do sleep 2; done && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0001_InitialIdentitySchema.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0002_TradingSchema.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0003_TradingConfigurationSchema.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0004_AccountMarketType.sql && \
     echo 'ALL MIGRATIONS OK' || \
     (echo 'PARTIAL MIGRATION — retrying (idempotent SQL)' && sleep 3 && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0001_InitialIdentitySchema.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0002_TradingSchema.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0003_TradingConfigurationSchema.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0004_AccountMarketType.sql && \
      echo 'ALL MIGRATIONS OK (retry)')"]
