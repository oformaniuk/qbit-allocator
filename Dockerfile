FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY QbitAllocator.slnx ./
COPY Directory.Build.props ./
COPY src/QbitAllocator/QbitAllocator.csproj src/QbitAllocator/
RUN dotnet restore src/QbitAllocator/QbitAllocator.csproj

COPY src/QbitAllocator/ src/QbitAllocator/
RUN dotnet publish src/QbitAllocator/QbitAllocator.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
ARG BUILD_DATE
ARG VCS_REF
ARG SOURCE_REPOSITORY_URL=""

WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
EXPOSE 8080
LABEL org.opencontainers.image.title="qbit-allocator" \
      org.opencontainers.image.description="qBittorrent-compatible proxy for allocating new downloads across disk roots" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.source="${SOURCE_REPOSITORY_URL}" \
      org.opencontainers.image.documentation="${SOURCE_REPOSITORY_URL}#readme" \
      org.opencontainers.image.licenses="MIT"
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "QbitAllocator.dll"]
