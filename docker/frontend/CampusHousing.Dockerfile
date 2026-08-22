FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["student-3/frontend/CampusHousing.Frontend/CampusHousing.Frontend.csproj", "student-3/frontend/CampusHousing.Frontend/"]
RUN dotnet restore "./student-3/frontend/CampusHousing.Frontend/CampusHousing.Frontend.csproj"
COPY . .
WORKDIR "/src/student-3/frontend/CampusHousing.Frontend"
RUN dotnet publish "./CampusHousing.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BlazorCacheBootResources=false

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
COPY --from=build /app/publish/wwwroot/docker-index.html ./index.html
EXPOSE 80