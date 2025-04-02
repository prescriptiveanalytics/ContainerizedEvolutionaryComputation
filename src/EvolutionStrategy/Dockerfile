FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR .

# Copy everything
COPY "EvolutionStrategy/*.csproj" "EvolutionStrategy/"
COPY "Common/*.csproj" "Common/"

# Restore as distinct layers
RUN dotnet restore "EvolutionStrategy/EvolutionStrategy.csproj"

# Build and publish a release
COPY "EvolutionStrategy/" "EvolutionStrategy/"
COPY "Common/" "Common/"
WORKDIR /EvolutionStrategy
RUN dotnet publish -c Release -o out
COPY EvolutionStrategy/configurations /EvolutionStrategy/out/configurations

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /EvolutionStrategy
COPY --from=build-env /EvolutionStrategy/out .
ENTRYPOINT ["dotnet", "CEAL.EvolutionStrategy.dll"]