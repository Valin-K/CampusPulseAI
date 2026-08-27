FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-2/database/CourseManagement.Database/CourseManagement.Database.csproj", "student-2/database/CourseManagement.Database/"]
RUN dotnet restore "./student-2/database/CourseManagement.Database/CourseManagement.Database.csproj"

COPY . .
WORKDIR "/src/student-2/database/CourseManagement.Database"
RUN dotnet build "./CourseManagement.Database.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS final
WORKDIR /app
COPY --from=build /app/build .
