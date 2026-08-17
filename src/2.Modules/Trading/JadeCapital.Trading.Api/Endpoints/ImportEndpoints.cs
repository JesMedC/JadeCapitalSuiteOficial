using System.Security.Claims;
using System.Security.Cryptography;
using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Imports;
using JadeCapital.Trading.Application.Features.Imports.BeginImport;
using JadeCapital.Trading.Application.Features.Imports.GetImportStatus;
using JadeCapital.Trading.Contracts.Imports;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// Import endpoints (slice 5a.1, Wave 5):
/// <list type="bullet">
///   <item><c>POST /api/imports/csv</c> — multipart upload (file + accountId),
///         returns 202 Accepted + { importJobId }.</item>
///   <item><c>GET /api/imports/{id}</c> — single-job status.</item>
/// </list>
/// Both require auth + the <c>api-general</c> rate-limit policy (Wave 4
/// precedent — uploads are heavyweight but 100/min is generous for the
/// beta; Wave 6 may introduce an <c>api-imports</c> 10/hour policy).
///
/// <para>
/// The upload endpoint kicks off <see cref="StreamImportService"/> in a
/// background <see cref="Task"/> after returning 202 — the FE polls the
/// status endpoint for progress.
/// </para>
/// </summary>
public static class ImportEndpoints
{
    /// <summary>10 MiB — mirrors <c>ImportJob.MaxFileSizeBytes</c>.</summary>
    public const long MaxFileSizeBytes = 10L * 1024L * 1024L;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").RequireAuthorization().RequireRateLimiting("api-general");

        group.MapPost("/csv", UploadCsvAsync);
        group.MapGet("/{id:guid}", GetStatusAsync);

        return app;
    }

    private static async Task<IResult> UploadCsvAsync(
        HttpContext http,
        ISender sender,
        IServiceProvider services,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();

        if (!http.Request.HasFormContentType)
            return Results.UnprocessableEntity(new { code = "import.content_type_invalid",
                detail = "multipart/form-data required." });

        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        var accountIdRaw = form["accountId"].ToString();

        if (file is null || file.Length == 0)
            return Results.UnprocessableEntity(new { code = "import.file_missing",
                detail = "A non-empty 'file' field is required." });

        if (file.Length > MaxFileSizeBytes)
            return Results.UnprocessableEntity(new { code = "validation.import_job.file_too_large",
                detail = $"File exceeds {MaxFileSizeBytes} bytes (10 MiB)." });

        if (!Guid.TryParse(accountIdRaw, out var accountId))
            return Results.UnprocessableEntity(new { code = "import.account_id_invalid",
                detail = "The 'accountId' field must be a valid GUID." });

        // Compute SHA-256 of the body (buffered — bodies are already < 10 MiB).
        string sha256Hex;
        await using (var stream = file.OpenReadStream())
        {
            sha256Hex = await ComputeSha256Async(stream, ct);
        }

        // Detect format via the parser contract. The endpoint is "/csv" so
        // the CSV parser wins; MT4/MT5 land in 5a.2 with their own endpoints.
        var detectedFormat = DetectFormat(file.FileName ?? string.Empty, file.OpenReadStream());

        var command = new BeginImportCommand(
            UserId: userId.Value,
            AccountId: accountId,
            FileName: file.FileName ?? "upload.csv",
            FileSizeBytes: file.Length,
            FileSha256: sha256Hex,
            DetectedFormat: detectedFormat);

        var result = await sender.Send(command, ct);
        if (result.IsFailure)
            return ResultToHttp(result.Error);

        // Kick off the streaming pipeline in the background. We pass a
        // fresh CancellationToken (not the HTTP one) — the import must
        // survive client disconnection, the same way Wave 3c's planner
        // sessions do.
        var jobId = result.Value.Id;
        // Use CancellationToken.None — the import must survive client
        // disconnection. The streaming service has its own internal timeout
        // via the parser's cancellation token.
        _ = Task.Run(async () => await RunImportInBackgroundAsync(jobId, file, services), CancellationToken.None);

        return Results.Accepted(
            uri: $"/api/imports/{jobId}",
            value: new BeginImportResponse(jobId));
    }

    private static async Task<IResult> GetStatusAsync(
        Guid id, HttpContext http, ISender sender, CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new GetImportStatusQuery(id, userId.Value), ct);
        if (result.IsFailure)
            return ResultToHttp(result.Error);

        return Results.Ok(result.Value);
    }

    private static async Task RunImportInBackgroundAsync(
        Guid jobId, IFormFile file, IServiceProvider services)
    {
        try
        {
            using var scope = services.CreateScope();
            var jobRepo = scope.ServiceProvider
                .GetRequiredService<JadeCapital.Trading.Application.Abstractions.IImportJobRepository>();

            var job = await jobRepo.GetByIdAsync(jobId, CancellationToken.None);
            if (job is null) return;

            // Re-open the stream from the form's file buffer. IFormFile
            // buffers the body in memory (multipart/form-data is small —
            // max 10 MiB), so OpenReadStream is safe.
            await using var replayStream = file.OpenReadStream();
            var parser = scope.ServiceProvider
                .GetRequiredService<JadeCapital.Shared.Kernel.Imports.IImportRowParser>();
            var streamSvc = scope.ServiceProvider
                .GetRequiredService<StreamImportService>();
            await streamSvc.ExecuteAsync(job, replayStream, parser, CancellationToken.None);
        }
        catch (Exception ex)
        {
            using var scope = services.CreateScope();
            var logger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ImportEndpoints");
            logger.LogError(ex, "Background import failed for job {JobId}", jobId);
        }
    }

    private static Guid? GetUserId(HttpContext http)
    {
        var raw = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static ImportFormat DetectFormat(string fileName, Stream head)
    {
        // 5a.1 only supports CSV; the endpoint is /api/imports/csv so the
        // default is CSV. Future slices (5a.2) may auto-detect MT4/MT5 from
        // the same endpoint and pick the parser with the highest CanParse score.
        var parserType = (fileName ?? "").ToLowerInvariant();
        if (parserType.EndsWith(".csv", StringComparison.Ordinal))
            return ImportFormat.Csv;
        if (parserType.EndsWith(".txt", StringComparison.Ordinal))
            return ImportFormat.Csv;
        return ImportFormat.Csv;  // safe default; 5a.2 will add MT4/MT5 detection
    }

    private static async Task<string> ComputeSha256Async(Stream body, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(body, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IResult ResultToHttp(Error error)
    {
        var code = error.Code ?? "error";
        return code switch
        {
            var c when c.StartsWith("notfound.", StringComparison.Ordinal) =>
                Results.NotFound(new { code, detail = error.Message }),
            var c when c.StartsWith("conflict.", StringComparison.Ordinal) =>
                Results.Conflict(new { code, detail = error.Message }),
            var c when c.StartsWith("validation.", StringComparison.Ordinal) =>
                Results.UnprocessableEntity(new { code, detail = error.Message }),
            _ => Results.BadRequest(new { code, detail = error.Message }),
        };
    }
}