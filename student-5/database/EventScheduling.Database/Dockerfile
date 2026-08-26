FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-5/database/EventScheduling.Database/EventScheduling.Database.csproj", "student-5/database/EventScheduling.Database/"]
RUN dotnet restore "./student-5/database/EventScheduling.Database/EventScheduling.Database.csproj"

COPY . .
WORKDIR "/src/student-5/database/EventScheduling.Database"
RUN dotnet build "./EventScheduling.Database.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS final
WORKDIR /app
COPY --from=build /app/build .
