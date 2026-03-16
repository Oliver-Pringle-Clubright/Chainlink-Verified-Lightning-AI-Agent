using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
using Microsoft.AspNetCore.SignalR;
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
builder.Services.AddScoped<ICcipMessageRepository, CcipMessageRepository>();
builder.Services.AddScoped<IArtifactRepository, ArtifactRepository>();
builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();

// ── Configuration sections ──────────────────────────────────────────
builder.Services.Configure<LightningSettings>(builder.Configuration.GetSection("Lightning"));
builder.Services.Configure<ChainlinkSettings>(builder.Configuration.GetSection("Chainlink"));
builder.Services.Configure<AcpSettings>(builder.Configuration.GetSection("Acp"));
builder.Services.Configure<ClaudeAiSettings>(builder.Configuration.GetSection("ClaudeAi"));
builder.Services.Configure<EscrowSettings>(builder.Configuration.GetSection("Escrow"));

var escrowSettings = builder.Configuration.GetSection("Escrow").Get<EscrowSettings>();
if (escrowSettings is not null && !string.IsNullOrWhiteSpace(escrowSettings.EncryptionKey))
{
    LightningAgent.Engine.Security.PreimageProtector.Initialize(escrowSettings.EncryptionKey);
}

builder.Services.Configure<PricingSettings>(builder.Configuration.GetSection("Pricing"));
builder.Services.Configure<CoinGeckoSettings>(builder.Configuration.GetSection("CoinGecko"));
builder.Services.Configure<MultiChainSettings>(builder.Configuration.GetSection("MultiChain"));
builder.Services.AddScoped<LightningAgent.Engine.Services.MultiChainPriceService>();

