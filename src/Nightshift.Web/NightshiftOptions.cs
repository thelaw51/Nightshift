namespace Nightshift.Web;

/// <summary>App-level settings, bound from the <c>Nightshift</c> configuration section.</summary>
public sealed class NightshiftOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Nightshift";

    /// <summary>
    /// Serve built-in fake shows instead of calling Sonarr. Off by default and never switched on automatically,
    /// so a misconfigured deploy fails loudly instead of showing fake data. The UI shows a banner while it's on.
    /// </summary>
    public bool SampleData { get; set; }

    /// <summary>
    /// IANA time zone for "today" and the week rail, e.g. <c>Europe/London</c>. When empty, the server's local
    /// zone is used, which in Docker is UTC unless the <c>TZ</c> environment variable is set.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>Resolves <see cref="TimeZone"/>, falling back to the server's local zone.</summary>
    public TimeZoneInfo ResolveTimeZone() =>
        string.IsNullOrWhiteSpace(TimeZone) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
}
