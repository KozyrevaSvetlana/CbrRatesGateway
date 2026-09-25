# syntax=docker/dockerfile:1

# ---------- restore + build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Сначала только файлы проектов — слой restore кэшируется, пока не меняются зависимости
COPY CbrRatesGateway.sln ./
COPY src/CbrRatesGateway.Api/CbrRatesGateway.Api.csproj src/CbrRatesGateway.Api/
COPY tests/CbrRatesGateway.Tests/CbrRatesGateway.Tests.csproj tests/CbrRatesGateway.Tests/
# Кэш NuGet живёт в кэше BuildKit между сборками — пакеты не скачиваются заново каждый раз.
# Монтируется во все шаги, которым нужны пакеты (restore/build/test/publish).
RUN --mount=type=cache,id=cbr-nuget,target=/root/.nuget/packages \
    dotnet restore CbrRatesGateway.sln

COPY . .
RUN --mount=type=cache,id=cbr-nuget,target=/root/.nuget/packages \
    dotnet build CbrRatesGateway.sln -c Release --no-restore

# ---------- test ----------
# Если хотя бы один тест упадёт, dotnet test вернёт ненулевой код и docker build остановится.
# RUN_TESTS=false передаёт Jenkins: там тесты уже прогнаны отдельной стадией (с отчётом JUnit),
# повторять их внутри docker build не нужно. При ручной сборке тесты запускаются по умолчанию.
FROM build AS test
ARG RUN_TESTS=true
RUN --mount=type=cache,id=cbr-nuget,target=/root/.nuget/packages \
    if [ "$RUN_TESTS" = "true" ]; then \
      dotnet test CbrRatesGateway.sln -c Release --no-build --verbosity normal; \
    else \
      echo "Тесты пропущены (RUN_TESTS=$RUN_TESTS)"; \
    fi

# ---------- publish ----------
# Наследуется от стадии test: BuildKit собирает только стадии, от которых зависит итоговый образ,
# поэтому без этой зависимости тесты были бы пропущены.
FROM test AS publish
RUN --mount=type=cache,id=cbr-nuget,target=/root/.nuget/packages \
    dotnet publish src/CbrRatesGateway.Api/CbrRatesGateway.Api.csproj \
      -c Release -o /app/publish --no-build /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ARG VERSION=dev
LABEL org.opencontainers.image.title="cbr-rates-gateway" \
      org.opencontainers.image.version="${VERSION}"

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

COPY --from=publish /app/publish .

# Непривилегированный пользователь, встроенный в образы .NET 8
USER $APP_UID

ENTRYPOINT ["dotnet", "CbrRatesGateway.Api.dll"]
