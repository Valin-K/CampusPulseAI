using System.Text.Json;
using CampusPulse.Shared.MultiAgent;
using Microsoft.Data.Sqlite;

namespace MultiAgentServer;

public sealed class WorkflowConflictException(string message) : Exception(message);

public sealed class WorkflowStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public WorkflowStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS workflows (id TEXT PRIMARY KEY, feature TEXT NOT NULL, version INTEGER NOT NULL, snapshot TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS audit (sequence INTEGER PRIMARY KEY AUTOINCREMENT, workflow_id TEXT NOT NULL, record TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS audit_workflow ON audit(workflow_id);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    public Workflow Save(Workflow workflow, string eventName)
    {
        var saved = workflow with { Version = workflow.Version + 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = workflow.Version == 0
            ? "INSERT INTO workflows(id,feature,version,snapshot) VALUES ($id,$feature,$next,$snapshot)"
            : "UPDATE workflows SET version=$next,snapshot=$snapshot WHERE id=$id AND version=$previous";
        command.Parameters.AddWithValue("$id", workflow.Id.ToString());
        command.Parameters.AddWithValue("$feature", workflow.Feature);
        command.Parameters.AddWithValue("$next", saved.Version);
        command.Parameters.AddWithValue("$previous", workflow.Version);
        command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(saved, Json));
        if (command.ExecuteNonQuery() != 1)
            throw new WorkflowConflictException("This workflow changed. Reload it before submitting a review.");
        command.CommandText = "INSERT INTO audit(workflow_id,record) VALUES ($id,$record)";
        command.Parameters.AddWithValue("$record", JsonSerializer.Serialize(new WorkflowAudit(
            0, saved.Id, saved.Feature, saved.Version, saved.Round, eventName, saved.Status, saved.UpdatedAtUtc), Json));
        command.ExecuteNonQuery();
        transaction.Commit();
        return saved;
    }

    public Workflow? Get(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot FROM workflows WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteScalar() is string json ? Deserialize(json) : null;
    }

    public IReadOnlyList<Workflow> List(string? feature = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot FROM workflows WHERE $feature IS NULL OR feature=$feature ORDER BY rowid DESC";
        command.Parameters.AddWithValue("$feature", (object?)feature ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var workflows = new List<Workflow>();
        while (reader.Read()) workflows.Add(Deserialize(reader.GetString(0)));
        return workflows;
    }

    public IReadOnlyList<WorkflowAudit> Audit(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,record FROM audit WHERE workflow_id=$id ORDER BY sequence";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        var entries = new List<WorkflowAudit>();
        while (reader.Read())
            entries.Add((JsonSerializer.Deserialize<WorkflowAudit>(reader.GetString(1), Json)
                ?? throw new InvalidDataException("Invalid workflow audit record.")) with { Sequence = reader.GetInt64(0) });
        return entries;
    }

    private static Workflow Deserialize(string json) => JsonSerializer.Deserialize<Workflow>(json, Json)
        ?? throw new InvalidDataException("Invalid workflow snapshot.");

    public void RecoverInterrupted()
    {
        foreach (var workflow in List().Where(item => item.Status == WorkflowStatus.Running))
            Save(workflow with { Status = WorkflowStatus.Failed, Error = "Server stopped before the workflow completed. Start a new workflow." }, "interrupted");
    }
}
