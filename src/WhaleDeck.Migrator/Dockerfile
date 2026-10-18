ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.13@sha256:939555726cae4c916d212323ac3c0792996dcac2614f00e82cc40fcf059e1af3
FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src
ARG WHALEDECK_BUILD_REVISION
RUN test "${#WHALEDECK_BUILD_REVISION}" -eq 40 && ! printf '%s' "$WHALEDECK_BUILD_REVISION" | grep -q '[^a-f0-9]'
COPY Directory.Build.props global.json WhaleDeck.slnx ./
COPY src/WhaleDeck.Domain/WhaleDeck.Domain.csproj src/WhaleDeck.Domain/
COPY src/WhaleDeck.Application/WhaleDeck.Application.csproj src/WhaleDeck.Application/
COPY src/WhaleDeck.Contracts/WhaleDeck.Contracts.csproj src/WhaleDeck.Contracts/
COPY src/WhaleDeck.Infrastructure/WhaleDeck.Infrastructure.csproj src/WhaleDeck.Infrastructure/
COPY src/WhaleDeck.Migrator/WhaleDeck.Migrator.csproj src/WhaleDeck.Migrator/
RUN dotnet restore src/WhaleDeck.Migrator/WhaleDeck.Migrator.csproj
COPY src/ src/
RUN dotnet publish src/WhaleDeck.Migrator/WhaleDeck.Migrator.csproj -c Release -o /app --no-restore -p:SourceRevisionId="$WHALEDECK_BUILD_REVISION"

FROM ${DOTNET_RUNTIME_IMAGE} AS final
WORKDIR /app
ARG WHALEDECK_BUILD_REVISION
LABEL org.opencontainers.image.revision="$WHALEDECK_BUILD_REVISION" \
      org.opencontainers.image.source="https://github.com/leng125172-code/whale-deck" \
      org.opencontainers.image.version="0.1.0"
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "WhaleDeck.Migrator.dll"]
