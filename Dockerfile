FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY global.json SendMeTheFuckingLog.csproj ./
RUN dotnet restore SendMeTheFuckingLog.csproj
COPY . .
RUN dotnet publish SendMeTheFuckingLog.csproj --configuration Release --no-restore \
    --self-contained false -p:UseAppHost=false -p:ServerGarbageCollection=false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app
COPY --from=build /out/ ./
COPY deployment/healthcheck.sh ./healthcheck.sh
RUN mkdir -p /app/data /app/tmp && chown 1000:1000 /app/data /app/tmp
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    HOME=/app TMPDIR=/app/tmp ASPNETCORE_TEMP=/app/tmp
USER 1000:1000
EXPOSE 8080 8443
HEALTHCHECK --interval=5s --timeout=3s --start-period=20s --retries=6 \
    CMD ["bash", "/app/healthcheck.sh"]
ENTRYPOINT ["dotnet", "/app/SendMeTheFuckingLog.dll"]
