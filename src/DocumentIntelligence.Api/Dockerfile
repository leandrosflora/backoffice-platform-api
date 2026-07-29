# Build from the repository root: docker build -f src/DocumentIntelligence.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
WORKDIR /src

COPY src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj src/DocumentIntelligence.Api/
RUN dotnet restore src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj

COPY src/DocumentIntelligence.Api/ src/DocumentIntelligence.Api/
RUN dotnet publish src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1fa23fc4872d95fd71c2833ebe65d7e84a43b2d51a31d119516852f13d9505a7
RUN groupadd --gid 10001 docintel \
    && useradd --uid 10001 --gid docintel --shell /usr/sbin/nologin --no-create-home docintel
WORKDIR /app
COPY --from=build /app/publish .
USER 10001:10001
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocumentIntelligence.Api.dll"]
