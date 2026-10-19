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
ENV BACKEND_BASE_URL=http://campushousing.backend:8080
ENV ACADEMICRECORDS_API_BASE_URL=http://academicrecords.backend:8080
COPY student-3/frontend/CampusHousing.Frontend/nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 80