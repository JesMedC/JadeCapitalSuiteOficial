using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.Domain.Common;

/// <summary>
/// Errores semanticos del bounded context Trading. Codigos
/// "{boundedContext}.{entidad}.{detalle}" para que DomainGuard enrute a
/// la excepcion correcta segun prefijo.
///
/// Nota sobre codigos: las factories de <see cref="Error"/> ya prefijan con
/// "validation." / "conflict." / etc. Por eso pasamos el codigo SIN el prefijo
/// (ej. "account.id_required") y el codigo final queda
/// "validation.account.id_required".
/// </summary>
public static class TradingDomainErrors
{
    public static class Trade
    {
        public static readonly Error IdRequired =
            Error.Validation("trade.id_required", "Trade id is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("trade.user_id_required", "User id is required.");

        public static readonly Error AccountIdRequired =
            Error.Validation("trade.account_id_required", "Account id is required.");

        public static readonly Error InstrumentIdRequired =
            Error.Validation("trade.instrument_id_required", "Instrument id is required.");

        public static readonly Error VolumeMustBePositive =
            Error.Validation("trade.volume_must_be_positive", "Volume must be greater than zero.");

        public static readonly Error EntryPriceMustBePositive =
            Error.Validation("trade.entry_price_must_be_positive", "Entry price must be greater than zero.");

        public static readonly Error ExitPriceMustBePositive =
            Error.Validation("trade.exit_price_must_be_positive", "Exit price must be greater than zero.");

        public static readonly Error EntryPriceCurrencyMismatch =
            Error.Validation("trade.entry_price_currency_mismatch", "Entry price currency does not match the symbol's quote currency.");

        public static readonly Error ExitPriceCurrencyMismatch =
            Error.Validation("trade.exit_price_currency_mismatch", "Exit price currency does not match the entry price currency.");

        public static readonly Error CurrencyMismatch =
            Error.Validation("trade.currency_mismatch", "Currencies must match.");

        public static readonly Error AlreadyClosed =
            Error.Conflict("trade.already_closed", "Trade is already closed or cancelled.");

        public static readonly Error AlreadyCancelled =
            Error.Conflict("trade.already_cancelled", "Trade is already cancelled and cannot be modified.");

        public static readonly Error StrategyTooLong =
            Error.Validation("trade.strategy_too_long", "Strategy must be at most 80 characters.");

        public static readonly Error NotesTooLong =
            Error.Validation("trade.notes_too_long", "Notes must be at most 2000 characters.");

        public static readonly Error AccountCurrencyRequired =
            Error.Validation("trade.account_currency_required", "Account currency code is required.");

        /// <summary>
        /// Slice 2c — MFE/MAE approximation. MFE is non-negative by definition
        /// (it's a magnitude of favorable excursion). Throwing here keeps the
        /// invariant enforceable from any caller that might write into the
        /// aggregate later (admin corrections, data import, etc.).
        /// </summary>
        public static readonly Error MfeMustBeNonNegative =
            Error.Validation("trade.mfe_must_be_non_negative", "MFE amount must be greater than or equal to zero.");

        /// <summary>
        /// Slice 2c — MAE is non-positive by definition (negative magnitude
        /// of adverse excursion, or zero when unknown).
        /// </summary>
        public static readonly Error MaeMustBeNonPositive =
            Error.Validation("trade.mae_must_be_non_positive", "MAE amount must be less than or equal to zero.");
    }

    public static class Symbol
    {
        public static readonly Error ValueRequired =
            Error.Validation("symbol.value_required", "Symbol value is required.");

        public static readonly Error ValueTooShort =
            Error.Validation("symbol.value_too_short", "Symbol must be at least 3 characters.");

        public static readonly Error ValueTooLong =
            Error.Validation("symbol.value_too_long", "Symbol must be at most 20 characters.");

        public static readonly Error ValueInvalidFormat =
            Error.Validation("symbol.value_invalid_format", "Symbol must contain only uppercase letters, digits and '/' separator.");
    }