// ── Payment Providers ────────────────────────────────────────────────
builder.Services.AddScoped<IPaymentProvider, LightningAgent.Engine.PaymentProviders.LightningPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, LightningAgent.Engine.PaymentProviders.Erc20PaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, LightningAgent.Engine.PaymentProviders.NativeTokenPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, LightningAgent.Engine.PaymentProviders.CcipPaymentProvider>();
builder.Services.AddScoped<LightningAgent.Engine.PaymentProviders.PaymentRouter>();
builder.Services.Configure<VerificationSettings>(builder.Configuration.GetSection("Verification"));
builder.Services.Configure<SpendLimitSettings>(builder.Configuration.GetSection("SpendLimits"));
builder.Services.Configure<PlatformFeeSettings>(builder.Configuration.GetSection("PlatformFees"));
builder.Services.Configure<WorkerAgentSettings>(builder.Configuration.GetSection("WorkerAgent"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<OpenRouterSettings>(builder.Configuration.GetSection("OpenRouter"));
builder.Services.Configure<BackupSettings>(builder.Configuration.GetSection("Backup"));
builder.Services.Configure<NetworkSettings>(builder.Configuration.GetSection("Network"));

builder.Services.PostConfigure<ChainlinkSettings>(settings =>
{
    var networkSettings = builder.Configuration.GetSection("Network").Get<NetworkSettings>() ?? new NetworkSettings();
    var source = networkSettings.IsTest ? settings.Testnet : settings.Mainnet;

    // Only override main properties if the network-specific value is non-empty
    if (!string.IsNullOrEmpty(source.EthereumRpcUrl)) settings.EthereumRpcUrl = source.EthereumRpcUrl;
    if (!string.IsNullOrEmpty(source.FunctionsRouterAddress)) settings.FunctionsRouterAddress = source.FunctionsRouterAddress;
    if (!string.IsNullOrEmpty(source.AutomationRegistryAddress)) settings.AutomationRegistryAddress = source.AutomationRegistryAddress;
    if (!string.IsNullOrEmpty(source.VrfCoordinatorAddress)) settings.VrfCoordinatorAddress = source.VrfCoordinatorAddress;
    if (!string.IsNullOrEmpty(source.VrfKeyHash)) settings.VrfKeyHash = source.VrfKeyHash;
    if (!string.IsNullOrEmpty(source.VrfConsumerAddress)) settings.VrfConsumerAddress = source.VrfConsumerAddress;
    if (!string.IsNullOrEmpty(source.BtcUsdPriceFeedAddress)) settings.BtcUsdPriceFeedAddress = source.BtcUsdPriceFeedAddress;
    if (!string.IsNullOrEmpty(source.EthUsdPriceFeedAddress)) settings.EthUsdPriceFeedAddress = source.EthUsdPriceFeedAddress;
    if (!string.IsNullOrEmpty(source.LinkUsdPriceFeedAddress)) settings.LinkUsdPriceFeedAddress = source.LinkUsdPriceFeedAddress;
    if (!string.IsNullOrEmpty(source.LinkEthPriceFeedAddress)) settings.LinkEthPriceFeedAddress = source.LinkEthPriceFeedAddress;
    if (!string.IsNullOrEmpty(source.VrfSubscriptionId)) settings.VrfSubscriptionId = source.VrfSubscriptionId;
    if (!string.IsNullOrEmpty(source.FunctionsSubscriptionId)) settings.FunctionsSubscriptionId = source.FunctionsSubscriptionId;
    if (!string.IsNullOrEmpty(source.DonId)) settings.DonId = source.DonId;
    if (!string.IsNullOrEmpty(source.PrivateKeyPath)) settings.PrivateKeyPath = source.PrivateKeyPath;
    if (!string.IsNullOrEmpty(source.CcipRouterAddress)) settings.CcipRouterAddress = source.CcipRouterAddress;
    if (source.CcipSourceChainSelector != 0) settings.CcipSourceChainSelector = source.CcipSourceChainSelector;
    if (!string.IsNullOrEmpty(source.VerifiedEscrowAddress)) settings.VerifiedEscrowAddress = source.VerifiedEscrowAddress;
    if (!string.IsNullOrEmpty(source.FairAssignmentAddress)) settings.FairAssignmentAddress = source.FairAssignmentAddress;
    if (!string.IsNullOrEmpty(source.ReputationLedgerAddress)) settings.ReputationLedgerAddress = source.ReputationLedgerAddress;
    if (!string.IsNullOrEmpty(source.DeadlineEnforcerAddress)) settings.DeadlineEnforcerAddress = source.DeadlineEnforcerAddress;
});

// ── Auto-detect chain ID and fill Chainlink addresses from registry ──
// Detect chain ID from RPC early so PostConfigure can use it
long? detectedChainId = null;
{
    var networkSettings = builder.Configuration.GetSection("Network").Get<NetworkSettings>() ?? new NetworkSettings();
    var chainlinkSection = builder.Configuration.GetSection("Chainlink");
    var rpcUrl = networkSettings.IsTest
        ? chainlinkSection["Testnet:EthereumRpcUrl"]
        : chainlinkSection["Mainnet:EthereumRpcUrl"];
    if (string.IsNullOrWhiteSpace(rpcUrl))
        rpcUrl = chainlinkSection["EthereumRpcUrl"];

    if (!string.IsNullOrWhiteSpace(rpcUrl))
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var request = new { jsonrpc = "2.0", method = "eth_chainId", @params = Array.Empty<object>(), id = 1 };
            var response = await httpClient.PostAsJsonAsync(rpcUrl, request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (json.TryGetProperty("result", out var resultProp))
                {
                    detectedChainId = Convert.ToInt64(resultProp.GetString(), 16);
                }
            }
        }
        catch { /* Chain detection is best-effort */ }
    }
}

