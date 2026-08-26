FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["student-4/database/FinancialAidBilling.Database/FinancialAidBilling.Database.csproj", "student-4/database/FinancialAidBilling.Database/"]
RUN dotnet restore "./student-4/database/FinancialAidBilling.Database/FinancialAidBilling.Database.csproj"

COPY . .
WORKDIR "/src/student-4/database/FinancialAidBilling.Database"
RUN dotnet build "./FinancialAidBilling.Database.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS final
WORKDIR /app
COPY --from=build /app/build .
