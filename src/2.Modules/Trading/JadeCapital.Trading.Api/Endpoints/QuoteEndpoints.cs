using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Quotes.GetQuote;
using JadeCapital.Trading.Application.Features.Quotes.GetQuotesBulk;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace JadeCapital.Trading.Api.Endpoints;

public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes")
            .RequireAuthorization()
            .RequireRateLimiting("api-quotes");

        group.MapGet("/{symbol}", GetBySymbolAsync);
        group.MapGet("/", GetBulkAsync);

        return app;
    }

    private static async Task<IResult> GetBySymbolAsync(
        string symbol, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetQuoteQuery(symbol), ct);
        return ResultToHttp(result, dto => Results.Ok(dto));
    }

    private static async Task<IResult> GetBulkAsync(
        string? symbols, ISender sender, CancellationToken ct)
    {
        var list = string.IsNullOrWhiteSpace(symbols)
            ? Array.Empty<string>()
            : symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await sender.Send(new GetQuotesBulkQuery(list), ct);
        return ResultToHttp(result, dtos => Results.Ok(dtos));
    }

    private static IResult ResultToHttp<T>(Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess) return onSuccess(result.Value);
        var code = result.Error.Code ?? "error";
        return code switch
        {
            var c when c.StartsWith("notfound.", StringComparison.Ordinal) =>
                Results.NotFound(new { code, detail = result.Error.Message }),
            var c when c.StartsWith("validation.", StringComparison.Ordinal) =>
                Results.UnprocessableEntity(new { code = c, detail = result.Error.Message }),
            _ => Results.BadRequest(new { code, detail = result.Error.Message }),
        };
    }
}
