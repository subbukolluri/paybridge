FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PayBridge.Contracts/PayBridge.Contracts.csproj src/PayBridge.Contracts/
COPY src/PayBridge.PaymentApi/PayBridge.PaymentApi.csproj src/PayBridge.PaymentApi/
RUN dotnet restore src/PayBridge.PaymentApi/PayBridge.PaymentApi.csproj

COPY src/PayBridge.Contracts/ src/PayBridge.Contracts/
COPY src/PayBridge.PaymentApi/ src/PayBridge.PaymentApi/
RUN dotnet build src/PayBridge.PaymentApi/PayBridge.PaymentApi.csproj -c Release -o /app/build

FROM build AS publish
RUN dotnet publish src/PayBridge.PaymentApi/PayBridge.PaymentApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PayBridge.PaymentApi.dll"]
