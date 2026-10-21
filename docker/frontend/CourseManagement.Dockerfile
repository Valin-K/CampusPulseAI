FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["student-2/frontend/CourseManagement.Frontend/CourseManagement.Frontend.csproj", "student-2/frontend/CourseManagement.Frontend/"]
RUN dotnet restore "./student-2/frontend/CourseManagement.Frontend/CourseManagement.Frontend.csproj"
COPY . .
WORKDIR "/src/student-2/frontend/CourseManagement.Frontend"
RUN dotnet publish "./CourseManagement.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BlazorCacheBootResources=false

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
COPY shared/frontend/campus-config.json.template ./campus-config.json.template
COPY --chmod=755 shared/frontend/docker-entrypoint.d/40-render-portal-links.sh /docker-entrypoint.d/40-render-portal-links.sh
ENV BACKEND_BASE_URL=http://coursemanagement.backend:8080
ENV ACADEMICRECORDS_API_BASE_URL=http://academicrecords.backend:8080
COPY student-2/frontend/CourseManagement.Frontend/nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 80