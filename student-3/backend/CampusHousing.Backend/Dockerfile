# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["shared/backend/CampusPulse.Shared.Ai/CampusPulse.Shared.Ai.csproj", "shared/backend/CampusPulse.Shared.Ai/"]
COPY ["shared/backend/CampusPulse.Shared.Rag/CampusPulse.Shared.Rag.csproj", "shared/backend/CampusPulse.Shared.Rag/"]
COPY ["student-3/backend/CampusHousing.Backend/CampusHousing.Backend.csproj", "student-3/backend/CampusHousing.Backend/"]
RUN dotnet restore "./student-3/backend/CampusHousing.Backend/CampusHousing.Backend.csproj"
COPY . .
WORKDIR "/src/student-3/backend/CampusHousing.Backend"
RUN dotnet build "./CampusHousing.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CampusHousing.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CampusHousing.Backend.dll"]