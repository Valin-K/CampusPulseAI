using AcademicRecords.Database.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Tests;

internal sealed class ControllerTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private ControllerTestDatabase(SqliteConnection connection, AcademicRecordsDbContext context)
    {
        this.connection = connection;
        Context = context;
    }

    public AcademicRecordsDbContext Context { get; }

    public static async Task<ControllerTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var context = new AcademicRecordsDbContext(
            new DbContextOptionsBuilder<AcademicRecordsDbContext>()
                .UseSqlite(connection)
                .Options);

        await context.Database.EnsureCreatedAsync();
        return new ControllerTestDatabase(connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