    public static class Account
    {
        public static readonly Error IdRequired =
            Error.Validation("account.id_required", "Account id is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("account.user_id_required", "User id is required.");

        public static readonly Error NameRequired =
            Error.Validation("account.name_required", "Account name is required.");

        public static readonly Error NameTooLong =
            Error.Validation("account.name_too_long", "Account name must be 80 chars or less.");

        public static readonly Error BrokerRequired =
            Error.Validation("account.broker_required", "Broker is required.");

        public static readonly Error BrokerTooLong =
            Error.Validation("account.broker_too_long", "Broker must be 80 chars or less.");

        public static readonly Error CurrencyCodeInvalid =
            Error.Validation("account.currency_code_invalid", "Account currency must be a valid 3-letter uppercase ISO 4217-like code.");

        public static readonly Error InitialBalanceMustBeNonNegative =
            Error.Validation("account.initial_balance_must_be_non_negative", "Initial balance must be zero or greater.");

        public static readonly Error LeverageMustBePositive =
            Error.Validation("account.leverage_must_be_positive", "Leverage must be greater than zero.");

        public static readonly Error LeverageRequiredForForex =
            Error.Validation("account.leverage_required_for_forex", "Leverage is required for Forex accounts.");

        public static readonly Error MarketTypeRequired =
            Error.Validation("account.market_type_required", "Market type is required.");

        public static readonly Error InvalidMarketType =
            Error.Validation("account.invalid_market_type", "Invalid market type.");

        public static readonly Error AlreadyInactive =
            Error.Conflict("account.already_inactive", "Account is already inactive.");

        public static readonly Error AlreadyActive =
            Error.Conflict("account.already_active", "Account is already active.");
    }

    public static class Instrument
    {
        public static readonly Error IdRequired =
            Error.Validation("instrument.id_required", "Instrument id is required.");

        public static readonly Error SymbolRequired =
            Error.Validation("instrument.symbol_required", "Symbol is required.");

        public static readonly Error SymbolTooLong =
            Error.Validation("instrument.symbol_too_long", "Symbol must be 20 chars or less.");

        public static readonly Error ContractSizeMustBePositive =
            Error.Validation("instrument.contract_size_must_be_positive", "Contract size must be greater than zero.");

        public static readonly Error DecimalPlacesMustBeNonNegative =
            Error.Validation("instrument.decimal_places_must_be_non_negative", "Decimal places must be zero or greater.");

        public static readonly Error PipValueMustBeNonNegative =
            Error.Validation("instrument.pip_value_must_be_non_negative", "Pip value must be zero or greater.");

        public static readonly Error PayoutPercentOutOfRange =
            Error.Validation("instrument.payout_percent_out_of_range", "Payout percent must be between 0 and 1.");

        public static readonly Error AssetClassesRequired =
            Error.Validation("instrument.asset_classes_required", "At least one asset class must be selected.");

        public static readonly Error AssetClassesInvalid =
            Error.Validation("instrument.asset_classes_invalid", "Asset classes contains an invalid flag.");

        public static readonly Error AlreadyInactive =
            Error.Conflict("instrument.already_inactive", "Instrument is already inactive.");

        public static readonly Error AlreadyActive =
            Error.Conflict("instrument.already_active", "Instrument is already active.");
    }

    public static class Metrics
    {
        public static readonly Error UserNotFound =
            Error.NotFound("metrics.user_not_found", "Authenticated user was not found.");
    }

    /// <summary>
    /// Errores del bounded context del pre-trade checklist (slice 1c.1).
    ///
    /// Los codigos llevan prefijo <c>pre_trade_checklist.</c> para que
    /// el endpoint <c>POST /api/trades</c> los distinga de errores de
    /// Trade (que llevan prefijo <c>trade.</c>) y rutee los del checklist
    /// a 422 con un mensaje semantico de validacion. Los Trade errors
    /// continuan mapeando a 400 via <c>validation.*</c> porque son
    /// invariantes del trade (currency mismatch, volumen cero, etc.); los
    /// del checklist son business-rule failures que el trader puede
    /// corregir resubmitiendo.
    /// </summary>
    public static class PreTradeChecklist
    {
        public static readonly Error SubmissionRequired =
            Error.Validation("pre_trade_checklist.submission_required",
                "Pre-trade checklist submission payload is required when checklist is provided.");

        public static readonly Error RrBelowTarget =
            Error.Validation("pre_trade_checklist.rr_below_target",
                "Risk/reward at entry must be greater than or equal to the risk/reward target used.");

