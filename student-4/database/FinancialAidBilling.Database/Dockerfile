FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["shared/backend/CampusPulse.Shared.Security/CampusPulse.Shared.Security.csproj", "shared/backend/CampusPulse.Shared.Security/"]
COPY ["student-4/database/FinancialAidBilling.Database/FinancialAidBilling.Database.csproj", "student-4/database/FinancialAidBilling.Database/"]
RUN dotnet restore "./student-4/database/FinancialAidBilling.Database/FinancialAidBilling.Database.csproj"

COPY . .
WORKDIR "/src/student-4/database/FinancialAidBilling.Database"
RUN dotnet publish "./FinancialAidBilling.Database.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R $APP_UID /app
USER $APP_UID
ENTRYPOINT ["dotnet", "FinancialAidBilling.Database.dll"]
