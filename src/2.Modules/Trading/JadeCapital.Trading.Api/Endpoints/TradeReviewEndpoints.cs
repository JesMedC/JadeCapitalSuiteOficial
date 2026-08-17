using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Attachments;
using JadeCapital.Trading.Application.Features.TradeReviews.ConfirmAttachmentUpload;
using JadeCapital.Trading.Application.Features.TradeReviews.CreateOrUpdate;
using JadeCapital.Trading.Application.Features.TradeReviews.DeleteAttachment;
using JadeCapital.Trading.Application.Features.TradeReviews.GetTradeReview;
using JadeCapital.Trading.Application.Features.TradeReviews.RequestAttachmentUpload;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// Endpoints del post-trade review + MinIO attachments (slice 1d.1).
///
/// Sigue el patron minimal-API del modulo:
/// <list type="bullet">
///   <item>Cada handler MediatR se expone via delegate que recibe
///   <see cref="ISender"/>; no hay logica de negocio en el endpoint.</item>
///   <item>Validacion corre en el <c>ValidationBehavior</c> pipeline;
///   errores de dominio/application se mapean a ProblemDetails via
///   <see cref="ProblemFromResult"/>.</item>
///   <item>Rate limit: <c>auth-strict</c> para POST/PUT/DELETE (writes),
///   <c>api-general</c> para GET.</item>
/// </list>
///
/// URLs:
/// <list type="bullet">
///   <item>POST <c>/api/trades/{tradeId}/review</c> — upsert review.</item>
///   <item>GET <c>/api/trades/{tradeId}/review</c> — fetch review + attachments.</item>
///   <item>POST <c>/api/trades/{tradeId}/review/attachments</c> — pedi un slot
///   (presigned URL + attachment ID pending).</item>
///   <item>POST <c>/api/trades/{tradeId}/review/attachments/{attachmentId}/complete</c>
///   — confirma la subida despues del PUT directo a MinIO.</item>
///   <item>DELETE <c>/api/trades/{tradeId}/review/attachments/{attachmentId}</c>
///   — borra el attachment (clean up best-effort del object).</item>
/// </list>
/// </summary>
public static class TradeReviewEndpoints
{
    public static IEndpointRouteBuilder MapTradeReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trades/{tradeId:guid}/review")
            .WithTags("Trading")
            .RequireAuthorization();

        // POST /api/trades/{tradeId}/review — upsert (create OR update).
        group.MapPost("/", UpsertReviewAsync)
            .WithName("UpsertTradeReview")
            .WithSummary("Crea o actualiza el post-trade review de un trade cerrado.")
            .Produces<TradeReviewDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting("api-general");

