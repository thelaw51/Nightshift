using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nightshift.Sonarr;
using Nightshift.Web;
using Nightshift.Web.Library;
using Nightshift.Web.Sample;

namespace Nightshift.Tests;

/// <summary>End-to-end composition of the Library view, using the sample client as a stand-in Sonarr.</summary>
public sealed class LibraryServiceTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

    private static LibraryService Service(ISonarrClient sonarr) =>
        new(sonarr, new MemoryCache(new MemoryCacheOptions()), Clock,
            Options.Create(new NightshiftOptions { TimeZone = "Europe/London" }));

    [Fact]
    public async Task Composes_the_mockup_library_from_sample_data()
    {
        var view = await Service(new SampleSonarrClient(Clock)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Slow Horses", "The Last of Us", "Fargo"], view.Attention.Select(c => c.Title));
        Assert.Equal(3, view.AttentionTotal);
        Assert.Equal(
            [StripSegment.Have, StripSegment.Have, StripSegment.Missing, StripSegment.Missing, StripSegment.Have, StripSegment.Unaired],
            view.Attention[0].Strip);
        Assert.Equal(15, view.SeriesCount);
        Assert.Equal(12, view.Shelf.Total);
        Assert.DoesNotContain(view.Shelf.Items, s => s.Title is "Slow Horses" or "The Last of Us" or "Fargo");
        Assert.Equal(new NavCounts(3, 14), view.Nav);
        Assert.Equal(4, view.Week.Days.Sum(d => d.Airings.Count));
    }

    [Fact]
    public async Task Reuses_the_composed_view_while_fresh()
    {
        var counting = new CountingClient(new SampleSonarrClient(Clock));
        var service = Service(counting);

        await service.GetAsync(TestContext.Current.CancellationToken);
        await service.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, counting.SeriesCalls);
    }

    /// <summary>Counts series fetches so caching can be observed.</summary>
    private sealed class CountingClient(ISonarrClient inner) : ISonarrClient
    {
        public int SeriesCalls { get; private set; }

        public Task<IReadOnlyList<Series>> GetSeriesAsync(CancellationToken cancellationToken = default)
        {
            SeriesCalls++;
            return inner.GetSeriesAsync(cancellationToken);
        }

        public Task<IReadOnlyList<Episode>> GetCalendarAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) => inner.GetCalendarAsync(start, end, cancellationToken);
        public Task<PagedResult<Episode>> GetMissingAsync(int pageSize, CancellationToken cancellationToken = default) => inner.GetMissingAsync(pageSize, cancellationToken);
        public Task<PagedResult<Episode>> GetCutoffUnmetAsync(int pageSize, CancellationToken cancellationToken = default) => inner.GetCutoffUnmetAsync(pageSize, cancellationToken);
        public Task<PagedResult<QueueItem>> GetQueueAsync(int pageSize, CancellationToken cancellationToken = default) => inner.GetQueueAsync(pageSize, cancellationToken);
        public Task<IReadOnlyList<Episode>> GetEpisodesAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) => inner.GetEpisodesAsync(seriesId, seasonNumber, cancellationToken);
        public Task<IReadOnlyList<RootFolder>> GetRootFoldersAsync(CancellationToken cancellationToken = default) => inner.GetRootFoldersAsync(cancellationToken);
        public Task<PosterImage?> GetPosterAsync(int seriesId, CancellationToken cancellationToken = default) => inner.GetPosterAsync(seriesId, cancellationToken);
    }
}