        public static readonly Error ConfluencesOutOfRange =
            Error.Validation("pre_trade_checklist.confluences_out_of_range",
                "Confluences count must be between 1 and 10 inclusive.");

        public static readonly Error EmotionalityOutOfRange =
            Error.Validation("pre_trade_checklist.emotionality_out_of_range",
                "Emotionality must be one of Fearful (1), Anxious (2), Neutral (3), Confident (4), Euphoric (5).");

        public static readonly Error SetupQualityOutOfRange =
            Error.Validation("pre_trade_checklist.setup_quality_out_of_range",
                "Setup quality must be one of Poor (1), BelowAverage (2), Average (3), Good (4), Excellent (5).");
    }

    /// <summary>
    /// Errores del bounded context del post-trade review (slice 1d.1).
    ///
    /// Mismas convenciones que <c>PreTradeChecklist</c>: los codigos
    /// llevan prefijo <c>trade_review.</c> para que el endpoint
    /// <c>POST /api/trades/{id}/review</c> distinga errores de review vs
    /// errores de Trade (que llevan prefijo <c>trade.</c>) y los rutee
    /// a HTTP correcto via <c>ProblemFromResult</c>.
    /// </summary>
    public static class TradeReview
    {
        public static readonly Error IdRequired =
            Error.Validation("trade_review.id_required", "Review id is required.");

        public static readonly Error TradeIdRequired =
            Error.Validation("trade_review.trade_id_required", "Trade id is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("trade_review.user_id_required", "User id is required.");

        public static readonly Error SubmissionRequired =
            Error.Validation("trade_review.submission_required",
                "Review payload is required.");

        public static readonly Error EmotionalityOutOfRange =
            Error.Validation("trade_review.emotionality_out_of_range",
                "Emotionality must be between 1 and 5 inclusive.");

        public static readonly Error RatingOutOfRange =
            Error.Validation("trade_review.rating_out_of_range",
                "Rating must be between 1 and 5 inclusive.");

        public static readonly Error SetupUsedTooLong =
            Error.Validation("trade_review.setup_used_too_long",
                "Setup tag must be 64 characters or less.");

        public static readonly Error LessonsTooLong =
            Error.Validation("trade_review.lessons_too_long",
                "Lessons must be 5000 characters or less.");

        public static readonly Error TradeNotClosed =
            Error.Conflict("trade_review.trade_not_closed",
                "Post-trade review is only allowed for closed trades.");

        public static readonly Error AlreadyExists =
            Error.Conflict("trade_review.already_exists",
                "A review already exists for this trade.");

        public static readonly Error NotFound =
            Error.NotFound("trade_review.not_found",
                "Post-trade review not found for this trade.");
    }

    /// <summary>
    /// Errores del bounded context de trade attachments (slice 1d.1).
    ///
    /// Los codigos llevan prefijo <c>trade_attachment.</c>. Los errores
    /// de validacion (sha256, size, etc.) mapean a 400 default; los de
    /// conflicto (collision, already_uploaded) mapean a 409.
    /// </summary>
    public static class TradeAttachment
    {
        public static readonly Error IdRequired =
            Error.Validation("trade_attachment.id_required", "Attachment id is required.");

        public static readonly Error ReviewIdRequired =
            Error.Validation("trade_attachment.review_id_required", "Review id is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("trade_attachment.user_id_required", "User id is required.");

        public static readonly Error ObjectKeyRequired =
            Error.Validation("trade_attachment.object_key_required", "Object key is required.");

        public static readonly Error ObjectKeyCollision =
            Error.Conflict("trade_attachment.object_key_collision",
                "An attachment with this object key already exists.");

        public static readonly Error ContentTypeRequired =
            Error.Validation("trade_attachment.content_type_required", "Content type is required.");

        public static readonly Error SizeOutOfRange =
            Error.Validation("trade_attachment.size_out_of_range",
                "Attachment size must be between 1 byte and 10 MB.");

        public static readonly Error Sha256ShapeInvalid =
            Error.Validation("trade_attachment.sha256_shape_invalid",
                "SHA-256 must be 64 hex characters.");

        public static readonly Error AlreadyUploaded =
            Error.Conflict("trade_attachment.already_uploaded",
                "Attachment has already been confirmed as uploaded.");

        public static readonly Error AlreadyFailed =
            Error.Conflict("trade_attachment.already_failed",
                "Attachment has already been marked as failed.");

        public static readonly Error NotFound =
            Error.NotFound("trade_attachment.not_found",
                "Attachment not found.");
    }

