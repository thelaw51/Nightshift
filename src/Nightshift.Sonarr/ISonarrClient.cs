namespace Nightshift.Sonarr;

/// <summary>
/// The read-only slice of Sonarr's /api/v3 that Nightshift uses. Implementations throw
/// <see cref="SonarrException"/> for any failure the UI should explain.
/// </summary>
public interface ISonarrClient
{
    /// <summary>All series in the library, with season statistics.</summary>
    Task<IReadOnlyList<Series>> GetSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Monitored episodes airing between <paramref name="start"/> and <paramref name="end"/>, with their series.</summary>
    Task<IReadOnlyList<Episode>> GetCalendarAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);

    /// <summary>Monitored, aired episodes with no file, newest first.</summary>
    Task<PagedResult<Episode>> GetMissingAsync(int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Monitored episodes whose file is below the quality profile's cutoff, newest first.</summary>
    Task<PagedResult<Episode>> GetCutoffUnmetAsync(int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The download queue, with series and episode details.</summary>
    Task<PagedResult<QueueItem>> GetQueueAsync(int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Every episode of one season of a series.</summary>
    Task<IReadOnlyList<Episode>> GetEpisodesAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default);

    /// <summary>Configured root folders and their free space.</summary>
    Task<IReadOnlyList<RootFolder>> GetRootFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>The series' poster, or <c>null</c> if Sonarr has none.</summary>
    Task<PosterImage?> GetPosterAsync(int seriesId, CancellationToken cancellationToken = default);
}
