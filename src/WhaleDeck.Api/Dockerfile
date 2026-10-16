ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.300
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

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
    /p:UseAppHost=false

FROM ${ASPNET_IMAGE} AS final
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN install -d -o "$APP_UID" -g "$APP_UID" -m 0700 /var/lib/whaledeck/data-protection
USER $APP_UID
ENTRYPOINT ["dotnet", "WhaleDeck.Api.dll"]
