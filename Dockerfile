# --- Стадия сборки ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Сначала только csproj/props — для кэша restore.
COPY Directory.Build.props ./
COPY src/SellerTools.Core/SellerTools.Core.csproj      src/SellerTools.Core/
COPY src/SellerTools.UzumApi/SellerTools.UzumApi.csproj src/SellerTools.UzumApi/
COPY src/SellerTools.App/SellerTools.App.csproj         src/SellerTools.App/
RUN dotnet restore src/SellerTools.App/SellerTools.App.csproj

# Остальной код и публикация.
COPY src/ src/
RUN dotnet publish src/SellerTools.App/SellerTools.App.csproj \
    -c Release -o /app --no-restore

# --- Стадия рантайма ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl нужен для healthcheck контейнера (в базовом образе его нет).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# У нас InvariantGlobalization=true — культуры не нужны, образ легче/стабильнее.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    ASPNETCORE_URLS=http://+:8080

COPY --from=build /app ./

# Каталог логов (том монтируется сюда в compose).
RUN mkdir -p /app/logs

EXPOSE 8080

# Режим по умолчанию — bot (см. Program.cs).
ENTRYPOINT ["dotnet", "SellerTools.App.dll", "bot"]