if (detectedChainId.HasValue)
{
    var chainId = detectedChainId.Value;
    builder.Services.PostConfigure<ChainlinkSettings>(settings =>
    {
        var defaults = ChainlinkAddressRegistry.GetDefaults(chainId);
        if (defaults is null) return;

        // Apply registry defaults to any empty fields in the resolved settings
        if (string.IsNullOrEmpty(settings.EthUsdPriceFeedAddress) && !string.IsNullOrEmpty(defaults.EthUsdPriceFeedAddress))
            settings.EthUsdPriceFeedAddress = defaults.EthUsdPriceFeedAddress;
        if (string.IsNullOrEmpty(settings.BtcUsdPriceFeedAddress) && !string.IsNullOrEmpty(defaults.BtcUsdPriceFeedAddress))
            settings.BtcUsdPriceFeedAddress = defaults.BtcUsdPriceFeedAddress;
        if (string.IsNullOrEmpty(settings.LinkUsdPriceFeedAddress) && !string.IsNullOrEmpty(defaults.LinkUsdPriceFeedAddress))
            settings.LinkUsdPriceFeedAddress = defaults.LinkUsdPriceFeedAddress;
        if (string.IsNullOrEmpty(settings.LinkEthPriceFeedAddress) && !string.IsNullOrEmpty(defaults.LinkEthPriceFeedAddress))
            settings.LinkEthPriceFeedAddress = defaults.LinkEthPriceFeedAddress;
        if (string.IsNullOrEmpty(settings.FunctionsRouterAddress) && !string.IsNullOrEmpty(defaults.FunctionsRouterAddress))
            settings.FunctionsRouterAddress = defaults.FunctionsRouterAddress;
        if (string.IsNullOrEmpty(settings.VrfCoordinatorAddress) && !string.IsNullOrEmpty(defaults.VrfCoordinatorAddress))
            settings.VrfCoordinatorAddress = defaults.VrfCoordinatorAddress;
        if (string.IsNullOrEmpty(settings.AutomationRegistryAddress) && !string.IsNullOrEmpty(defaults.AutomationRegistryAddress))
            settings.AutomationRegistryAddress = defaults.AutomationRegistryAddress;
        if (string.IsNullOrEmpty(settings.CcipRouterAddress) && !string.IsNullOrEmpty(defaults.CcipRouterAddress))
            settings.CcipRouterAddress = defaults.CcipRouterAddress;
        if (settings.CcipSourceChainSelector == 0 && defaults.CcipSourceChainSelector != 0)
            settings.CcipSourceChainSelector = defaults.CcipSourceChainSelector;
        if (string.IsNullOrEmpty(settings.DonId) && !string.IsNullOrEmpty(defaults.DonId))
            settings.DonId = defaults.DonId;
    });
}

builder.Services.PostConfigure<LightningSettings>(settings =>
{
    var networkSettings = builder.Configuration.GetSection("Network").Get<NetworkSettings>() ?? new NetworkSettings();
    var source = networkSettings.IsTest ? settings.Testnet : settings.Mainnet;

    if (!string.IsNullOrEmpty(source.LndRestUrl)) settings.LndRestUrl = source.LndRestUrl;
    if (!string.IsNullOrEmpty(source.MacaroonPath)) settings.MacaroonPath = source.MacaroonPath;
    if (!string.IsNullOrEmpty(source.TlsCertPath)) settings.TlsCertPath = source.TlsCertPath;
});

// ── Lightning Network ─────────────────────────────────────────────
// Only allow insecure TLS in development — production requires a real cert
LightningAgent.Lightning.LndTlsCertHandler.AllowInsecureDevelopmentMode = builder.Environment.IsDevelopment();
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
    // Enforce minimum 32-byte key length for HMAC-SHA256
    if (Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
    {
        throw new InvalidOperationException(
            "Jwt:Secret must be at least 32 bytes long for HMAC-SHA256 security. " +
            "Current length: " + Encoding.UTF8.GetByteCount(jwtSettings.Secret) + " bytes.");
    }

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
else if (!builder.Environment.IsDevelopment())
{
    // In production, JWT must be configured
    throw new InvalidOperationException(
        "Jwt:Secret is required in non-development environments. " +
        "Set it via user secrets, environment variables, or appsettings.Production.json.");
}
else
{
    // Development only: generate ephemeral JWT secret so auth is still functional
    var ephemeralSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    Console.WriteLine("WARNING: Jwt:Secret is not configured. Using ephemeral secret for this session. " +
        "Set Jwt:Secret for persistent JWT tokens.");
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ephemeralSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiKeyAuthenticated", policy =>
        policy.RequireAssertion(context =>
        {
            var httpContext = context.Resource as HttpContext
                ?? (context.Resource as Microsoft.AspNetCore.SignalR.HubInvocationContext)?.Context?.GetHttpContext();
            if (httpContext is null) return false;

            // Allow through if DevMode is active, admin key matched, or agent key matched
            return httpContext.Items.ContainsKey("DevMode")
                || httpContext.Items.ContainsKey("IsAdmin")
                || httpContext.Items.ContainsKey("AuthenticatedAgentId");
        }));
});

