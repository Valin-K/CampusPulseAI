FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-1/database/AcademicRecords.Database/AcademicRecords.Database.csproj", "student-1/database/AcademicRecords.Database/"]
RUN dotnet restore "./student-1/database/AcademicRecords.Database/AcademicRecords.Database.csproj"

COPY . .
WORKDIR "/src/student-1/database/AcademicRecords.Database"
RUN dotnet publish "./AcademicRecords.Database.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R $APP_UID /app
USER $APP_UID
ENTRYPOINT ["dotnet", "AcademicRecords.Database.dll"]
