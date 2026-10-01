# syntax=docker/dockerfile:1

# ---- Build ----
# The SDK runs on the build machine's own architecture and cross-publishes for the target
# (amd64 or arm64), so `docker buildx build --platform linux/arm64` works without emulating the SDK.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, in its own layer, so it's cached until the project file changes.
COPY api/api.csproj api/
RUN dotnet restore api/api.csproj -a $TARGETARCH

COPY api/ api/
RUN dotnet publish api/api.csproj -c Release -a $TARGETARCH --no-restore -o /app/publish

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# libSkiaSharp needs fontconfig. The test page asks for "Arial", which fontconfig maps to the
# metric-compatible Liberation Sans.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# The databases (app_dbs/) and stored print documents (app_files/) live on volumes. The folders are
# created here, owned by the non-root `app` user, so fresh named volumes inherit that ownership.
RUN mkdir -p /app/app_dbs /app/app_files \
    && chown -R $APP_UID /app/app_dbs /app/app_files

USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "api.dll"]