    /// <summary>
    /// Errores del bounded context del daily journal (slice 2a.1).
    ///
    /// Los codigos llevan prefijo <c>journal.</c> y mapean a 400
    /// (validation) o 404 (notfound) segun corresponda. Los errores
    /// semanticos (mood fuera de rango, plan demasiado largo, etc.)
    /// son <see cref="Error.Validation"/>; la falta de entry para un
    /// GET es <see cref="Error.NotFound"/>.
    /// </summary>
    public static class Journal
    {
        public static readonly Error UserIdRequired =
            Error.Validation("journal.user_id_required", "User id is required.");

        public static readonly Error TimezoneRequired =
            Error.Validation("journal.timezone_required", "Timezone is required.");

        public static readonly Error MoodOutOfRange =
            Error.Validation("journal.mood_out_of_range",
                "Mood must be between 1 and 5 inclusive (1=Fearful, 2=Anxious, 3=Neutral, 4=Confident, 5=Euphoric).");

        public static readonly Error PremarketPlanTooLong =
            Error.Validation("journal.premarket_plan_too_long",
                "Premarket plan must be at most 2000 characters.");

        public static readonly Error PostmarketReflectionTooLong =
            Error.Validation("journal.postmarket_reflection_too_long",
                "Postmarket reflection must be at most 5000 characters.");

        public static readonly Error TooManyTags =
            Error.Validation("journal.too_many_tags",
                "Tags array must contain at most 10 items.");

        public static readonly Error TagTooLong =
            Error.Validation("journal.tag_too_long",
                "Each tag must be at most 32 characters.");

        public static readonly Error NothingToSave =
            Error.Validation("journal.nothing_to_save",
                "At least one field must be present to save or update the journal entry.");

        public static readonly Error NotFound =
            Error.NotFound("journal.not_found",
                "Journal entry not found for the given date.");
    }

    /// <summary>
    /// Errores del bounded context de Strategies (slice 3a).
    ///
    /// Los codigos llevan prefijo <c>strategy.</c>. Las validaciones
    /// (name vacio, name demasiado largo, etc.) son <see cref="Error.Validation"/>
    /// y mapean a 400. El duplicate-name (otro active con el mismo name)
    /// es <see cref="Error.Conflict"/> y mapea a 409. El not-found es
    /// <see cref="Error.NotFound"/> y mapea a 404.
    /// </summary>
    public static class Strategy
    {
        public static readonly Error UserIdRequired =
            Error.Validation("strategy.user_id_required", "User id is required.");

        public static readonly Error NameRequired =
            Error.Validation("strategy.name_required", "Strategy name is required.");

        public static readonly Error NameTooLong =
            Error.Validation("strategy.name_too_long",
                $"Strategy name must be at most {JadeCapital.Trading.Domain.Strategies.Strategy.MaxNameLength} characters.");

        public static readonly Error DescriptionTooLong =
            Error.Validation("strategy.description_too_long",
                $"Strategy description must be at most {JadeCapital.Trading.Domain.Strategies.Strategy.MaxDescriptionLength} characters.");

        public static readonly Error RulesTooLong =
            Error.Validation("strategy.rules_too_long",
                $"Strategy rules must be at most {JadeCapital.Trading.Domain.Strategies.Strategy.MaxRulesLength} characters.");

        public static readonly Error InvalidTimeframe =
            Error.Validation("strategy.invalid_timeframe",
                "Strategy timeframe must be one of M1, M5, M15, M30, H1, H4, D1, W1, MN.");

        public static readonly Error SymbolTooLong =
            Error.Validation("strategy.symbol_too_long",
                "Strategy symbol must be at most 20 characters.");

        public static readonly Error DuplicateName =
            Error.Conflict("strategy.duplicate_name",
                "An active strategy with this name already exists for the user.");

        public static readonly Error NotFound =
            Error.NotFound("strategy.not_found", "Strategy not found.");
    }
}
