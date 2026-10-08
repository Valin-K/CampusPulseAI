using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using MultiAgentServer;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddHttpClient<IMcpClient, McpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("workflowRag", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(30);
});
var store = new WorkflowStore(builder.Configuration["MultiAgent:DatabasePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".artifacts", "multi-agent.db"));
store.RecoverInterrupted();
builder.Services.AddSingleton(store);
builder.Services.AddSingleton<IWorkflowAgents, WorkflowAgents>();
builder.Services.AddSingleton<IWorkflowEvidenceCollector, WorkflowEvidenceCollector>();
builder.Services.AddSingleton<WorkflowCoordinator>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<WorkflowCoordinator>());

var app = builder.Build();
app.MapGet("/health", (IConfiguration config) => Results.Ok(new
{
    status = "healthy", service = "multi-agent", enabled = config.GetValue("MultiAgent:Enabled", false)
}));
app.MapWorkflowApi();
app.Run();
