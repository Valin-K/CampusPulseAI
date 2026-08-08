# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Build the Blazor WebAssembly app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["student-2/frontend/CourseManagement.Frontend/CourseManagement.Frontend.csproj", "student-2/frontend/CourseManagement.Frontend/"]
RUN dotnet restore "./student-2/frontend/CourseManagement.Frontend/CourseManagement.Frontend.csproj"
COPY . .
WORKDIR "/src/student-2/frontend/CourseManagement.Frontend"
RUN dotnet publish "./CourseManagement.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BlazorCacheBootResources=false

# Serve the static files with nginx
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
COPY --from=build /app/publish/wwwroot/docker-index.html ./index.html
EXPOSE 80