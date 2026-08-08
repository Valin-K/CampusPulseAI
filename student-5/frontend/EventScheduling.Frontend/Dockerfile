# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Build the Blazor WebAssembly app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["student-5/frontend/EventScheduling.Frontend/EventScheduling.Frontend.csproj", "student-5/frontend/EventScheduling.Frontend/"]
RUN dotnet restore "./student-5/frontend/EventScheduling.Frontend/EventScheduling.Frontend.csproj"
COPY . .
WORKDIR "/src/student-5/frontend/EventScheduling.Frontend"
RUN dotnet publish "./EventScheduling.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BlazorCacheBootResources=false

# Serve the static files with nginx
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
COPY --from=build /app/publish/wwwroot/docker-index.html ./index.html
EXPOSE 80