# syntax=docker/dockerfile
ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy global.json + csproj trước để layer restore được cache
# COPY global.json ./
COPY LegacyVault.API/LegacyVault.API.csproj LegacyVault.API/
COPY LegacyVault.BLL/LegacyVault.BLL.csproj LegacyVault.BLL/
COPY LegacyVault.DAL/LegacyVault.DAL.csproj LegacyVault.DAL/
RUN dotnet restore LegacyVault.API/LegacyVault.API.csproj

# Copy source của 3 project, không kéo Tests/docs/database vào image
COPY LegacyVault.API/ LegacyVault.API/
COPY LegacyVault.BLL/ LegacyVault.BLL/
COPY LegacyVault.DAL/ LegacyVault.DAL/
RUN dotnet publish LegacyVault.API/LegacyVault.API.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LegacyVault.API.dll"]
