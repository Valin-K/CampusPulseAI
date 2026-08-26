FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-1/database/AcademicRecords.Database/AcademicRecords.Database.csproj", "student-1/database/AcademicRecords.Database/"]
RUN dotnet restore "./student-1/database/AcademicRecords.Database/AcademicRecords.Database.csproj"

COPY . .
WORKDIR "/src/student-1/database/AcademicRecords.Database"
RUN dotnet build "./AcademicRecords.Database.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS final
WORKDIR /app
COPY --from=build /app/build .
