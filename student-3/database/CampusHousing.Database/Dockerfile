FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-3/database/CampusHousing.Database/CampusHousing.Database.csproj", "student-3/database/CampusHousing.Database/"]
RUN dotnet restore "./student-3/database/CampusHousing.Database/CampusHousing.Database.csproj"

COPY . .
WORKDIR "/src/student-3/database/CampusHousing.Database"
RUN dotnet build "./CampusHousing.Database.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS final
WORKDIR /app
COPY --from=build /app/build .
