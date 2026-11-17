# Build and publish with the SDK image, then run on the smaller ASP.NET runtime image as a non-root user.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Nightshift.slnx ./
COPY src/Nightshift.Sonarr/Nightshift.Sonarr.csproj src/Nightshift.Sonarr/
COPY src/Nightshift.Web/Nightshift.Web.csproj src/Nightshift.Web/
RUN dotnet restore src/Nightshift.Web/Nightshift.Web.csproj
COPY src/ src/
RUN dotnet publish src/Nightshift.Web/Nightshift.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nightshift.Web.dll"]
