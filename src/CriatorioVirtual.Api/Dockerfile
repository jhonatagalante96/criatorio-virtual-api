FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["src/CriatorioVirtual.Api/CriatorioVirtual.Api.csproj", "src/CriatorioVirtual.Api/"]
COPY ["src/CriatorioVirtual.Application/CriatorioVirtual.Application.csproj", "src/CriatorioVirtual.Application/"]
COPY ["src/CriatorioVirtual.Domain/CriatorioVirtual.Domain.csproj", "src/CriatorioVirtual.Domain/"]
COPY ["src/CriatorioVirtual.Infrastructure/CriatorioVirtual.Infrastructure.csproj", "src/CriatorioVirtual.Infrastructure/"]
COPY ["src/CriatorioVirtual.Migrations/CriatorioVirtual.Migrations.csproj", "src/CriatorioVirtual.Migrations/"]
COPY ["tools/PrivateStorageMigration/PrivateStorageMigration.csproj", "tools/PrivateStorageMigration/"]

RUN dotnet restore "src/CriatorioVirtual.Api/CriatorioVirtual.Api.csproj"
RUN dotnet restore "src/CriatorioVirtual.Migrations/CriatorioVirtual.Migrations.csproj"
RUN dotnet restore "tools/PrivateStorageMigration/PrivateStorageMigration.csproj"

COPY . .
WORKDIR "/src/src/CriatorioVirtual.Api"
RUN dotnet publish --configuration Release --output /app/publish --no-restore /p:UseAppHost=false
RUN dotnet publish "../CriatorioVirtual.Migrations/CriatorioVirtual.Migrations.csproj" \
    --configuration Release \
    --output /app/migrations \
    --no-restore \
    /p:UseAppHost=false
RUN dotnet publish "/src/tools/PrivateStorageMigration/PrivateStorageMigration.csproj" \
    --configuration Release \
    --output /app/private-storage-migration \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates wget \
    && wget --no-verbose --output-document=/tmp/google-chrome-stable.deb https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
    && cd /tmp \
    && apt-get install -y --no-install-recommends ./google-chrome-stable.deb \
    && command -v google-chrome-stable \
    && test -x /usr/bin/google-chrome-stable \
    && google-chrome-stable --version \
    && rm -f /tmp/google-chrome-stable.deb \
    && rm -rf /var/lib/apt/lists/*
ENV DOCUMENT_RENDERING_CHROMIUM_PATH=/usr/bin/google-chrome-stable

EXPOSE 8080

COPY --from=build /app/publish .
COPY --from=build /app/migrations ./migrations
COPY --from=build /app/private-storage-migration ./tools/PrivateStorageMigration
USER $APP_UID

ENTRYPOINT ["dotnet", "CriatorioVirtual.Api.dll"]