// ── Webhook Delivery ─────────────────────────────────────────
builder.Services.AddHttpClient<WebhookDeliveryService>();
builder.Services.AddScoped<WebhookDispatcher>();

// ── SignalR Event Publishing ──────────────────────────────────
builder.Services.AddScoped<IEventPublisher, SignalREventPublisher>();

// ── CoinGecko Price Feeds ─────────────────────────────────────────
builder.Services.AddHttpClient<ICoinGeckoClient, LightningAgent.Engine.Services.CoinGeckoClient>();

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
builder.Services.AddScoped<OnChainEscrowService>();
builder.Services.AddScoped<OnChainReputationService>();
builder.Services.AddScoped<TaskLifecycleWorkflow>();
builder.Services.AddScoped<MilestonePaymentWorkflow>();
builder.Services.AddScoped<WorkerAgent>();
builder.Services.AddHostedService<AgentWorkerService>();
builder.Services.AddHostedService<EscrowExpiryChecker>();
builder.Services.AddHostedService<SpendLimitResetter>();
builder.Services.AddHostedService<ChainlinkResponsePoller>();
builder.Services.AddHostedService<VrfAuditSampler>();
builder.Services.AddHostedService<VrfResponsePoller>();
builder.Services.AddHostedService<PriceFeedRefresher>();
builder.Services.AddHostedService<EscrowRetryService>();
builder.Services.AddHostedService<InvoiceStatusPoller>();
builder.Services.AddHostedService<SecretRotationService>();
builder.Services.AddHostedService<DataCleanupService>();
builder.Services.AddHostedService<StaleTaskReassigner>();
builder.Services.AddHostedService<ParentTaskCompletionService>();
builder.Services.AddHostedService<AutomatedBackupService>();
builder.Services.AddHostedService<CcipMessagePoller>();
builder.Services.AddScoped<CcipBridgeService>();
builder.Services.AddHostedService<LightningAgent.Engine.BackgroundJobs.RecurringTaskService>();

// ── Task Queue (background orchestration) ────────────────────────────
builder.Services.AddSingleton<ITaskQueue, TaskQueue>();
builder.Services.AddHostedService<TaskQueueProcessor>();

// ── Metrics (singleton) ──────────────────────────────────────────────
builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();

// ── Service Health Tracking (singleton) ─────────────────────────────
builder.Services.AddSingleton<IServiceHealthTracker, ServiceHealthTracker>();

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

// ── CORS (restrictive by default) ───────────────────────────────────
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["https://localhost:5001", "http://localhost:5210"];

    if (allowedOrigins.Length == 0 && !builder.Environment.IsDevelopment())
    {
        // In production, default to same-origin only (no CORS)
        options.AddDefaultPolicy(policy =>
        {
            policy.SetIsOriginAllowed(_ => false);
        });
    }
    else
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(allowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray())
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    }
});

var app = builder.Build();

// ── Startup configuration validation ────────────────────────────────
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    StartupValidator.Validate(app.Configuration, startupLogger);
    await StartupValidator.ValidateChainIdAsync(app.Configuration, startupLogger);
}

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

// ── Security Headers ────────────────────────────────────────────────
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "0"; // Modern best practice: disable legacy filter, rely on CSP
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; style-src 'self' 'unsafe-inline'; connect-src 'self' wss: ws:; img-src 'self' data:;";
    await next();
});

// ── Static files (wwwroot for dashboard + landing page) ──────────────
app.UseDefaultFiles(); // serves index.html at /
app.UseStaticFiles();

// ── Middleware pipeline ─────────────────────────────────────────────
app.UseMiddleware<RequestSizeLimitMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<AuditLoggingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseCors();
app.MapControllers();
app.MapHub<AgentNotificationHub>("/hubs/agent-notifications");

app.Run();
