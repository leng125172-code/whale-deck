ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.13@sha256:939555726cae4c916d212323ac3c0792996dcac2614f00e82cc40fcf059e1af3

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["global.json", "."]
COPY ["src/WhaleDeck.Domain/WhaleDeck.Domain.csproj", "src/WhaleDeck.Domain/"]
COPY ["src/WhaleDeck.Application/WhaleDeck.Application.csproj", "src/WhaleDeck.Application/"]
COPY ["src/WhaleDeck.Contracts/WhaleDeck.Contracts.csproj", "src/WhaleDeck.Contracts/"]
COPY ["src/WhaleDeck.Contracts/Protos/agent.proto", "src/WhaleDeck.Contracts/Protos/"]
COPY ["src/WhaleDeck.Infrastructure/WhaleDeck.Infrastructure.csproj", "src/WhaleDeck.Infrastructure/"]
COPY ["src/WhaleDeck.Worker/WhaleDeck.Worker.csproj", "src/WhaleDeck.Worker/"]
RUN dotnet restore "src/WhaleDeck.Worker/WhaleDeck.Worker.csproj"

COPY src/ src/
RUN dotnet publish "src/WhaleDeck.Worker/WhaleDeck.Worker.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS final
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "WhaleDeck.Worker.dll"]
