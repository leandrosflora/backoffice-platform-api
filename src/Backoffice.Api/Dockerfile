# Build from the repository root: docker build -f src/Backoffice.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Backoffice.Api/Backoffice.Api.csproj src/Backoffice.Api/
COPY src/Backoffice.Application/Backoffice.Application.csproj src/Backoffice.Application/
COPY src/Backoffice.Domain/Backoffice.Domain.csproj src/Backoffice.Domain/
COPY src/Backoffice.Infrastructure/Backoffice.Infrastructure.csproj src/Backoffice.Infrastructure/
RUN dotnet restore src/Backoffice.Api/Backoffice.Api.csproj

COPY src/Backoffice.Api/ src/Backoffice.Api/
COPY src/Backoffice.Application/ src/Backoffice.Application/
COPY src/Backoffice.Domain/ src/Backoffice.Domain/
COPY src/Backoffice.Infrastructure/ src/Backoffice.Infrastructure/
RUN dotnet publish src/Backoffice.Api/Backoffice.Api.csproj -c Release --no-restore -o /app/publish

# Debian-based (not chiseled/Alpine): NSec.Cryptography (libsodium) and Confluent.Kafka
# (librdkafka) ship glibc-linked native binaries, which chiseled/musl-based images can't
# load — matching design.md's open question on base-image choice for these native deps.
FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN groupadd --gid 10001 backoffice \
    && useradd --uid 10001 --gid backoffice --shell /usr/sbin/nologin --no-create-home backoffice
WORKDIR /app
COPY --from=build /app/publish .
USER 10001:10001
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Backoffice.Api.dll"]
