FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["shared/frontend/CampusPulse.Shared.Components/CampusPulse.Shared.Components.csproj", "shared/frontend/CampusPulse.Shared.Components/"]
COPY ["student-5/frontend/EventScheduling.Frontend/EventScheduling.Frontend.csproj", "student-5/frontend/EventScheduling.Frontend/"]
RUN dotnet restore "./student-5/frontend/EventScheduling.Frontend/EventScheduling.Frontend.csproj"
COPY . .
WORKDIR "/src/student-5/frontend/EventScheduling.Frontend"
RUN dotnet publish "./EventScheduling.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BlazorCacheBootResources=false

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
ENV BACKEND_BASE_URL=http://eventscheduling.backend:8080
ENV ACADEMICRECORDS_API_BASE_URL=http://academicrecords.backend:8080
COPY student-5/frontend/EventScheduling.Frontend/nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 80
