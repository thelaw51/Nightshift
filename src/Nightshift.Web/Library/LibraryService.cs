using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Nightshift.Sonarr;

namespace Nightshift.Web.Library;

/// <summary>
/// Loads the Library screen: fetches from Sonarr in parallel, fetches episode strips for the attention cards,
/// and composes the result. The composed view is cached briefly so reconnects and quick navigations
/// don't re-hit a Sonarr running on modest home hardware.
/// </summary>
public sealed class LibraryService(
    ISonarrClient sonarr,
    IMemoryCache cache,
    TimeProvider clock,
    IOptions<NightshiftOptions> options)
{
    /// <summary>How long a composed Library view is reused.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    /// <summary>Most concurrent per-series episode requests, so the strip fan-out can't flood Sonarr.</summary>
    public const int MaxConcurrentEpisodeRequests = 4;

    private const string CacheKey = "nightshift:library";
    private const int WantedPageSize = 100;
    private const int QueuePageSize = 100;

    /// <summary>Returns the Library view, from cache when it's fresh.</summary>
    public async Task<LibraryView> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out LibraryView? cached) && cached is not null)
        {
            return cached;
        }

        var view = await LoadAsync(cancellationToken);
        cache.Set(CacheKey, view, CacheDuration);
        return view;
    }

    private async Task<LibraryView> LoadAsync(CancellationToken cancellationToken)
    {
        var zone = options.Value.ResolveTimeZone();
        var now = clock.GetUtcNow();
        var (_, weekStart, weekEnd) = LibraryComposer.WeekBounds(now, zone);

        var seriesTask = sonarr.GetSeriesAsync(cancellationToken);
        var calendarTask = sonarr.GetCalendarAsync(weekStart, weekEnd, cancellationToken);
        var missingTask = sonarr.GetMissingAsync(WantedPageSize, cancellationToken);
        var cutoffTask = sonarr.GetCutoffUnmetAsync(WantedPageSize, cancellationToken);
        var queueTask = sonarr.GetQueueAsync(QueuePageSize, cancellationToken);
        var foldersTask = sonarr.GetRootFoldersAsync(cancellationToken);
        await Task.WhenAll(seriesTask, calendarTask, missingTask, cutoffTask, queueTask, foldersTask);

        var series = seriesTask.Result;
        var missing = missingTask.Result;
        var queue = queueTask.Result;

        var candidates = LibraryComposer.FindAttention(missing.Records, cutoffTask.Result.Records, queue.Records, now, zone);
        var cards = await BuildCardsAsync(candidates.Take(LibraryComposer.MaxAttentionCards).ToList(), now, cancellationToken);
        var attentionIds = candidates.Select(c => c.SeriesId).ToHashSet();

        return new LibraryView(
            LibraryComposer.BuildWeek(calendarTask.Result, now, zone),
            cards,
            attentionIds.Count,
            LibraryComposer.BuildShelf(series, attentionIds),
            series.Count,
            new NavCounts(queue.TotalRecords, missing.TotalRecords),
            LibraryComposer.Disk(foldersTask.Result));
    }

    private async Task<IReadOnlyList<AttentionCard>> BuildCardsAsync(
        List<AttentionCandidate> candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var strips = new Dictionary<(int SeriesId, int Season), List<StripSegment>>();
        var seasons = candidates
            .Where(c => c.SeasonNumber is not null && c.SeriesId > 0)
            .Select(c => (c.SeriesId, Season: c.SeasonNumber!.Value))
            .Distinct()
            .ToList();

        await Parallel.ForEachAsync(
            seasons,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentEpisodeRequests, CancellationToken = cancellationToken },
            async (key, ct) =>
            {
                var episodes = await sonarr.GetEpisodesAsync(key.SeriesId, key.Season, ct);
                var strip = LibraryComposer.BuildStrip(episodes, now);
                lock (strips)
                {
                    strips[key] = strip;
                }
            });

        return candidates
            .Select(c => new AttentionCard(
                c.Id,
                c.SeriesId,
                c.Title,
                c.Summary,
                c.Kind,
                c.SeasonNumber is { } season && strips.TryGetValue((c.SeriesId, season), out var strip) ? strip : null,
                c.ProgressPercent,
                c.PrimaryAction,
                c.SecondaryAction))
            .ToList();
    }
}
