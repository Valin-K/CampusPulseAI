using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Database.Data;

public static class CourseManagementDatabaseMigrator
{
    public const string InitialMigrationId =
        "20260830044348_InitialCourseManagementSchema";

    const string integrityConstraintsMigrationId =
        "20260830044424_AddDatabaseIntegrityConstraints";

    public static async Task MigrateAsync(
        CourseManagementDbContext database,
        CancellationToken cancellationToken = default)
    {
        await BaselineEnsureCreatedDatabaseAsync(database, cancellationToken);
        await database.Database.MigrateAsync(cancellationToken);
    }

    static async Task BaselineEnsureCreatedDatabaseAsync(
        CourseManagementDbContext database,
        CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == ConnectionState.Closed;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (await HasMigrationHistoryAsync(connection, cancellationToken)
                || !await HasLegacySchemaAsync(connection, cancellationToken))
            {
                return;
            }

            await CreateMigrationHistoryAsync(connection, cancellationToken);
            await RecordMigrationAsync(
                connection,
                InitialMigrationId,
                cancellationToken);

            if (await HasIntegrityConstraintsAsync(connection, cancellationToken))
            {
                await RecordMigrationAsync(
                    connection,
                    integrityConstraintsMigrationId,
                    cancellationToken);
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    static async Task<bool> HasLegacySchemaAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('Courses', 'Prerequisites', 'FacultyAssignments');
            """;

        return await ExecuteScalarIntAsync(connection, sql, cancellationToken) == 3;
    }

    static async Task<bool> HasMigrationHistoryAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = '__EFMigrationsHistory';
            """;

        return await ExecuteScalarIntAsync(connection, sql, cancellationToken) == 1;
    }

    static async Task<bool> HasIntegrityConstraintsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND (
                sql LIKE '%CK_Courses_CreditPoints%'
                OR sql LIKE '%CK_Courses_Level%'
                OR sql LIKE '%CK_Prerequisites_DifferentCourses%'
              );
            """;

        return await ExecuteScalarIntAsync(connection, sql, cancellationToken) == 2;
    }

    static async Task CreateMigrationHistoryAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """;

        await ExecuteNonQueryAsync(connection, sql, cancellationToken);
    }

    static async Task RecordMigrationAsync(
        DbConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES (@migrationId, '10.0.9');
            """;

        var migrationParameter = command.CreateParameter();
        migrationParameter.ParameterName = "@migrationId";
        migrationParameter.Value = migrationId;
        command.Parameters.Add(migrationParameter);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    static async Task<int> ExecuteScalarIntAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
