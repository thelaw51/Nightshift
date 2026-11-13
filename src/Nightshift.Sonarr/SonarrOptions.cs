using System.ComponentModel.DataAnnotations;

namespace Nightshift.Sonarr;

/// <summary>
/// Connection settings for the Sonarr instance, bound from the <c>Sonarr</c> configuration section.
/// The API key grants full admin access to Sonarr, so it must only come from user-secrets or environment variables.
/// </summary>
public sealed class SonarrOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Sonarr";

    /// <summary>Sonarr's root URL, e.g. <c>http://sonarr:8989</c>. Include any URL base Sonarr is configured with.</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>The API key from Sonarr's Settings › General page.</summary>
    [Required]
    [MinLength(20)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>How long a single Sonarr request may take before it is abandoned.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
