# Build context is the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/shared/GrimoireLibrary/GrimoireLibrary.csproj src/shared/GrimoireLibrary/
COPY src/web/server/Grimoire.Server.csproj src/web/server/
RUN dotnet restore src/web/server/Grimoire.Server.csproj

COPY src/shared/ src/shared/
COPY src/web/server/ src/web/server/
RUN dotnet publish src/web/server/Grimoire.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# appsettings.json carries a base "Urls": "http://localhost:5080" for local dev, and
# that config key wins over the ASPNETCORE_URLS/URLS environment variable. A command-line
# switch is the only layer that overrides it, so bind the container explicitly here —
# otherwise the process listens on localhost:5080 and is unreachable behind Traefik.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Grimoire.Server.dll", "--urls", "http://0.0.0.0:8080"]
