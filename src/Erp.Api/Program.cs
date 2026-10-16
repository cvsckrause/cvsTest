using System.Security.Claims;
using Erp.Api;
using Erp.BuildingBlocks;
using Erp.Observability;
using Erp.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddErpJsonLogging();
var connectionString = BuildConnectionString(builder.Configuration);
var cognitoAuthority = builder.Configuration["Authentication:Authority"];
var cognitoClientId = builder.Configuration["Authentication:ClientId"];
var authenticationEnabled = !string.IsNullOrWhiteSpace(cognitoAuthority);
if (authenticationEnabled && string.IsNullOrWhiteSpace(cognitoClientId))
{
    throw new InvalidOperationException("Authentication:ClientId is required when authentication is enabled.");
}

var developmentIdentity = DevelopmentIdentity.FromConfiguration(builder.Configuration);
var bootstrapTenantId = Guid.TryParse(builder.Configuration["Authentication:BootstrapTenantId"], out var parsedBootstrap)
    && parsedBootstrap != Guid.Empty
    ? parsedBootstrap
    : (Guid?)null;
if (developmentIdentity.Enabled && !builder.Environment.IsDevelopment() && authenticationEnabled)
{
    throw new InvalidOperationException(
        "DevelopmentIdentity:Enabled is only allowed when the environment is Development or authentication is disabled.");
}

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.AddSingleton(new ErpConnection(connectionString));
builder.Services.AddScoped<ITenantMemberships, EfTenantMemberships>();
builder.Services.AddScoped<RequestTenantContext>();
builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<RequestTenantContext>());
builder.Services.AddDbContext<ErpDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(services.GetRequiredService<ITenantContext>())));
builder.Services.AddErpTelemetry(
    "erp-api",
    builder.Configuration,
    tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.EnrichWithHttpRequest = (activity, request) =>
                activity.SetTag("correlation.id", request.HttpContext.TraceIdentifier);
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                if (response.HttpContext.Items[RequestLog.TenantId] is string tenantId && tenantId.Length > 0)
                {
                    activity.SetTag("tenant.id", tenantId);
                }

                if (response.HttpContext.Items[RequestLog.UserId] is string userId && userId.Length > 0)
                {
                    activity.SetTag("enduser.id", userId);
                }
            };
        })
        .AddHttpClientInstrumentation(),
    metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

if (authenticationEnabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options => CognitoAuthentication.Configure(options, cognitoAuthority!, cognitoClientId!));
    builder.Services.AddAuthorization();
}

var app = builder.Build();
app.UseExceptionHandler();
if (developmentIdentity.Enabled && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "Development identity is enabled outside Development because authentication is disabled. Remove DevelopmentIdentity when authentication is enabled.");
}

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }
    else if (correlationId.Length > 100)
    {
        correlationId = correlationId[..100];
    }

    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Erp.Request");
    using (logger.BeginScope(new RequestLogScope(context)))
    {
        await next();
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            logger.LogInformation(
                "Handled {Method} {Path} with status {StatusCode}.",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode);
        }
    }
});
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    if (!authenticationEnabled)
    {
        var identity = developmentIdentity.Resolve();
        if (identity is null)
        {
            await WriteForbidden(context, "Development identity is not configured.");
            return;
        }

        context.RequestServices.GetRequiredService<RequestTenantContext>()
            .Set(identity.Value.TenantId, identity.Value.UserId);
        context.Items[RequestLog.TenantId] = identity.Value.TenantId.ToString();
        context.Items[RequestLog.UserId] = identity.Value.UserId;
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(userId))
    {
        await WriteForbidden(context, "The access token has no subject.");
        return;
    }

    var membershipDirectory = context.RequestServices.GetRequiredService<ITenantMemberships>();
    var memberships = await membershipDirectory.ListAsync(userId, context.RequestAborted);
    if (memberships.Count == 0 && bootstrapTenantId is Guid bootstrap)
    {
        await membershipDirectory.EnsureDefaultAsync(userId, bootstrap, context.RequestAborted);
        memberships = await membershipDirectory.ListAsync(userId, context.RequestAborted);
    }

    if (HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.Equals("/api/v1/me/memberships", StringComparison.OrdinalIgnoreCase))
    {
        context.Items[RequestLog.UserId] = userId;
        await next();
        return;
    }

    var choice = TenantMembershipSelection.Choose(
        context.Request.Headers[TenantMembershipSelection.TenantHeader].FirstOrDefault(),
        memberships);
    if (choice.TenantId is not Guid tenantId)
    {
        var detail = memberships.Count == 0
            ? "This user has no tenant membership."
            : "The requested tenant is not allowed.";
        await WriteForbidden(context, detail);
        return;
    }

    context.RequestServices.GetRequiredService<RequestTenantContext>().Set(tenantId, userId);
    context.Items[RequestLog.TenantId] = tenantId.ToString();
    context.Items[RequestLog.UserId] = userId;
    await next();
});

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/auth-config.json", () =>
{
    if (!authenticationEnabled)
    {
        return Results.Json(new { mode = "local" });
    }

    var hostedUiDomain = builder.Configuration["Authentication:HostedUiDomain"];
    if (string.IsNullOrWhiteSpace(hostedUiDomain))
    {
        throw new InvalidOperationException(
            "Authentication:HostedUiDomain is required when authentication is enabled.");
    }

    return Results.Json(new
    {
        mode = "cognito",
        authority = cognitoAuthority,
        clientId = cognitoClientId,
        hostedUiOrigin = $"https://{hostedUiDomain}",
    });
});

var api = app.MapGroup("/api/v1");
if (authenticationEnabled)
{
    api.RequireAuthorization();
}

api.MapMasterDataEndpoints();
api.MapInventoryEndpoints();
api.MapSalesEndpoints();
api.MapMembershipEndpoints();

app.MapFallbackToFile("index.html");
app.Run();

static Task WriteForbidden(HttpContext context, string detail) =>
    Results.Problem(
        title: "The request is not allowed.",
        detail: detail,
        statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);

static string BuildConnectionString(IConfiguration configuration)
{
    var configured = configuration.GetConnectionString("Erp");
    if (!string.IsNullOrWhiteSpace(configured)) return configured;

    return new NpgsqlConnectionStringBuilder
    {
        Host = configuration["Database:Host"] ?? throw new InvalidOperationException("Database:Host is required."),
        Database = configuration["Database:Name"] ?? "erp_dev",
        Username = configuration["Database:Username"] ?? throw new InvalidOperationException("Database:Username is required."),
        Password = configuration["Database:Password"] ?? throw new InvalidOperationException("Database:Password is required."),
        SslMode = SslMode.Require
    }.ConnectionString;
}

public sealed class RequestTenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public string UserId { get; private set; } = string.Empty;

    public void Set(Guid tenantId, string userId)
    {
        TenantId = tenantId;
        UserId = userId;
    }
}

public partial class Program;
