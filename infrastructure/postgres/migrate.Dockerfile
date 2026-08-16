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
COPY migrations/20260806_0005_InstrumentMultiAssetClass.sql      /migrations/20260806_0005_InstrumentMultiAssetClass.sql
COPY migrations/20260811_0006_PasswordRecovery.sql               /migrations/20260811_0006_PasswordRecovery.sql
COPY migrations/20260812_0007_RecoverySupersession.sql           /migrations/20260812_0007_RecoverySupersession.sql
COPY migrations/20260813_0007_BillingSubscriptions.sql           /migrations/20260813_0007_BillingSubscriptions.sql
COPY migrations/20260814_0008_SeedPlans.sql                       /migrations/20260814_0008_SeedPlans.sql
COPY migrations/0009_risk_profiles.sql                           /migrations/0009_risk_profiles.sql
COPY migrations/0011_pre_trade_checklists.sql                    /migrations/0011_pre_trade_checklists.sql
COPY migrations/0012_trade_reviews_and_attachments.sql          /migrations/0012_trade_reviews_and_attachments.sql
COPY migrations/0013_journal_entries.sql                        /migrations/0013_journal_entries.sql
COPY migrations/0014_trades_mfe_mae.sql                        /migrations/0014_trades_mfe_mae.sql
COPY migrations/0015a_strategies.sql                           /migrations/0015a_strategies.sql
# Importante: como usamos CMD ["bash", "-c", ...] (no el entrypoint oficial),
# NO se propaga POSTGRES_PASSWORD -> PGPASSWORD automaticamente. Lo seteamos a mano.
CMD ["bash", "-c", "until pg_isready -h postgres -U \"$POSTGRES_USER\"; do sleep 2; done && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0001_InitialIdentitySchema.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0002_TradingSchema.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0003_TradingConfigurationSchema.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0004_AccountMarketType.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0005_InstrumentMultiAssetClass.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260811_0006_PasswordRecovery.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260812_0007_RecoverySupersession.sql && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260813_0007_BillingSubscriptions.sql && \
PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260814_0008_SeedPlans.sql && \
PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0009_risk_profiles.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0011_pre_trade_checklists.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0012_trade_reviews_and_attachments.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0012_trade_reviews_and_attachments.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0013_journal_entries.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0014_trades_mfe_mae.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0015a_strategies.sql && \
       echo 'ALL MIGRATIONS OK' || \
     (echo 'PARTIAL MIGRATION — retrying (idempotent SQL)' && sleep 3 && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0001_InitialIdentitySchema.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0002_TradingSchema.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0003_TradingConfigurationSchema.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0004_AccountMarketType.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260806_0005_InstrumentMultiAssetClass.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260811_0006_PasswordRecovery.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260812_0007_RecoverySupersession.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260813_0007_BillingSubscriptions.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/20260814_0008_SeedPlans.sql && \
      PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0009_risk_profiles.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0011_pre_trade_checklists.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0013_journal_entries.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0014_trades_mfe_mae.sql && \
       PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f /migrations/0015a_strategies.sql && \
       echo 'ALL MIGRATIONS OK (retry)')"]
