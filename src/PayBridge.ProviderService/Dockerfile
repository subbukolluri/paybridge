FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PayBridge.ProviderService/PayBridge.ProviderService.csproj src/PayBridge.ProviderService/
RUN dotnet restore src/PayBridge.ProviderService/PayBridge.ProviderService.csproj

COPY src/PayBridge.ProviderService/ src/PayBridge.ProviderService/
RUN dotnet build src/PayBridge.ProviderService/PayBridge.ProviderService.csproj -c Release -o /app/build

FROM build AS publish
RUN dotnet publish src/PayBridge.ProviderService/PayBridge.ProviderService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PayBridge.ProviderService.dll"]
