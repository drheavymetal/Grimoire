# Build context is the repository root. Run on demand, e.g. `... run worker seed`.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/shared/GrimoireLibrary/GrimoireLibrary.csproj src/shared/GrimoireLibrary/
COPY src/console/server/Grimoire.Worker.csproj src/console/server/
RUN dotnet restore src/console/server/Grimoire.Worker.csproj

COPY src/shared/ src/shared/
COPY src/console/server/ src/console/server/
RUN dotnet publish src/console/server/Grimoire.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Grimoire.Worker.dll"]
