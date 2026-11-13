namespace Nightshift.Web.Library;

/// <summary>Everything the Library screen renders, composed from several Sonarr calls.</summary>
/// <param name="AttentionTotal">Distinct series needing action; can exceed <see cref="Attention"/>, which is capped.</param>
/// <param name="Disk">Space across root folder volumes, or <c>null</c> if Sonarr reported none.</param>
public sealed record LibraryView(
    WeekView Week,
    IReadOnlyList<AttentionCard> Attention,
    int AttentionTotal,
    ShelfView Shelf,
    int SeriesCount,
    NavCounts Nav,
    DiskSpace? Disk);

/// <summary>The Monday-to-Sunday week containing today, in the configured time zone.</summary>
public sealed record WeekView(DateOnly Start, DateOnly End, IReadOnlyList<WeekDay> Days);

/// <summary>One day on the week rail.</summary>
public sealed record WeekDay(DateOnly Date, bool IsToday, IReadOnlyList<Airing> Airings);

/// <summary>One episode airing on a given day.</summary>
/// <param name="EpisodeCode">e.g. <c>S5E6</c>.</param>
/// <param name="LocalTime">Air time in the configured zone, <c>HH:mm</c>.</param>
/// <param name="Note">Extra context such as <c>season finale</c>.</param>
public sealed record Airing(int SeriesId, string SeriesTitle, string EpisodeCode, string LocalTime, string? Note);

/// <summary>What kind of problem put a series on the attention board.</summary>
public enum AttentionKind
{
    /// <summary>Aired, monitored episodes with no file.</summary>
    Missing,

    /// <summary>Files below the quality profile's cutoff.</summary>
    CutoffUnmet,

    /// <summary>A queued download that Sonarr flagged with a warning or error.</summary>
    Download,
}

/// <summary>One card in the "Needs attention" stack.</summary>
/// <param name="Id">Unique per card, safe for render keys and element ids.</param>
/// <param name="Strip">The relevant season's episodes, or <c>null</c> for download cards.</param>
/// <param name="ProgressPercent">Download progress for download cards.</param>
public sealed record AttentionCard(
    string Id,
    int SeriesId,
    string Title,
    string Summary,
    AttentionKind Kind,
    IReadOnlyList<StripSegment>? Strip,
    int? ProgressPercent,
    string PrimaryAction,
    string SecondaryAction);

/// <summary>One tick on an episode strip.</summary>
public enum StripSegment
{
    /// <summary>The episode has a file on disk.</summary>
    Have,

    /// <summary>Aired with no file.</summary>
    Missing,

    /// <summary>Not aired yet, or no air date.</summary>
    Unaired,
}

/// <summary>The "Up to date" poster shelf.</summary>
/// <param name="Hidden">Series left off the shelf because of the display cap.</param>
public sealed record ShelfView(IReadOnlyList<ShelfItem> Items, int Total, int Hidden);

/// <summary>A poster on the shelf. <paramref name="Code"/> is the short label shown when there's no artwork.</summary>
public sealed record ShelfItem(int SeriesId, string Title, string Code);

/// <summary>Badge counts for the top navigation.</summary>
public sealed record NavCounts(int Activity, int Wanted);

/// <summary>Free and total bytes across the library's volumes.</summary>
public sealed record DiskSpace(long FreeBytes, long TotalBytes)
{
    /// <summary>Share of the volumes in use, 0–100.</summary>
    public int UsedPercent => TotalBytes <= 0 ? 0 : (int)Math.Round((TotalBytes - FreeBytes) * 100.0 / TotalBytes);
}
