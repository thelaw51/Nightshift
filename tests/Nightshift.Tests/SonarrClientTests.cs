using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nightshift.Sonarr;

namespace Nightshift.Tests;

/// <summary>Behaviour of the Sonarr HTTP client, exercised through its real DI registration.</summary>
public sealed class SonarrClientTests
{
    [Fact]
    public async Task Sends_key_as_header_and_keeps_the_url_base()
    {
        var handler = new StubHandler(_ => TestHttp.Fixture("queue.json"));
        var client = TestHttp.Client(handler, baseUrl: "http://sonarr.local/sonarr");

        await client.GetQueueAsync(100, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/sonarr/api/v3/queue", request.RequestUri!.AbsolutePath);
        Assert.Equal("0123456789abcdef0123456789abcdef", Assert.Single(request.Headers.GetValues("X-Api-Key")));
        Assert.DoesNotContain("apikey", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Parses_queue_including_status_values_it_has_never_seen()
    {
        var client = TestHttp.Client(new StubHandler(_ => TestHttp.Fixture("queue.json")));

        var queue = await client.GetQueueAsync(100, TestContext.Current.CancellationToken);

        Assert.Equal(2, queue.TotalRecords);
        var stalled = queue.Records[0];
        Assert.Equal("Fargo", stalled.Series?.Title);
        Assert.Equal("The download is stalled with no connections", stalled.StatusMessages![0].Messages![0]);
        Assert.Equal("aStatusFromAFutureSonarr", queue.Records[1].Status);
        Assert.Null(queue.Records[1].Series);
    }

    [Fact]
    public async Task Parses_wanted_missing_page()
    {
        var client = TestHttp.Client(new StubHandler(_ => TestHttp.Fixture("missing.json")));

        var missing = await client.GetMissingAsync(100, TestContext.Current.CancellationToken);

        Assert.Equal(14, missing.TotalRecords);
        var episode = Assert.Single(missing.Records);
        Assert.Equal((5, 3), (episode.SeasonNumber, episode.EpisodeNumber));
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero), episode.AirDateUtc);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SonarrFailure.Unauthorized)]
    [InlineData(HttpStatusCode.Found, SonarrFailure.BadResponse)]
    [InlineData(HttpStatusCode.InternalServerError, SonarrFailure.BadResponse)]
    public async Task Maps_http_failures_to_explainable_errors(HttpStatusCode status, SonarrFailure expected)
    {
        var client = TestHttp.Client(new StubHandler(_ => new HttpResponseMessage(status)));

        var ex = await Assert.ThrowsAsync<SonarrException>(() => client.GetSeriesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expected, ex.Failure);
        Assert.DoesNotContain("0123456789abcdef", ex.Message);
    }

    [Fact]
    public async Task Unreachable_host_is_reported_as_unreachable()
    {
        var client = TestHttp.Client(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        var ex = await Assert.ThrowsAsync<SonarrException>(() => client.GetSeriesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SonarrFailure.Unreachable, ex.Failure);
    }

    [Fact]
    public async Task Poster_refuses_non_image_content()
    {
        var client = TestHttp.Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>login</html>", System.Text.Encoding.UTF8, "text/html"),
        }));

        var ex = await Assert.ThrowsAsync<SonarrException>(() => client.GetPosterAsync(3, TestContext.Current.CancellationToken));

        Assert.Equal(SonarrFailure.BadResponse, ex.Failure);
    }

    [Fact]
    public async Task Poster_requests_only_the_fixed_poster_file()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) { Headers = { ContentType = new("image/jpeg") } },
        });
        var client = TestHttp.Client(handler);

        var poster = await client.GetPosterAsync(3, TestContext.Current.CancellationToken);

        Assert.Equal("image/jpeg", poster!.ContentType);
        Assert.Equal("/api/v3/mediacover/3/poster-250.jpg", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Missing_poster_returns_null()
    {
        var client = TestHttp.Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await client.GetPosterAsync(3, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, "0123456789abcdef0123456789abcdef")]
    [InlineData("ftp://sonarr.local", "0123456789abcdef0123456789abcdef")]
    [InlineData("http://sonarr.local", "")]
    public void Invalid_options_fail_validation(string? baseUrl, string apiKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sonarr:BaseUrl"] = baseUrl, ["Sonarr:ApiKey"] = apiKey })
            .Build();
        var provider = new ServiceCollection().AddLogging().AddSonarrClient(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SonarrOptions>>().Value);
    }
}
