FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["student-1/frontend/AcademicRecords.Frontend/AcademicRecords.Frontend.csproj", "student-1/frontend/AcademicRecords.Frontend/"]
RUN dotnet restore "./student-1/frontend/AcademicRecords.Frontend/AcademicRecords.Frontend.csproj"
COPY . .
WORKDIR "/src/student-1/frontend/AcademicRecords.Frontend"
RUN dotnet publish "./AcademicRecords.Frontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BlazorCacheBootResources=false

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
ENV BACKEND_HOST=academicrecords.backend
ENV BACKEND_PORT=8080
COPY student-1/frontend/AcademicRecords.Frontend/nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 80