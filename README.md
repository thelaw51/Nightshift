# Nightshift

A different front end for [Sonarr](https://sonarr.tv). It talks to your existing Sonarr through its v3 API and leaves Sonarr itself untouched. Run it next to the stock UI and use whichever one suits the job.

The library screen sorts itself by what needs you. Missing episodes, files below your quality cutoff and stuck downloads sit at the top as cards. A week rail shows what airs this week. Everything that's fine collapses into a poster shelf underneath.

This first release is read-only. The Search, Retry and Blocklist buttons are visible but disabled, and they get wired up in the next release along with snooze and live updates.

## Requirements

- Sonarr v4 (the v3 API that v4 serves)
- .NET 10 SDK to build, or Docker to run

## Run it locally

Store your Sonarr address and API key with user-secrets so they never land in a file the repo tracks:

```bash
dotnet user-secrets --project src/Nightshift.Web set "Sonarr:BaseUrl" "http://192.168.1.10:8989"
```

```bash
dotnet user-secrets --project src/Nightshift.Web set "Sonarr:ApiKey" "<key from Sonarr › Settings › General>"
```

```bash
dotnet run --project src/Nightshift.Web
```

Include Sonarr's URL base in `BaseUrl` if you set one, e.g. `http://host:8989/sonarr`. Nightshift refuses to start if either setting is missing or the URL isn't http or https.

To try it without a Sonarr, turn on sample data. A banner stays on screen while it's on, and it never switches on by itself.

```bash
Nightshift__SampleData=true dotnet run --project src/Nightshift.Web
```

## Run it in Docker

Copy `docker-compose.example.yml` to `docker-compose.yml`, put `SONARR_API_KEY=...` in a `.env` file beside it, and set the network name to the one your Sonarr container uses. Then:

```bash
docker compose up -d --build
```

The container listens on 8080 as a non-root user.

## Settings

| Key | Default | What it does |
| --- | --- | --- |
| `Sonarr:BaseUrl` | none | Sonarr's address. Required. |
| `Sonarr:ApiKey` | none | Sonarr's API key. Required. Use user-secrets or an environment variable, never `appsettings.json`. |
| `Sonarr:Timeout` | `00:00:10` | How long one Sonarr request may take. |
| `Nightshift:TimeZone` | server zone | IANA zone for "today" and the week rail, e.g. `Europe/London`. In Docker you can set `TZ` instead. |
| `Nightshift:SampleData` | `false` | Show built-in fake shows and skip Sonarr. |

In environment variables, swap `:` for `__`, e.g. `Sonarr__ApiKey`.

## Security

Sonarr's API key is an admin key. Whoever holds it can change download clients, paths and settings, and delete series. Nightshift keeps it on the server. It goes out only as an `X-Api-Key` header on server-side requests, never in a URL, and the browser never receives it. Posters come through Nightshift's own `/posters/{id}` route for the same reason. The HTTP client doesn't follow redirects, so a misrouted Sonarr address can't forward the key to another host.

Nightshift has no login yet. Anyone who can reach its port can see your library. Keep it on your LAN, or put it behind a reverse proxy that handles authentication (Authelia, Authentik, your proxy's basic auth). Don't publish the port to the internet. Login or trusted forward-auth lands before any button that changes Sonarr gets turned on.

`/health` returns 200 when Sonarr answers with the configured key and 503 when it doesn't.

## Project layout

- `src/Nightshift.Sonarr` is the typed Sonarr API client and its DTOs. It has no UI code.
- `src/Nightshift.Web` is the Blazor Server app. `Library/LibraryComposer.cs` holds the rules that turn Sonarr data into the screen (grouping, week bucketing, episode strips) as pure functions. `Library/LibraryService.cs` does the fetching and caching.
- `tests/Nightshift.Tests` uses xUnit v3 with JSON fixtures shaped like Sonarr's OpenAPI spec.

```bash
dotnet test
```

## Design

The look comes from Nocturne, a dark design system built for this project: one blurple accent, Inter at weight 500, compact spacing. `wwwroot/css/nocturne.css` holds its tokens and components. Inter is self-hosted under the SIL Open Font License (`wwwroot/fonts/OFL.txt`), so the app makes no third-party requests.
