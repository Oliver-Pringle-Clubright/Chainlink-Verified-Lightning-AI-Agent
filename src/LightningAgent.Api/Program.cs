using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Data;
using LightningAgent.Data.Repositories;
using LightningAgent.Lightning;
using LightningAgent.Chainlink;
using LightningAgent.AI;
using LightningAgent.Verification;
using LightningAgent.Acp;
using LightningAgent.Engine;
using LightningAgent.Engine.BackgroundJobs;
using LightningAgent.Engine.Workflows;
using LightningAgent.AI.Judge;
using LightningAgent.AI.Orchestrator;
using LightningAgent.AI.Fraud;
using LightningAgent.Api.Hubs;
using LightningAgent.Api.Middleware;
using LightningAgent.Api.Services;
using LightningAgent.Data.Migrations;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── SQLite ──────────────────────────────────────────────────────────
var sqliteConnectionString = builder.Configuration.GetConnectionString("Sqlite")
    ?? "Data Source=lightningagent.db;Cache=Shared";
builder.Services.AddSingleton(new SqliteConnectionFactory(sqliteConnectionString));
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<MigrationRunner>();

// ── Repositories (scoped) ───────────────────────────────────────────
builder.Services.AddScoped<IAgentRepository, AgentRepository>();
builder.Services.AddScoped<IAgentCapabilityRepository, AgentCapabilityRepository>();
builder.Services.AddScoped<IAgentReputationRepository, AgentReputationRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IMilestoneRepository, MilestoneRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IDisputeRepository, DisputeRepository>();
builder.Services.AddScoped<IEscrowRepository, EscrowRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IPriceCacheRepository, PriceCacheRepository>();
builder.Services.AddScoped<ISpendLimitRepository, SpendLimitRepository>();
builder.Services.AddScoped<IVerificationRepository, VerificationRepository>();
builder.Services.AddScoped<IVerificationStrategyConfigRepository, VerificationStrategyConfigRepository>();

// ── Configuration sections ──────────────────────────────────────────
builder.Services.Configure<LightningSettings>(builder.Configuration.GetSection("Lightning"));
builder.Services.Configure<ChainlinkSettings>(builder.Configuration.GetSection("Chainlink"));
builder.Services.Configure<AcpSettings>(builder.Configuration.GetSection("Acp"));
builder.Services.Configure<ClaudeAiSettings>(builder.Configuration.GetSection("ClaudeAi"));
builder.Services.Configure<EscrowSettings>(builder.Configuration.GetSection("Escrow"));
builder.Services.Configure<PricingSettings>(builder.Configuration.GetSection("Pricing"));
builder.Services.Configure<VerificationSettings>(builder.Configuration.GetSection("Verification"));
builder.Services.Configure<SpendLimitSettings>(builder.Configuration.GetSection("SpendLimits"));
builder.Services.Configure<WorkerAgentSettings>(builder.Configuration.GetSection("WorkerAgent"));

// ── Lightning Network ─────────────────────────────────────────────
builder.Services.AddLightningServices(builder.Configuration);

// ── Chainlink ─────────────────────────────────────────────────────
builder.Services.AddChainlinkServices();

// ── AI (Claude API) ───────────────────────────────────────────────
builder.Services.AddAiServices(builder.Configuration);

// ── ACP Protocol ──────────────────────────────────────────────────
builder.Services.AddAcpServices(builder.Configuration);

// ── Verification Pipeline ─────────────────────────────────────────
builder.Services.AddVerificationServices();

// ── Webhook Delivery ─────────────────────────────────────────
builder.Services.AddHttpClient<WebhookDeliveryService>();

// ── SignalR Event Publishing ──────────────────────────────────
builder.Services.AddScoped<IEventPublisher, SignalREventPublisher>();

// ── Engine Services ───────────────────────────────────────────────
builder.Services.AddScoped<IEscrowManager, EscrowManager>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IReputationService, ReputationService>();
builder.Services.AddScoped<IAgentMatcher, AgentMatcher>();
builder.Services.AddScoped<ISpendLimitService, SpendLimitService>();
builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<ITaskOrchestrator, TaskOrchestrator>();
builder.Services.AddScoped<IDisputeResolver, DisputeResolver>();
builder.Services.AddScoped<IFraudDetector, FraudDetector>();
builder.Services.AddScoped<TaskDecompositionEngine>();
builder.Services.AddScoped<TaskLifecycleWorkflow>();
builder.Services.AddScoped<MilestonePaymentWorkflow>();
builder.Services.AddScoped<WorkerAgent>();
builder.Services.AddHostedService<AgentWorkerService>();
builder.Services.AddHostedService<EscrowExpiryChecker>();
builder.Services.AddHostedService<SpendLimitResetter>();
builder.Services.AddHostedService<ChainlinkResponsePoller>();
builder.Services.AddHostedService<VrfAuditSampler>();
builder.Services.AddHostedService<PriceFeedRefresher>();

// ── MVC + SignalR + Swagger ─────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

// ── CORS (allow all for dev) ────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ── Initialize database on startup ──────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var dbInit = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await dbInit.InitializeAsync();

    var migrationRunner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
    await migrationRunner.RunMigrationsAsync();
}

// ── Middleware pipeline ─────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.MapControllers();
app.MapHub<AgentNotificationHub>("/hubs/agent-notifications");

app.Run();
