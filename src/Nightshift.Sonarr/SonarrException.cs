namespace Nightshift.Sonarr;

/// <summary>Why a Sonarr call failed, in terms the UI can explain to a person.</summary>
public enum SonarrFailure
{
    /// <summary>The host could not be reached or refused the connection.</summary>
    Unreachable,

    /// <summary>The request took longer than <see cref="SonarrOptions.Timeout"/>.</summary>
    Timeout,

    /// <summary>Sonarr rejected the API key.</summary>
    Unauthorized,

    /// <summary>Sonarr answered with a redirect, an error status or a body that could not be parsed.</summary>
    BadResponse,
}

/// <summary>
/// A failed Sonarr call. <see cref="Exception.Message"/> is safe to show in the UI:
/// it never contains the API key, request headers or upstream response bodies.
/// </summary>
public sealed class SonarrException(SonarrFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The category of failure.</summary>
    public SonarrFailure Failure { get; } = failure;
}
