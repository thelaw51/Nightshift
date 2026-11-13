using Nightshift.Sonarr;
using Nightshift.Web.Library;

namespace Nightshift.Tests;

/// <summary>The rules that turn Sonarr data into the Library view.</summary>
public sealed class LibraryComposerTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static Series Show(int id, string title, DateTimeOffset? previousAiring = null) =>
        new(id, title, null, "continuing", true, 2020, previousAiring, null, null, null);

    private static Episode Ep(int seriesId, int season, int number, DateTimeOffset? aired, bool hasFile = false, string? finale = null, string title = "Show") =>
        new(seriesId * 1000 + number, seriesId, season, number, null, aired, hasFile, true, finale, Show(seriesId, title));

    [Fact]
    public void Week_starts_on_the_local_monday_not_the_utc_one()
    {
        // Sunday 23:30 UTC in July is already Monday 00:30 in London.
        var now = new DateTimeOffset(2026, 7, 12, 23, 30, 0, TimeSpan.Zero);

        var (monday, startUtc, endUtc) = LibraryComposer.WeekBounds(now, London);

        Assert.Equal(new DateOnly(2026, 7, 13), monday);
        Assert.Equal(new DateTimeOffset(2026, 7, 12, 23, 0, 0, TimeSpan.Zero), startUtc);
        Assert.Equal(TimeSpan.FromDays(7), endUtc - startUtc);
    }

    [Fact]
    public void Airings_are_bucketed_by_local_day_with_finale_notes()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Episode[] calendar =
        [
            Ep(1, 5, 6, new DateTimeOffset(2026, 7, 14, 23, 30, 0, TimeSpan.Zero), finale: "season", title: "Slow Horses"),
            Ep(2, 3, 9, new DateTimeOffset(2026, 7, 17, 2, 0, 0, TimeSpan.Zero), title: "Foundation"),
        ];

        var week = LibraryComposer.BuildWeek(calendar, now, London);

        Assert.Equal(7, week.Days.Count);
        Assert.True(week.Days[1].IsToday);
        var wednesday = Assert.Single(week.Days[2].Airings);
        Assert.Equal(("Slow Horses", "S5E6", "00:30", "season finale"),
            (wednesday.SeriesTitle, wednesday.EpisodeCode, wednesday.LocalTime, wednesday.Note));
        Assert.Equal("03:00", Assert.Single(week.Days[4].Airings).LocalTime);
        Assert.Empty(week.Days[1].Airings);
    }

    [Theory]
    [InlineData(new[] { 3 }, "S5E3")]
    [InlineData(new[] { 4, 3 }, "S5E3–E4")]
    [InlineData(new[] { 3, 5 }, "S5E3, E5")]
    [InlineData(new[] { 1, 3, 5, 7 }, "S5 · 4 episodes")]
    public void Episode_ranges_read_naturally(int[] episodes, string expected) =>
        Assert.Equal(expected, LibraryComposer.EpisodeRange(5, episodes));

    [Fact]
    public void Missing_and_cutoff_on_one_series_share_a_card()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Episode[] missing = [Ep(2, 2, 4, now.AddDays(-9), title: "The Last of Us")];
        Episode[] cutoff = [Ep(2, 2, 1, now.AddDays(-30), hasFile: true, title: "The Last of Us")];

        var candidate = Assert.Single(LibraryComposer.FindAttention(missing, cutoff, [], now, London));

        Assert.Equal(AttentionKind.Missing, candidate.Kind);
        Assert.Equal("S2E4 missing · cutoff unmet on E1 · aired last week", candidate.Summary);
        Assert.Equal(2, candidate.SeasonNumber);
        Assert.Equal("Search", candidate.PrimaryAction);
    }

    [Fact]
    public void Two_missing_episodes_offer_search_both_and_newest_problem_sorts_first()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Episode[] missing =
        [
            Ep(2, 2, 4, now.AddDays(-20), title: "Older"),
            Ep(1, 5, 3, now.AddDays(-2), title: "Newer"),
            Ep(1, 5, 4, now.AddDays(-2), title: "Newer"),
        ];

        var candidates = LibraryComposer.FindAttention(missing, [], [], now, London);

        Assert.Equal(["Newer", "Older"], candidates.Select(c => c.Title));
        Assert.Equal("S5E3–E4 missing · aired 2 days ago", candidates[0].Summary);
        Assert.Equal("Search both", candidates[0].PrimaryAction);
    }

    [Fact]
    public void Only_unhealthy_downloads_need_attention()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        QueueItem[] queue =
        [
            new(1, 7, 1, "Fargo", 100, 86, "warning", "warning", "downloading",
                [new QueueStatusMessage("Fargo", ["Stalled, no connections"])], null, now, Show(7, "Fargo"), null),
            new(2, 9, 2, "Dark", 100, 50, "downloading", "ok", "downloading", [], null, now, Show(9, "Dark"), null),
            new(3, 4, 3, "Silo", 100, 0, "completed", "warning", "importBlocked", [], "Not a valid video file", now, Show(4, "Silo"), null),
        ];

        var candidates = LibraryComposer.FindAttention([], [], queue, now, London);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.Equal(AttentionKind.Download, c.Kind));
        Assert.Equal("Download stalled · 14% · Stalled, no connections", candidates.Single(c => c.Title == "Fargo").Summary);
        Assert.Equal("Import blocked · 100% · Not a valid video file", candidates.Single(c => c.Title == "Silo").Summary);
    }

    [Fact]
    public void Two_stuck_downloads_on_one_series_get_distinct_ids()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        QueueItem[] queue =
        [
            new(11, 7, 1, "Fargo E9", 100, 90, "warning", "warning", "downloading", [], null, now, Show(7, "Fargo"), null),
            new(12, 7, 2, "Fargo E10", 100, 90, "warning", "warning", "downloading", [], null, now, Show(7, "Fargo"), null),
            new(13, null, null, "Unknown.Release", 100, 90, "failed", "error", "failed", [], null, now, null, null),
            new(14, null, null, "Another.Unknown", 100, 90, "failed", "error", "failed", [], null, now, null, null),
        ];
        Episode[] missing = [Ep(7, 5, 3, now.AddDays(-1), title: "Fargo")];

        var ids = LibraryComposer.FindAttention(missing, [], queue, now, London).Select(c => c.Id).ToList();

        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Strip_marks_files_gaps_and_future_episodes()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Episode[] season =
        [
            Ep(1, 5, 3, now.AddDays(-7)),
            Ep(1, 5, 1, now.AddDays(-21), hasFile: true),
            Ep(1, 5, 0, now.AddDays(-30)), // specials-style episode 0 is not a tick
            Ep(1, 5, 2, now.AddDays(-14), hasFile: true),
            Ep(1, 5, 4, now.AddDays(7)),
            Ep(1, 5, 5, null),
        ];

        Assert.Equal(
            [StripSegment.Have, StripSegment.Have, StripSegment.Missing, StripSegment.Unaired, StripSegment.Unaired],
            LibraryComposer.BuildStrip(season, now));
    }

    [Fact]
    public void Shelf_excludes_attention_series_and_caps_with_a_count()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var series = Enumerable.Range(1, 20).Select(i => Show(i, $"Show {i}", now.AddDays(-i))).ToList();

        var shelf = LibraryComposer.BuildShelf(series, new HashSet<int> { 1, 2 });

        Assert.Equal(18, shelf.Total);
        Assert.Equal(LibraryComposer.MaxShelfItems, shelf.Items.Count);
        Assert.Equal(18 - LibraryComposer.MaxShelfItems, shelf.Hidden);
        Assert.Equal("Show 3", shelf.Items[0].Title);
    }

    [Theory]
    [InlineData("The Bear", "BEAR")]
    [InlineData("Severance", "SEV")]
    [InlineData("Shōgun", "SHŌGUN")]
    [InlineData("House of the Dragon", "HOTD")]
    [InlineData("The Last of Us", "TLOU")]
    [InlineData("Better Call Saul", "BCS")]
    [InlineData("Mr. Robot", "MR")]
    public void Short_codes_match_the_design(string title, string expected) =>
        Assert.Equal(expected, LibraryComposer.ShortCode(title));

    [Fact]
    public void Disk_counts_each_volume_once()
    {
        RootFolder[] folders =
        [
            new(1, "/tv", true, 500, 1000),
            new(2, "/tv/anime", true, 500, 1000),
            new(3, "/other", true, 200, 400),
            new(4, "/offline", false, 999, 999),
        ];

        Assert.Equal(new DiskSpace(700, 1400), LibraryComposer.Disk(folders));
    }

    [Theory]
    [InlineData(6_817_000_000_000L, "6.2 TB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(10L, "10 B")]
    public void Bytes_format_in_binary_units(long bytes, string expected) =>
        Assert.Equal(expected, LibraryComposer.FormatBytes(bytes));
}
