namespace Nightshift.Sonarr;

// DTOs mirror the field names in Sonarr's /api/v3 OpenAPI spec (camelCase on the wire).
// Only the fields Nightshift reads are declared; everything else is ignored on deserialisation.
// Enum-like fields stay strings so a value added in a newer Sonarr never breaks parsing.

/// <summary>One page of a paged Sonarr endpoint such as <c>wanted/missing</c> or <c>queue</c>.</summary>
public sealed record PagedResult<T>(int Page, int PageSize, int TotalRecords, IReadOnlyList<T> Records);

/// <summary>A series as returned by <c>/api/v3/series</c>.</summary>
public sealed record Series(
    int Id,
    string Title,
    string? SortTitle,
    string? Status,
    bool Monitored,
    int? Year,
    DateTimeOffset? PreviousAiring,
    DateTimeOffset? NextAiring,
    IReadOnlyList<Season>? Seasons,
    SeriesStatistics? Statistics);

/// <summary>A season within a <see cref="Series"/>.</summary>
public sealed record Season(int SeasonNumber, bool Monitored, SeasonStatistics? Statistics);

/// <summary>Per-season file counts.</summary>
public sealed record SeasonStatistics(int EpisodeFileCount, int EpisodeCount, int TotalEpisodeCount);

/// <summary>Whole-series file counts.</summary>
public sealed record SeriesStatistics(int SeasonCount, int EpisodeFileCount, int EpisodeCount, int TotalEpisodeCount, long SizeOnDisk);

/// <summary>An episode from <c>/api/v3/episode</c>, <c>calendar</c> or the <c>wanted</c> endpoints.</summary>
/// <param name="FinaleType">Sonarr v4 sets this to <c>season</c>, <c>series</c> or <c>midseason</c> on finale episodes.</param>
public sealed record Episode(
    int Id,
    int SeriesId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? AirDateUtc,
    bool HasFile,
    bool Monitored,
    string? FinaleType,
    Series? Series);

/// <summary>A download in Sonarr's queue.</summary>
/// <param name="Status">Download client status, e.g. <c>downloading</c>, <c>warning</c>, <c>failed</c>.</param>
/// <param name="TrackedDownloadStatus">Sonarr's health verdict: <c>ok</c>, <c>warning</c> or <c>error</c>.</param>
/// <param name="TrackedDownloadState">Where Sonarr is in handling it, e.g. <c>downloading</c>, <c>importBlocked</c>.</param>
public sealed record QueueItem(
    int Id,
    int? SeriesId,
    int? EpisodeId,
    string? Title,
    double Size,
    double Sizeleft,
    string? Status,
    string? TrackedDownloadStatus,
    string? TrackedDownloadState,
    IReadOnlyList<QueueStatusMessage>? StatusMessages,
    string? ErrorMessage,
    DateTimeOffset? Added,
    Series? Series,
    Episode? Episode);

/// <summary>A warning or error Sonarr attached to a queue item.</summary>
public sealed record QueueStatusMessage(string? Title, IReadOnlyList<string>? Messages);

/// <summary>A root folder and the free space on its volume.</summary>
public sealed record RootFolder(int Id, string Path, bool Accessible, long? FreeSpace, long? TotalSpace);

/// <summary>A poster image fetched from Sonarr, already size-checked and buffered.</summary>
public sealed record PosterImage(byte[] Content, string ContentType);
