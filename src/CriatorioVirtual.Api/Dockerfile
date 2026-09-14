FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["src/CriatorioVirtual.Api/CriatorioVirtual.Api.csproj", "src/CriatorioVirtual.Api/"]
COPY ["src/CriatorioVirtual.Application/CriatorioVirtual.Application.csproj", "src/CriatorioVirtual.Application/"]
COPY ["src/CriatorioVirtual.Domain/CriatorioVirtual.Domain.csproj", "src/CriatorioVirtual.Domain/"]
COPY ["src/CriatorioVirtual.Infrastructure/CriatorioVirtual.Infrastructure.csproj", "src/CriatorioVirtual.Infrastructure/"]
COPY ["src/CriatorioVirtual.Migrations/CriatorioVirtual.Migrations.csproj", "src/CriatorioVirtual.Migrations/"]

RUN dotnet restore "src/CriatorioVirtual.Api/CriatorioVirtual.Api.csproj"
RUN dotnet restore "src/CriatorioVirtual.Migrations/CriatorioVirtual.Migrations.csproj"

COPY . .
WORKDIR "/src/src/CriatorioVirtual.Api"
RUN dotnet publish --configuration Release --output /app/publish --no-restore /p:UseAppHost=false
RUN dotnet publish "../CriatorioVirtual.Migrations/CriatorioVirtual.Migrations.csproj" \
    --configuration Release \
    --output /app/migrations \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends chromium \
    && rm -rf /var/lib/apt/lists/*
ENV DOCUMENT_RENDERING_CHROMIUM_PATH=/usr/bin/chromium

EXPOSE 8080

RUN apt-get update \
    && apt-get install -y --no-install-recommends chromium \
    && rm -rf /var/lib/apt/lists/*

ENV DOCUMENT_RENDERING_CHROMIUM_PATH=/usr/bin/chromium

COPY --from=build /app/publish .
COPY --from=build /app/migrations ./migrations
USER $APP_UID

ENTRYPOINT ["dotnet", "CriatorioVirtual.Api.dll"]
