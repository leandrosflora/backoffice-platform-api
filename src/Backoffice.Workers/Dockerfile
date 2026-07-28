# Build from the repository root: docker build -f src/Backoffice.Workers/Dockerfile .
# One image serves all three worker deployables (outbox dispatcher, workflow consumer, timer
# firing) plus the single-process "all" role, selected at runtime via the Worker__Role
# environment variable (see Program.cs) — matching the reference Python sample's pattern of
# reusing one build for outbox-publisher/workflow-worker/timer-worker with a different
# `command` per container (spec: platform-deployment, task 12.1).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Backoffice.Workers/Backoffice.Workers.csproj src/Backoffice.Workers/
COPY src/Backoffice.Application/Backoffice.Application.csproj src/Backoffice.Application/
COPY src/Backoffice.Domain/Backoffice.Domain.csproj src/Backoffice.Domain/
COPY src/Backoffice.Infrastructure/Backoffice.Infrastructure.csproj src/Backoffice.Infrastructure/
RUN dotnet restore src/Backoffice.Workers/Backoffice.Workers.csproj

COPY src/Backoffice.Workers/ src/Backoffice.Workers/
COPY src/Backoffice.Application/ src/Backoffice.Application/
COPY src/Backoffice.Domain/ src/Backoffice.Domain/
COPY src/Backoffice.Infrastructure/ src/Backoffice.Infrastructure/
RUN dotnet publish src/Backoffice.Workers/Backoffice.Workers.csproj -c Release --no-restore -o /app/publish

# Debian-based (not chiseled/Alpine) for the same native-dependency reason as the Api image.
# Needs the ASP.NET Core shared framework, not just the base runtime, because
# Backoffice.Infrastructure references OpenTelemetry's ASP.NET Core instrumentation package
# (shared with the Api project), which pulls in a Microsoft.AspNetCore.App FrameworkReference
# even though this worker process itself never listens on HTTP.
FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN groupadd --gid 10001 backoffice \
    && useradd --uid 10001 --gid backoffice --shell /usr/sbin/nologin --no-create-home backoffice
WORKDIR /app
COPY --from=build /app/publish .
USER 10001:10001
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Backoffice.Workers.dll"]
