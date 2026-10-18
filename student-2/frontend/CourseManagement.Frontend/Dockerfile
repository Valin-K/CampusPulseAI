FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["shared/backend/CampusPulse.Shared.MultiAgent/CampusPulse.Shared.MultiAgent.csproj", "shared/backend/CampusPulse.Shared.MultiAgent/"]
COPY ["shared/frontend/CampusPulse.Shared.Components/CampusPulse.Shared.Components.csproj", "shared/frontend/CampusPulse.Shared.Components/"]
COPY ["student-2/frontend/CourseManagement.Frontend/CourseManagement.Frontend.csproj", "student-2/frontend/CourseManagement.Frontend/"]
RUN dotnet restore "./student-2/frontend/CourseManagement.Frontend/CourseManagement.Frontend.csproj"
COPY . .
WORKDIR "/src/student-2/frontend/CourseManagement.Frontend"
RUN dotnet publish "./CourseManagement.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BlazorCacheBootResources=false

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
ENV BACKEND_HOST=coursemanagement.backend
ENV BACKEND_PORT=8080
ENV ACADEMICRECORDS_API_HOST=academicrecords.backend
ENV ACADEMICRECORDS_API_PORT=8080
COPY ["student-2/frontend/CourseManagement.Frontend/nginx.conf.template", "/etc/nginx/templates/default.conf.template"]
EXPOSE 80
