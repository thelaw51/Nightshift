using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nightshift.Sonarr;

namespace Nightshift.Web;

/// <summary>Reports whether Sonarr answers with the configured key, using its cheapest endpoint.</summary>
public sealed class SonarrHealthCheck(ISonarrClient sonarr) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await sonarr.GetRootFoldersAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (SonarrException ex)
        {
            return HealthCheckResult.Unhealthy(ex.Failure.ToString());
        }
    }
}
