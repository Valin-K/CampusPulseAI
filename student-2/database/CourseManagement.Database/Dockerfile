FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-2/database/CourseManagement.Database/CourseManagement.Database.csproj", "student-2/database/CourseManagement.Database/"]
RUN dotnet restore "./student-2/database/CourseManagement.Database/CourseManagement.Database.csproj"

COPY . .
WORKDIR "/src/student-2/database/CourseManagement.Database"
RUN dotnet publish "./CourseManagement.Database.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R $APP_UID /app
USER $APP_UID
ENTRYPOINT ["dotnet", "CourseManagement.Database.dll"]
