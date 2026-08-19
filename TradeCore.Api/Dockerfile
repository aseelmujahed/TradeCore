FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TradeCore.Api/TradeCore.Api.csproj TradeCore.Api/
COPY TradeCore.Console/TradeCore.Console.csproj TradeCore.Console/
COPY TradeCore.Messaging/TradeCore.Messaging.csproj TradeCore.Messaging/
RUN dotnet restore TradeCore.Api/TradeCore.Api.csproj

COPY TradeCore.Api/ TradeCore.Api/
COPY TradeCore.Console/ TradeCore.Console/
COPY TradeCore.Messaging/ TradeCore.Messaging/
RUN dotnet publish TradeCore.Api/TradeCore.Api.csproj --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TradeCore.Api.dll"]
