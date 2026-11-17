using System.Globalization;
using Nightshift.Sonarr;

namespace Nightshift.Web.Library;

/// <summary>A series that needs attention, before its episode strip has been fetched.</summary>
/// <param name="SeasonNumber">The season whose strip the card shows; <c>null</c> for download cards.</param>
/// <param name="Id">Unique across all candidates; a series can have several stuck downloads.</param>
/// <param name="SortKey">Most recent relevant date; newer problems sort first.</param>
public sealed record AttentionCandidate(
    string Id,
    int SeriesId,
    string Title,
    AttentionKind Kind,
    int? SeasonNumber,
    string Summary,
    DateTimeOffset SortKey,
    int? ProgressPercent,
    string PrimaryAction,
    string SecondaryAction);

/// <summary>
/// Pure functions that turn raw Sonarr payloads into the Library view. Kept free of I/O and clocks
/// (callers pass <c>now</c> and the zone) so every rule here is unit-testable.
/// </summary>
public static class LibraryComposer
{
    /// <summary>Most cards the attention stack shows; also bounds the per-series episode fetches.</summary>
    public const int MaxAttentionCards = 12;

    /// <summary>Most posters on the shelf before it collapses into "+N more".</summary>
    public const int MaxShelfItems = 12;

    private static readonly HashSet<string> ProblemQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "warning", "failed", "downloadClientUnavailable",
    };

    /// <summary>
    /// Returns the Monday that starts the week containing <paramref name="now"/> in <paramref name="zone"/>,
    /// plus the UTC instants that bound that week for the calendar query.
    /// </summary>
    public static (DateOnly Monday, DateTimeOffset StartUtc, DateTimeOffset EndUtc) WeekBounds(DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return (monday, LocalMidnightUtc(monday, zone), LocalMidnightUtc(monday.AddDays(7), zone));
    }

    /// <summary>Buckets calendar episodes into the seven days of the week, in the configured zone.</summary>
    public static WeekView BuildWeek(IEnumerable<Episode> calendar, DateTimeOffset now, TimeZoneInfo zone)
    {
        var (monday, _, _) = WeekBounds(now, zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        var byDay = calendar
            .Where(e => e.AirDateUtc is not null)
            .Select(e => (Episode: e, Local: TimeZoneInfo.ConvertTime(e.AirDateUtc!.Value, zone)))
            .GroupBy(x => DateOnly.FromDateTime(x.Local.DateTime))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Local).ToList());

        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = monday.AddDays(offset);
            var airings = byDay.TryGetValue(date, out var list)
                ? list.Select(x => new Airing(
                        x.Episode.SeriesId,
                        x.Episode.Series?.Title ?? "Unknown series",
                        EpisodeCode(x.Episode.SeasonNumber, x.Episode.EpisodeNumber),
                        x.Local.ToString("HH:mm", CultureInfo.InvariantCulture),
                        FinaleNote(x.Episode.FinaleType)))
                    .ToList()
                : [];
            return new WeekDay(date, date == today, airings);
        }).ToList();

        return new WeekView(monday, monday.AddDays(6), days);
    }

    /// <summary>
    /// Groups missing episodes, cutoff-unmet episodes and unhealthy downloads into one candidate per problem,
    /// newest first. Missing and cutoff problems on the same series share a card.
    /// </summary>
    public static List<AttentionCandidate> FindAttention(
        IEnumerable<Episode> missing,
        IEnumerable<Episode> cutoffUnmet,
        IEnumerable<QueueItem> queue,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        var cutoffBySeries = cutoffUnmet.GroupBy(e => e.SeriesId).ToDictionary(g => g.Key, g => g.ToList());
        var candidates = new List<AttentionCandidate>();

        foreach (var group in missing.GroupBy(e => e.SeriesId))
        {
            var episodes = group.ToList();
            var latest = episodes.MaxBy(e => e.AirDateUtc ?? DateTimeOffset.MinValue)!;
            var season = latest.SeasonNumber;
            var inSeason = episodes.Where(e => e.SeasonNumber == season).Select(e => e.EpisodeNumber).ToList();
            var otherSeasons = episodes.Count - inSeason.Count;

            var summary = $"{EpisodeRange(season, inSeason)}{(otherSeasons > 0 ? $" +{otherSeasons} more" : "")} missing";
            if (cutoffBySeries.Remove(group.Key, out var cutoff))
            {
                summary += $" · cutoff unmet on {CutoffLabel(cutoff, season)}";
            }

            if (latest.AirDateUtc is { } aired)
            {
                summary += $" · aired {RelativeDay(aired, now, zone)}";
            }

            candidates.Add(new AttentionCandidate(
                $"missing-{group.Key}",
                group.Key,
                latest.Series?.Title ?? "Unknown series",
                AttentionKind.Missing,
                season,
                summary,
                latest.AirDateUtc ?? DateTimeOffset.MinValue,
                null,
                episodes.Count switch { 1 => "Search", 2 => "Search both", _ => "Search all" },
                "Interactive…"));
        }

        foreach (var (seriesId, episodes) in cutoffBySeries)
        {
            var latest = episodes.MaxBy(e => e.AirDateUtc ?? DateTimeOffset.MinValue)!;
            candidates.Add(new AttentionCandidate(
                $"cutoff-{seriesId}",
                seriesId,
                latest.Series?.Title ?? "Unknown series",
                AttentionKind.CutoffUnmet,
                latest.SeasonNumber,
                $"Cutoff unmet on {CutoffLabel(episodes, null)}",
                latest.AirDateUtc ?? DateTimeOffset.MinValue,
                null,
                "Search",
                "Interactive…"));
        }

        var ordered = candidates.OrderByDescending(c => c.SortKey).ToList();

        // Downloads go after episode problems: they usually resolve on their own, so they are the least urgent.
        foreach (var item in queue.Where(IsUnhealthy).OrderByDescending(q => q.Added))
        {
            var progress = item.Size > 0 ? (int)Math.Round((item.Size - item.Sizeleft) / item.Size * 100) : 0;
            ordered.Add(new AttentionCandidate(
                $"download-{item.Id}",
                item.SeriesId ?? 0,
                item.Series?.Title ?? item.Title ?? "Unknown download",
                AttentionKind.Download,
                null,
                $"{DownloadHeadline(item)} · {progress}%{DownloadDetail(item)}",
                item.Added ?? DateTimeOffset.MinValue,
                progress,
                "Retry",
                "Blocklist & search"));
        }

        return ordered;
    }

    /// <summary>
    /// Turns one season's episodes into strip ticks: on disk, aired without a file, or not aired yet.
    /// </summary>
    public static List<StripSegment> BuildStrip(IEnumerable<Episode> seasonEpisodes, DateTimeOffset now) =>
        seasonEpisodes
            .Where(e => e.EpisodeNumber > 0)
            .OrderBy(e => e.EpisodeNumber)
            .Select(e => e.HasFile ? StripSegment.Have
                : e.AirDateUtc is { } aired && aired <= now ? StripSegment.Missing
                : StripSegment.Unaired)
            .ToList();

    /// <summary>Builds the poster shelf from series not already on the attention board, most recently aired first.</summary>
    public static ShelfView BuildShelf(IEnumerable<Series> series, IReadOnlySet<int> attentionSeriesIds)
    {
        var upToDate = series
            .Where(s => !attentionSeriesIds.Contains(s.Id))
            .OrderByDescending(s => s.PreviousAiring ?? DateTimeOffset.MinValue)
            .ThenBy(s => s.SortTitle ?? s.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = upToDate.Take(MaxShelfItems).Select(s => new ShelfItem(s.Id, s.Title, ShortCode(s.Title))).ToList();
        return new ShelfView(items, upToDate.Count, upToDate.Count - items.Count);
    }

    /// <summary>Free and total space across root folders, counting each volume once.</summary>
    public static DiskSpace? Disk(IEnumerable<RootFolder> folders)
    {
        var volumes = folders
            .Where(f => f is { Accessible: true, FreeSpace: not null })
            .DistinctBy(f => (f.FreeSpace, f.TotalSpace))
            .ToList();
        return volumes.Count == 0
            ? null
            : new DiskSpace(volumes.Sum(f => f.FreeSpace!.Value), volumes.Sum(f => f.TotalSpace ?? 0));
    }

    /// <summary>
    /// Formats episode numbers within one season: <c>S5E3</c>, <c>S5E3–E4</c>, <c>S5E3, E5</c>,
    /// or <c>S5 · 6 episodes</c> when the list is long and broken up.
    /// </summary>
    public static string EpisodeRange(int season, IEnumerable<int> episodeNumbers)
    {
        var numbers = episodeNumbers.Distinct().Order().ToList();
        if (numbers.Count == 0)
        {
            return $"S{season}";
        }

        if (numbers.Count == 1)
        {
            return EpisodeCode(season, numbers[0]);
        }

        var contiguous = numbers[^1] - numbers[0] == numbers.Count - 1;
        if (contiguous)
        {
            return $"{EpisodeCode(season, numbers[0])}–E{numbers[^1]}";
        }

        return numbers.Count <= 3
            ? $"{EpisodeCode(season, numbers[0])}, {string.Join(", ", numbers.Skip(1).Select(n => $"E{n}"))}"
            : $"S{season} · {numbers.Count} episodes";
    }

    /// <summary>A short poster label for when there's no artwork: a short word is kept, a long one clipped, several become initials.</summary>
    public static string ShortCode(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var significant = words.Length > 1 && words[0].Equals("The", StringComparison.OrdinalIgnoreCase) ? words[1..] : words;

        if (significant.Length == 1)
        {
            // Short words fit whole ("SHŌGUN"); longer ones become a clipped stem ("SEV") rather than a cut-off word.
            var word = new string(significant[0].Where(char.IsLetterOrDigit).ToArray());
            return (word.Length <= 6 ? word : word[..3]).ToUpperInvariant();
        }

        return new string(words.Select(w => w.FirstOrDefault(char.IsLetterOrDigit)).Where(c => c != default).Take(4).ToArray())
            .ToUpperInvariant();
    }

    /// <summary>Formats a size in bytes as e.g. <c>6.2 TB</c>, using binary units like Sonarr does.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    private static string EpisodeCode(int season, int episode) => $"S{season}E{episode}";

    private static string? FinaleNote(string? finaleType) => finaleType?.ToLowerInvariant() switch
    {
        "season" => "season finale",
        "series" => "series finale",
        "midseason" => "midseason finale",
        _ => null,
    };

    private static string CutoffLabel(List<Episode> episodes, int? preferSeason)
    {
        var season = preferSeason ?? episodes.MaxBy(e => e.AirDateUtc ?? DateTimeOffset.MinValue)!.SeasonNumber;
        var inSeason = episodes.Where(e => e.SeasonNumber == season).Select(e => e.EpisodeNumber).ToList();
        if (inSeason.Count == 0)
        {
            return $"{episodes.Count} episode{(episodes.Count == 1 ? "" : "s")}";
        }

        // On a shared card the season is already named, so drop the S prefix: "cutoff unmet on E1".
        var range = EpisodeRange(season, inSeason);
        return preferSeason is not null && range.StartsWith($"S{season}E", StringComparison.Ordinal)
            ? range[$"S{season}".Length..]
            : range;
    }

    private static string RelativeDay(DateTimeOffset aired, DateTimeOffset now, TimeZoneInfo zone)
    {
        var airedDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(aired, zone).DateTime);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var days = today.DayNumber - airedDay.DayNumber;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 7 => $"{days} days ago",
            < 14 => "last week",
            < 60 => $"{days / 7} weeks ago",
            _ => airedDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    private static bool IsUnhealthy(QueueItem item) =>
        string.Equals(item.TrackedDownloadStatus, "warning", StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.TrackedDownloadStatus, "error", StringComparison.OrdinalIgnoreCase)
        || (item.Status is not null && ProblemQueueStatuses.Contains(item.Status));

    private static string DownloadHeadline(QueueItem item) => item switch
    {
        { Status: var s } when string.Equals(s, "failed", StringComparison.OrdinalIgnoreCase) => "Download failed",
        { TrackedDownloadState: var s } when string.Equals(s, "importBlocked", StringComparison.OrdinalIgnoreCase) => "Import blocked",
        { Status: var s } when string.Equals(s, "downloadClientUnavailable", StringComparison.OrdinalIgnoreCase) => "Download client unavailable",
        { TrackedDownloadStatus: var s } when string.Equals(s, "error", StringComparison.OrdinalIgnoreCase) => "Download error",
        _ => "Download stalled",
    };

    private static string DownloadDetail(QueueItem item)
    {
        var message = item.StatusMessages?.SelectMany(m => m.Messages ?? []).FirstOrDefault() ?? item.ErrorMessage;
        return string.IsNullOrWhiteSpace(message) ? "" : $" · {message}";
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Midnight can fall inside a DST gap in a few zones; step forward to the first valid local time.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
