FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/DbScanner.ScannerWorker/DbScanner.ScannerWorker.csproj", "src/DbScanner.ScannerWorker/"]
COPY ["src/DbScanner.Application/DbScanner.Application.csproj", "src/DbScanner.Application/"]
COPY ["src/DbScanner.Infrastructure/DbScanner.Infrastructure.csproj", "src/DbScanner.Infrastructure/"]
COPY ["src/DbScanner.Contracts/DbScanner.Contracts.csproj", "src/DbScanner.Contracts/"]
COPY ["src/DbScanner.Domain/DbScanner.Domain.csproj", "src/DbScanner.Domain/"]

RUN dotnet restore "src/DbScanner.ScannerWorker/DbScanner.ScannerWorker.csproj" \
    --disable-parallel

COPY . .
WORKDIR /src/src/DbScanner.ScannerWorker
RUN dotnet publish "DbScanner.ScannerWorker.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends nmap libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DbScanner.ScannerWorker.dll"]
