FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["src/CriatorioVirtual.Api/CriatorioVirtual.Api.csproj", "src/CriatorioVirtual.Api/"]
COPY ["src/CriatorioVirtual.Application/CriatorioVirtual.Application.csproj", "src/CriatorioVirtual.Application/"]
COPY ["src/CriatorioVirtual.Domain/CriatorioVirtual.Domain.csproj", "src/CriatorioVirtual.Domain/"]
COPY ["src/CriatorioVirtual.Infrastructure/CriatorioVirtual.Infrastructure.csproj", "src/CriatorioVirtual.Infrastructure/"]

RUN dotnet restore "src/CriatorioVirtual.Api/CriatorioVirtual.Api.csproj"

COPY . .
WORKDIR "/src/src/CriatorioVirtual.Api"
RUN dotnet publish --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "CriatorioVirtual.Api.dll"]
