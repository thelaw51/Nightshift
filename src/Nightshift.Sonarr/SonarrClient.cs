using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Nightshift.Sonarr;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper over Sonarr's /api/v3. The base address, API key header,
/// timeout and no-redirect handler are configured in <see cref="SonarrServiceCollectionExtensions"/>.
/// Request paths are relative (no leading slash) so a Sonarr URL base such as <c>/sonarr</c> is kept.
/// </summary>
public sealed class SonarrClient(HttpClient http) : ISonarrClient
{
    /// <summary>Posters larger than this are refused rather than buffered.</summary>
    public const int MaxPosterBytes = 5 * 1024 * 1024;

    private static readonly SonarrJsonContext Json = SonarrJsonContext.Default;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Series>> GetSeriesAsync(CancellationToken cancellationToken = default) =>
        await GetJsonAsync("api/v3/series", Json.ListSeries, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Episode>> GetCalendarAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) =>
        await GetJsonAsync(
            $"api/v3/calendar?start={Iso(start)}&end={Iso(end)}&includeSeries=true&unmonitored=false",
            Json.ListEpisode,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<Episode>> GetMissingAsync(int pageSize, CancellationToken cancellationToken = default) =>
        GetJsonAsync(
            $"api/v3/wanted/missing?page=1&pageSize={pageSize}&sortKey=episodes.airDateUtc&sortDirection=descending&monitored=true&includeSeries=true",
            Json.PagedResultEpisode,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<Episode>> GetCutoffUnmetAsync(int pageSize, CancellationToken cancellationToken = default) =>
        GetJsonAsync(
            $"api/v3/wanted/cutoff?page=1&pageSize={pageSize}&sortKey=episodes.airDateUtc&sortDirection=descending&monitored=true&includeSeries=true",
            Json.PagedResultEpisode,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<QueueItem>> GetQueueAsync(int pageSize, CancellationToken cancellationToken = default) =>
        GetJsonAsync(
            $"api/v3/queue?page=1&pageSize={pageSize}&includeSeries=true&includeEpisode=true",
            Json.PagedResultQueueItem,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) =>
        await GetJsonAsync($"api/v3/episode?seriesId={seriesId}&seasonNumber={seasonNumber}", Json.ListEpisode, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RootFolder>> GetRootFoldersAsync(CancellationToken cancellationToken = default) =>
        await GetJsonAsync("api/v3/rootfolder", Json.ListRootFolder, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The file name is fixed here, never taken from the caller, so the proxy endpoint can only ever reach
    /// Sonarr's poster route. Non-image responses and oversized bodies are refused so an upstream error page
    /// can't be served from Nightshift's origin.
    /// </remarks>
    public async Task<PosterImage?> GetPosterAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync($"api/v3/mediacover/{seriesId}/poster-250.jpg", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new SonarrException(SonarrFailure.BadResponse, "Sonarr returned something other than an image for a poster.");
        }

        if (response.Content.Headers.ContentLength > MaxPosterBytes)
        {
            throw new SonarrException(SonarrFailure.BadResponse, "Sonarr returned a poster larger than the size limit.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxPosterBytes)
            {
                throw new SonarrException(SonarrFailure.BadResponse, "Sonarr returned a poster larger than the size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return new PosterImage(buffer.ToArray(), contentType);
    }

    private async Task<T> GetJsonAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(path, cancellationToken);
        EnsureSuccess(response);

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync(body, typeInfo, cancellationToken)
                ?? throw new SonarrException(SonarrFailure.BadResponse, "Sonarr returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new SonarrException(SonarrFailure.BadResponse, "Sonarr returned data Nightshift couldn't read. Check that it's Sonarr v4.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SonarrException(SonarrFailure.Timeout, "Sonarr took too long to answer.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SonarrException(SonarrFailure.Unreachable, "Couldn't reach Sonarr. Check the URL and that Sonarr is running.", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is >= 200 and < 300)
        {
            return;
        }

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new SonarrException(SonarrFailure.Unauthorized, "Sonarr rejected the API key."),
            // Redirects are never followed: a custom X-Api-Key header would be re-sent to the new host.
            _ when status is >= 300 and < 400 =>
                new SonarrException(SonarrFailure.BadResponse, "Sonarr answered with a redirect. Set the base URL to Sonarr's final address, including any URL base."),
            _ => new SonarrException(SonarrFailure.BadResponse, $"Sonarr answered with HTTP {status}."),
        };
    }

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}

/// <summary>Registers <see cref="ISonarrClient"/> against a real Sonarr.</summary>
public static class SonarrServiceCollectionExtensions
{
    /// <summary>
    /// Adds the typed Sonarr client and validates <see cref="SonarrOptions"/> at startup.
    /// The handler never follows redirects, so the API key header can't be forwarded to another host.
    /// </summary>
    public static IServiceCollection AddSonarrClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SonarrOptions>()
            .Bind(configuration.GetSection(SonarrOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.BaseUrl is { IsAbsoluteUri: true } u && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps),
                "Sonarr:BaseUrl must be an absolute http or https URL.")
            .ValidateOnStart();

        services.AddHttpClient<ISonarrClient, SonarrClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<SonarrOptions>>().Value;
                var baseUrl = options.BaseUrl!.ToString();
                http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                http.Timeout = options.Timeout;
                http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(["X-Api-Key"]);

        return services;
    }
}
