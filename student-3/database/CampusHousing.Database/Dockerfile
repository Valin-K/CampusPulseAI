FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-3/database/CampusHousing.Database/CampusHousing.Database.csproj", "student-3/database/CampusHousing.Database/"]
RUN dotnet restore "./student-3/database/CampusHousing.Database/CampusHousing.Database.csproj"

COPY . .
WORKDIR "/src/student-3/database/CampusHousing.Database"
RUN dotnet publish "./CampusHousing.Database.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
COPY student-3/database/CampusHousing.Database/docker-entrypoint.sh /usr/local/bin/campus-housing-database-entrypoint
RUN sed -i 's/\r$//' /usr/local/bin/campus-housing-database-entrypoint \
	&& chmod +x /usr/local/bin/campus-housing-database-entrypoint
ENTRYPOINT ["/usr/local/bin/campus-housing-database-entrypoint"]
