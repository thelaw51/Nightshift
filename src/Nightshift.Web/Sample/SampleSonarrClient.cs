using Nightshift.Sonarr;

namespace Nightshift.Web.Sample;

/// <summary>
/// Fake Sonarr data matching the design mockups, with air dates relative to the current week.
/// Only used when <see cref="NightshiftOptions.SampleData"/> is explicitly switched on.
/// </summary>
public sealed class SampleSonarrClient(TimeProvider clock) : ISonarrClient
{
    private static readonly string[] Titles =
    [
        "Slow Horses", "The Last of Us", "Fargo", "Severance", "The Bear", "House of the Dragon", "Shōgun",
        "Foundation", "Dark", "Silo", "Andor", "Better Call Saul", "True Detective", "Mr. Robot", "Succession",
    ];

    private static Series Show(int id) => new(id, Titles[id - 1], null, "continuing", true, 2020, null, null, null, null);

    private DateTimeOffset Monday
    {
        get
        {
            var today = clock.GetUtcNow().UtcDateTime.Date;
            return new DateTimeOffset(today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Series>> GetSeriesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        IReadOnlyList<Series> series = Titles
            .Select((_, i) => Show(i + 1) with { PreviousAiring = now.AddDays(-(i * 3)) })
            .ToList();
        return Task.FromResult(series);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Episode>> GetCalendarAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
    {
        var monday = Monday;
        IReadOnlyList<Episode> episodes =
        [
            Ep(1, 5, 6, monday.AddDays(2).AddHours(9), hasFile: false, finale: "season"),
            Ep(8, 3, 9, monday.AddDays(4).AddHours(3), hasFile: false),
            Ep(5, 4, 4, monday.AddDays(4).AddHours(21), hasFile: false),
            Ep(10, 3, 10, monday.AddDays(6).AddHours(3), hasFile: false, finale: "season"),
        ];
        return Task.FromResult(episodes);
    }

    /// <inheritdoc />
    public Task<PagedResult<Episode>> GetMissingAsync(int pageSize, CancellationToken cancellationToken = default)
    {
        var lastWeek = Monday.AddDays(-5).AddHours(9);
        List<Episode> records =
        [
            Ep(1, 5, 3, lastWeek, hasFile: false),
            Ep(1, 5, 4, lastWeek.AddHours(1), hasFile: false),
            Ep(2, 2, 4, lastWeek.AddDays(-9), hasFile: false),
        ];
        return Task.FromResult(new PagedResult<Episode>(1, pageSize, 14, records));
    }

    /// <inheritdoc />
    public Task<PagedResult<Episode>> GetCutoffUnmetAsync(int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<Episode>(1, pageSize, 1, [Ep(2, 2, 1, Monday.AddDays(-35), hasFile: true)]));

    /// <inheritdoc />
    public Task<PagedResult<QueueItem>> GetQueueAsync(int pageSize, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        List<QueueItem> records =
        [
            new(1, 3, 301, "Fargo.S05E10.1080p", 4_000_000_000, 3_440_000_000, "warning", "warning", "downloading",
                [new QueueStatusMessage("Fargo.S05E10.1080p", ["The download is stalled with no connections"])],
                null, now.AddDays(-2), Show(3), null),
            new(2, 9, 901, "Dark.S03E08.2160p", 9_000_000_000, 2_000_000_000, "downloading", "ok", "downloading",
                [], null, now.AddHours(-1), Show(9), null),
            new(3, 11, 1101, "Andor.S02E12.1080p", 3_000_000_000, 0, "completed", "ok", "importPending",
                [], null, now.AddMinutes(-20), Show(11), null),
        ];
        return Task.FromResult(new PagedResult<QueueItem>(1, pageSize, records.Count, records));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default)
    {
        var monday = Monday;
        IReadOnlyList<Episode> episodes = (seriesId, seasonNumber) switch
        {
            (1, 5) =>
            [
                Ep(1, 5, 1, monday.AddDays(-19), true), Ep(1, 5, 2, monday.AddDays(-12), true),
                Ep(1, 5, 3, monday.AddDays(-5), false), Ep(1, 5, 4, monday.AddDays(-5), false),
                Ep(1, 5, 5, monday.AddDays(-1), true), Ep(1, 5, 6, monday.AddDays(2), false, "season"),
            ],
            (2, 2) => Enumerable.Range(1, 7)
                .Select(n => Ep(2, 2, n, monday.AddDays(-56 + n * 7), hasFile: n != 4))
                .ToList(),
            _ => [],
        };
        return Task.FromResult(episodes);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RootFolder>> GetRootFoldersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RootFolder>>([new RootFolder(1, "/tv", true, 6_817_000_000_000, 16_000_000_000_000)]);

    /// <inheritdoc />
    public Task<PosterImage?> GetPosterAsync(int seriesId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PosterImage?>(null);

    private static Episode Ep(int seriesId, int season, int episode, DateTimeOffset aired, bool hasFile, string? finale = null) =>
        new(seriesId * 1000 + season * 100 + episode, seriesId, season, episode, null, aired, hasFile, true, finale, Show(seriesId));
}
