using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Nightshift.Sonarr;

namespace Nightshift.Tests;

/// <summary>An <see cref="HttpMessageHandler"/> that records requests and answers from a callback.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    /// <summary>Every request sent through the handler, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}

/// <summary>Helpers for building responses and a client wired through the real DI registration.</summary>
public static class TestHttp
{
    /// <summary>A JSON response read from a file under Fixtures/.</summary>
    public static HttpResponseMessage Fixture(string name) => Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    /// <summary>A 200 JSON response.</summary>
    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Builds <see cref="ISonarrClient"/> via <c>AddSonarrClient</c>, swapping only the network handler.</summary>
    public static ISonarrClient Client(StubHandler handler, string baseUrl = "http://sonarr.local:8989", string apiKey = "0123456789abcdef0123456789abcdef")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sonarr:BaseUrl"] = baseUrl,
                ["Sonarr:ApiKey"] = apiKey,
            })
            .Build();

        var services = new ServiceCollection().AddLogging();
        services.AddSonarrClient(configuration);
        services.AddHttpClient<ISonarrClient, SonarrClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<ISonarrClient>();
    }
}
