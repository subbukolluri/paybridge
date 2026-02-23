FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PayBridge.Logging/PayBridge.Logging.csproj PayBridge.Logging/
COPY src/PayBridge.FraudService/PayBridge.FraudService.csproj src/PayBridge.FraudService/
COPY protos/ protos/
RUN dotnet restore src/PayBridge.FraudService/PayBridge.FraudService.csproj

COPY PayBridge.Logging/ PayBridge.Logging/
COPY src/PayBridge.FraudService/ src/PayBridge.FraudService/
RUN dotnet build src/PayBridge.FraudService/PayBridge.FraudService.csproj -c Release -o /app/build

FROM build AS publish
RUN dotnet publish src/PayBridge.FraudService/PayBridge.FraudService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PayBridge.FraudService.dll"]
