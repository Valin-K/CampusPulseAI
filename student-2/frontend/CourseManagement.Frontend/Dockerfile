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
EXPOSE 80
