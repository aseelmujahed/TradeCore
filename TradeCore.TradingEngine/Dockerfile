FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TradeCore.TradingEngine/TradeCore.TradingEngine.csproj TradeCore.TradingEngine/
COPY TradeCore.Console/TradeCore.Console.csproj TradeCore.Console/
COPY TradeCore.Messaging/TradeCore.Messaging.csproj TradeCore.Messaging/
RUN dotnet restore TradeCore.TradingEngine/TradeCore.TradingEngine.csproj

COPY TradeCore.TradingEngine/ TradeCore.TradingEngine/
COPY TradeCore.Console/ TradeCore.Console/
COPY TradeCore.Messaging/ TradeCore.Messaging/
RUN dotnet publish TradeCore.TradingEngine/TradeCore.TradingEngine.csproj --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "TradeCore.TradingEngine.dll"]
