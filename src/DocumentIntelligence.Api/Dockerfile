# Build from the repository root: docker build -f src/DocumentIntelligence.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj src/DocumentIntelligence.Api/
RUN dotnet restore src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj

COPY src/DocumentIntelligence.Api/ src/DocumentIntelligence.Api/
RUN dotnet publish src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN groupadd --gid 10001 docintel \
    && useradd --uid 10001 --gid docintel --shell /usr/sbin/nologin --no-create-home docintel
WORKDIR /app
COPY --from=build /app/publish .
USER 10001:10001
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocumentIntelligence.Api.dll"]
