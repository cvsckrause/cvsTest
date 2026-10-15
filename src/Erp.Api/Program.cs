using Erp.Api;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = BuildConnectionString(builder.Configuration);
var cognitoAuthority = builder.Configuration["Authentication:Authority"];
var authenticationEnabled = !string.IsNullOrWhiteSpace(cognitoAuthority);
var developmentIdentity = DevelopmentIdentity.FromConfiguration(builder.Configuration);
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
builder.Services.AddScoped<RequestTenantContext>();
builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<RequestTenantContext>());
builder.Services.AddDbContext<ErpDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(services.GetRequiredService<ITenantContext>())));

if (authenticationEnabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = cognitoAuthority;
            options.Audience = builder.Configuration["Authentication:Audience"];
        });
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
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    await next();
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

    var identity = developmentIdentity.Resolve(context.User, authenticationEnabled);
    if (identity is null)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var tenant = context.RequestServices.GetRequiredService<RequestTenantContext>();
    tenant.Set(identity.Value.TenantId, identity.Value.UserId);
    await next();
});

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api/v1");
if (authenticationEnabled)
{
    api.RequireAuthorization();
}

api.MapMasterDataEndpoints();
api.MapInventoryEndpoints();
api.MapSalesEndpoints();

app.MapFallbackToFile("index.html");
app.Run();

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
