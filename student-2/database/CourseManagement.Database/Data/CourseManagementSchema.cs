using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Database.Data;

public static class CourseManagementSchema
{
    public static async Task EnsureCurrentAsync(
        CourseManagementDbContext database,
        CancellationToken cancellationToken = default)
    {
        await database.Database.EnsureCreatedAsync(cancellationToken);

        var connection = database.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var columnCheck = connection.CreateCommand();
            columnCheck.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info('Courses') WHERE name = 'GradeType';";
            var columnCount = Convert.ToInt64(
                await columnCheck.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (columnCount > 0)
            {
                return;
            }

            await using var addColumn = connection.CreateCommand();
            addColumn.CommandText =
                """ALTER TABLE "Courses" ADD COLUMN "GradeType" TEXT NOT NULL DEFAULT 'Mark';""";
            await addColumn.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