        // GET /api/trades/{tradeId}/review — fetch review + attachments.
        group.MapGet("/", GetReviewAsync)
            .WithName("GetTradeReview")
            .WithSummary("Devuelve el review post-trade del trade con sus attachments.")
            .Produces<TradeReviewDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        // POST /api/trades/{tradeId}/review/attachments — request slot.
        group.MapPost("/attachments", RequestAttachmentAsync)
            .WithName("RequestAttachmentUpload")
            .WithSummary("Genera un slot pending + presigned PUT URL para subir un attachment directo a MinIO.")
            .Produces<RequestAttachmentUploadResultDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable) // MinIO down
            .RequireRateLimiting("api-general");

        // POST /api/trades/{tradeId}/review/attachments/{attachmentId}/complete
        group.MapPost("/attachments/{attachmentId:guid}/complete", CompleteAttachmentAsync)
            .WithName("CompleteAttachmentUpload")
            .WithSummary("Confirma que el cliente subio los bytes al slot; verifica tamano contra MinIO.")
            .Produces<TradeAttachmentDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("api-general");

        // DELETE /api/trades/{tradeId}/review/attachments/{attachmentId}
        group.MapDelete("/attachments/{attachmentId:guid}", DeleteAttachmentAsync)
            .WithName("DeleteAttachment")
            .WithSummary("Borra un attachment del review (clean up best-effort del object en MinIO).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        // ===== Slice 4d — Attachment thumbnail + usage =====
        // Both endpoints live on a separate /api/attachments group (NOT
        // under /api/trades/{tradeId}/review) because they operate on the
        // attachment as a first-class resource, not on a review sub-path.
        // Per Wave 4 chain convention, the slice extends the existing
        // TradeReviewEndpoints class — no new MapAttachmentEndpoints group.
        var attachments = app.MapGroup("/api/attachments")
            .WithTags("Trading")
            .RequireAuthorization();

        // GET /api/attachments/{attachmentId}/thumbnail?width=N&height=N
        // Returns a presigned GET URL pointing to the resized image.
        attachments.MapGet("/{attachmentId:guid}/thumbnail", GetThumbnailAsync)
            .WithName("GetAttachmentThumbnail")
            .WithSummary("Devuelve un presigned GET URL para el thumbnail 256x256 (default) del attachment. Solo aplica a image/png|jpeg|webp.")
            .Produces<ThumbnailUrlDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .RequireRateLimiting("api-general");

        // GET /api/attachments/usage
        attachments.MapGet("/usage", GetUsageAsync)
            .WithName("GetAttachmentUsage")
            .WithSummary("Snapshot del uso de attachments del usuario autenticado (totalBytes, attachmentCount, quotaBytes, percentFull).")
            .Produces<AttachmentUsageDto>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        return app;
    }

    // ===== Helpers =====

    private static Guid GetUserId(HttpContext http)
    {
        var claim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !Guid.TryParse(claim, out var id))
            throw new UnauthorizedAccessException("Invalid user claim.");
        return id;
    }

    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status400BadRequest,
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            // Slice 4d — quota exceeded → 413 Payload Too Large (per spec).
            "failure.attachment.quota_exceeded" => StatusCodes.Status413PayloadTooLarge,
            // Slice 4d — non-image thumbnail → 415 Unsupported Media Type.
            "failure.attachment.thumbnail_not_supported" => StatusCodes.Status415UnsupportedMediaType,
            // Slice 4d — virus scanner down → 503 Service Unavailable.
            "failure.attachment.scanner_unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    // ===== Handlers =====

    private static async Task<IResult> UpsertReviewAsync(
        Guid tradeId,
        [Microsoft.AspNetCore.Mvc.FromBody] UpsertReviewRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new CreateOrUpdateTradeReviewCommand(
            TradeId: tradeId,
            UserId: userId,
            Emotionality: req.Emotionality,
            Rating: req.Rating,
            SetupUsed: req.SetupUsed,
            Lessons: req.Lessons);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetReviewAsync(
        Guid tradeId,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new GetTradeReviewQuery(tradeId, userId), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> RequestAttachmentAsync(
        Guid tradeId,
        [Microsoft.AspNetCore.Mvc.FromBody] RequestAttachmentRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new RequestAttachmentUploadCommand(
            TradeId: tradeId,
            UserId: userId,
            ContentType: req.ContentType,
            SizeBytes: req.SizeBytes,
            Filename: req.Filename);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Created($"/api/trades/{tradeId}/review/attachments/{result.Value.AttachmentId}", result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> CompleteAttachmentAsync(
        Guid tradeId,
        Guid attachmentId,
        [Microsoft.AspNetCore.Mvc.FromBody] CompleteAttachmentRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new ConfirmAttachmentUploadedCommand(
            AttachmentId: attachmentId,
            UserId: userId,
            Sha256: req.Sha256);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeleteAttachmentAsync(
        Guid tradeId,
        Guid attachmentId,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new DeleteAttachmentCommand(attachmentId, userId), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }

    // ===== Slice 4d — attachment thumbnail + usage =====

    private static async Task<IResult> GetThumbnailAsync(
        Guid attachmentId,
        [Microsoft.AspNetCore.Mvc.FromQuery] int? width,
        [Microsoft.AspNetCore.Mvc.FromQuery] int? height,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        // Defaults: 256x256 (per spec — also matches the values used by
        // the MinIO transform params). The handler clamps to [16, 1024].
        var w = width ?? 256;
        var h = height ?? 256;

        var result = await sender.Send(
            new GetAttachmentThumbnailQuery(attachmentId, userId, w, h), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetUsageAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new GetAttachmentUsageQuery(userId), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}

// ===== Request records =====

public sealed record UpsertReviewRequest(
    byte Emotionality,
    byte? Rating,
    string? SetupUsed,
    string? Lessons);

public sealed record RequestAttachmentRequest(
    string ContentType,
    long SizeBytes,
    string? Filename);

public sealed record CompleteAttachmentRequest(string? Sha256);
