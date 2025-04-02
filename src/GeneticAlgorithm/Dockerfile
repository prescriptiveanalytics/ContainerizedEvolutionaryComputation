FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR .

# Copy everything
COPY "GeneticAlgorithm/*.csproj" "GeneticAlgorithm/"
COPY "Common/*.csproj" "Common/"

# Restore as distinct layers
RUN dotnet restore "GeneticAlgorithm/GeneticAlgorithm.csproj"

# Build and publish a release
COPY "GeneticAlgorithm/" "GeneticAlgorithm/"
COPY "Common/" "Common/"
WORKDIR /GeneticAlgorithm
RUN dotnet publish -c Release -o out
COPY GeneticAlgorithm/configurations /GeneticAlgorithm/out/configurations

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /GeneticAlgorithm
COPY --from=build-env /GeneticAlgorithm/out .
ENTRYPOINT ["dotnet", "CEAL.GeneticAlgorithm.dll"]