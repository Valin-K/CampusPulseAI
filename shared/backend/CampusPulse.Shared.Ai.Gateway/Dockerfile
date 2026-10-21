FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["shared/backend/CampusPulse.Shared.Security/CampusPulse.Shared.Security.csproj", "shared/backend/CampusPulse.Shared.Security/"]
COPY ["shared/backend/CampusPulse.Shared.MultiAgent/CampusPulse.Shared.MultiAgent.csproj", "shared/backend/CampusPulse.Shared.MultiAgent/"]
COPY ["shared/backend/CampusPulse.Shared.Ai/CampusPulse.Shared.Ai.csproj", "shared/backend/CampusPulse.Shared.Ai/"]
COPY ["shared/backend/CampusPulse.Shared.Ai.Gateway/CampusPulse.Shared.Ai.Gateway.csproj", "shared/backend/CampusPulse.Shared.Ai.Gateway/"]
RUN dotnet restore "shared/backend/CampusPulse.Shared.Ai.Gateway/CampusPulse.Shared.Ai.Gateway.csproj"
COPY shared/backend/CampusPulse.Shared.Security shared/backend/CampusPulse.Shared.Security
COPY shared/backend/CampusPulse.Shared.MultiAgent shared/backend/CampusPulse.Shared.MultiAgent
COPY shared/backend/CampusPulse.Shared.Ai shared/backend/CampusPulse.Shared.Ai
COPY shared/backend/CampusPulse.Shared.Ai.Gateway shared/backend/CampusPulse.Shared.Ai.Gateway
RUN dotnet publish "shared/backend/CampusPulse.Shared.Ai.Gateway/CampusPulse.Shared.Ai.Gateway.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER $APP_UID
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CampusPulse.Shared.Ai.Gateway.dll"]
