FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PayBridge.Contracts/PayBridge.Contracts.csproj src/PayBridge.Contracts/
COPY src/PayBridge.SettlementConsumer/PayBridge.SettlementConsumer.csproj src/PayBridge.SettlementConsumer/
RUN dotnet restore src/PayBridge.SettlementConsumer/PayBridge.SettlementConsumer.csproj

COPY src/PayBridge.Contracts/ src/PayBridge.Contracts/
COPY src/PayBridge.SettlementConsumer/ src/PayBridge.SettlementConsumer/
RUN dotnet build src/PayBridge.SettlementConsumer/PayBridge.SettlementConsumer.csproj -c Release --no-restore

FROM build AS publish
RUN dotnet publish src/PayBridge.SettlementConsumer/PayBridge.SettlementConsumer.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PayBridge.SettlementConsumer.dll"]
