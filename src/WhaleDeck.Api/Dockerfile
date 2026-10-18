ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src
ARG WHALEDECK_BUILD_REVISION
RUN test "${#WHALEDECK_BUILD_REVISION}" -eq 40 && ! printf '%s' "$WHALEDECK_BUILD_REVISION" | grep -q '[^a-f0-9]'

COPY ["Directory.Build.props", "."]
COPY ["global.json", "."]
COPY ["src/WhaleDeck.Domain/WhaleDeck.Domain.csproj", "src/WhaleDeck.Domain/"]
COPY ["src/WhaleDeck.Application/WhaleDeck.Application.csproj", "src/WhaleDeck.Application/"]
COPY ["src/WhaleDeck.Contracts/WhaleDeck.Contracts.csproj", "src/WhaleDeck.Contracts/"]
COPY ["src/WhaleDeck.Contracts/Protos/agent.proto", "src/WhaleDeck.Contracts/Protos/"]
COPY ["src/WhaleDeck.Infrastructure/WhaleDeck.Infrastructure.csproj", "src/WhaleDeck.Infrastructure/"]
COPY ["src/WhaleDeck.Api/WhaleDeck.Api.csproj", "src/WhaleDeck.Api/"]
RUN dotnet restore "src/WhaleDeck.Api/WhaleDeck.Api.csproj"

COPY src/ src/
RUN dotnet publish "src/WhaleDeck.Api/WhaleDeck.Api.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    -p:SourceRevisionId="$WHALEDECK_BUILD_REVISION"

FROM ${ASPNET_IMAGE} AS final
WORKDIR /app
ARG WHALEDECK_BUILD_REVISION
LABEL org.opencontainers.image.revision="$WHALEDECK_BUILD_REVISION" \
      org.opencontainers.image.source="https://github.com/leng125172-code/whale-deck" \
      org.opencontainers.image.version="0.1.0"
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN install -d -o "$APP_UID" -g "$APP_UID" -m 0700 /var/lib/whaledeck/data-protection
USER $APP_UID
ENTRYPOINT ["dotnet", "WhaleDeck.Api.dll"]
