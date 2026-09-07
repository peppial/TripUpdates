FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY TripUpdates/TripUpdates.csproj TripUpdates/
RUN dotnet restore TripUpdates/TripUpdates.csproj
COPY TripUpdates/ TripUpdates/
RUN dotnet publish TripUpdates/TripUpdates.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

# The 19 MB static GTFS feed is cached here. Mount a volume to survive restarts;
# without one it is simply re-downloaded on boot.
ENV TripUpdates__Feed__CacheDirectory=/app/cache
RUN mkdir -p /app/cache

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TripUpdates.dll"]
