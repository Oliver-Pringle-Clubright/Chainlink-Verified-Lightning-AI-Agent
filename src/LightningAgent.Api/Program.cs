using System.Text;
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
using LightningAgent.Engine.Queue;
using LightningAgent.Engine.Services;
using LightningAgent.Engine.Workflows;
using LightningAgent.AI.Judge;
using LightningAgent.AI.Orchestrator;
using LightningAgent.AI.Fraud;
using LightningAgent.Api.Authentication;
using LightningAgent.Api.Hubs;
using LightningAgent.Api.Middleware;
using LightningAgent.Api.HealthChecks;
using LightningAgent.Api.Lifetime;
using LightningAgent.Api.Services;
using LightningAgent.Data.Migrations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Asp.Versioning;
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
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
builder.Services.AddScoped<IWebhookLogRepository, WebhookLogRepository>();
builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

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
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<OpenRouterSettings>(builder.Configuration.GetSection("OpenRouter"));
builder.Services.Configure<BackupSettings>(builder.Configuration.GetSection("Backup"));

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

// ── JWT Authentication ───────────────────────────────────────────
builder.Services.AddSingleton<JwtTokenService>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (!string.IsNullOrWhiteSpace(jwtSettings.Secret))
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
}
else
{
    builder.Services.AddAuthentication();
}

builder.Services.AddAuthorization();

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
builder.Services.AddScoped<ChannelManagerService>();
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
builder.Services.AddHostedService<EscrowRetryService>();
builder.Services.AddHostedService<InvoiceStatusPoller>();
builder.Services.AddHostedService<SecretRotationService>();
builder.Services.AddHostedService<DataCleanupService>();

// ── Task Queue (background orchestration) ────────────────────────────
builder.Services.AddSingleton<ITaskQueue, TaskQueue>();
builder.Services.AddHostedService<TaskQueueProcessor>();

// ── Metrics (singleton) ──────────────────────────────────────────────
builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();

// ── Graceful Shutdown ────────────────────────────────────────────────
builder.Services.AddHostedService<GracefulShutdownService>();

// ── Health Checks ───────────────────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks()
    .AddCheck<ClaudeApiHealthCheck>("claude-api");

// ── In-Memory Cache ─────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICachedDataService, CachedDataService>();

// ── API Versioning ───────────────────────────────────────────────────
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

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

    // ── Auto-register system agent for all skill types (idempotent) ──
    var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
    var existingAgent = await agentRepo.GetByExternalIdAsync("system-builtin");
    if (existingAgent is null)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Registering system built-in agent with all skill types");

        var systemAgent = new LightningAgent.Core.Models.Agent
        {
            ExternalId = "system-builtin",
            Name = "System Worker Agent",
            Status = LightningAgent.Core.Enums.AgentStatus.Active,
            DailySpendCapSats = 1_000_000,
            WeeklySpendCapSats = 5_000_000,
            RateLimitPerMinute = 1000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var agentId = await agentRepo.CreateAsync(systemAgent);

        var capRepo = scope.ServiceProvider.GetRequiredService<IAgentCapabilityRepository>();
        var skillTypes = new[]
        {
            LightningAgent.Core.Enums.SkillType.CodeGeneration,
            LightningAgent.Core.Enums.SkillType.DataAnalysis,
            LightningAgent.Core.Enums.SkillType.TextWriting,
            LightningAgent.Core.Enums.SkillType.ImageGeneration
        };

        foreach (var skill in skillTypes)
        {
            await capRepo.CreateAsync(new LightningAgent.Core.Models.AgentCapability
            {
                AgentId = agentId,
                SkillType = skill,
                TaskTypes = skill.ToString(),
                MaxConcurrency = 10,
                PriceSatsPerUnit = 0,
                CreatedAt = DateTime.UtcNow
            });
        }

        logger.LogInformation(
            "System agent registered with Id {AgentId} and {Count} capabilities",
            agentId, skillTypes.Length);
    }
}

// ── HTTPS / HSTS ────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

// ── Static files (wwwroot for dashboard) ─────────────────────────────
app.UseStaticFiles();

// ── Middleware pipeline ─────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<AuditLoggingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.MapControllers();
app.MapHub<AgentNotificationHub>("/hubs/agent-notifications");

app.Run();
