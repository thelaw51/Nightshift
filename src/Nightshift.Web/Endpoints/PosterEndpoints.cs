using Microsoft.Extensions.Caching.Memory;
using Nightshift.Sonarr;

namespace Nightshift.Web.Endpoints;

/// <summary>
/// Serves series posters from Sonarr through Nightshift, so the browser never needs the API key.
/// The route only accepts an integer series id and the Sonarr file name is fixed in the client,
/// so this can't be used as a general proxy.
/// </summary>
public static class PosterEndpoints
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    // Sonarr fetches artwork shortly after a series is added, so "no poster" is only remembered briefly.
    private static readonly TimeSpan MissCacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>Maps <c>GET /posters/{seriesId}</c>.</summary>
    public static IEndpointRouteBuilder MapPosterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posters/{seriesId:int:min(1)}", GetPosterAsync);
        return app;
    }

    private static async Task<IResult> GetPosterAsync(
        int seriesId,
        ISonarrClient sonarr,
        IMemoryCache cache,
        HttpContext context,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var key = $"nightshift:poster:{seriesId}";
        if (!cache.TryGetValue(key, out PosterImage? poster))
        {
            try
            {
                poster = await sonarr.GetPosterAsync(seriesId, cancellationToken);
            }
            catch (SonarrException ex)
            {
                loggerFactory.CreateLogger(nameof(PosterEndpoints))
                    .LogWarning("Poster for series {SeriesId} unavailable: {Reason}", seriesId, ex.Message);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            cache.Set(key, poster, poster is null ? MissCacheDuration : CacheDuration);
        }

        if (poster is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = $"private, max-age={(int)CacheDuration.TotalSeconds}";
        return Results.Bytes(poster.Content, poster.ContentType);
    }
}
