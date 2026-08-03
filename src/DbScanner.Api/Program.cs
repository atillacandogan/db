using DbScanner.Application;
using DbScanner.Api.Contracts;
using DbScanner.Api.Mapping;
using DbScanner.Api.Security;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure;
using DbScanner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:3000", "http://127.0.0.1:3000"];

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
var authenticationAuthority = builder.Configuration["Authentication:Authority"]
    ?? throw new InvalidOperationException("Authentication:Authority is required.");
var authenticationAudience = builder.Configuration["Authentication:Audience"]
    ?? throw new InvalidOperationException("Authentication:Audience is required.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authenticationAuthority;
        options.Audience = authenticationAudience;
        options.RequireHttpsMetadata = builder.Configuration
            .GetValue("Authentication:RequireHttpsMetadata", !builder.Environment.IsDevelopment());
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = "roles"
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.Viewer, policy =>
        policy.RequireAuthenticatedUser().RequireRole(
            AuthorizationRoles.Viewer,
            AuthorizationRoles.Operator,
            AuthorizationRoles.Admin))
    .AddPolicy(AuthorizationPolicies.Operator, policy =>
        policy.RequireAuthenticatedUser().RequireRole(
            AuthorizationRoles.Operator,
            AuthorizationRoles.Admin))
    .AddPolicy(AuthorizationPolicies.Admin, policy =>
        policy.RequireAuthenticatedUser().RequireRole(AuthorizationRoles.Admin));
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebClient", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services
    .AddDbScannerApplication()
    .AddDbScannerInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.ApplyDbScannerMigrationsAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("WebClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");

app.MapGet("/api/status", () => Results.Ok(new
{
    service = "DbScanner.Api",
    status = "ready"
}))
.WithName("GetApiStatus");

app.MapGet("/api/findings", async (
    IFindingCatalogService findingCatalogService,
    CancellationToken cancellationToken) =>
{
    var findings = await findingCatalogService.ListLatestAsync(50, cancellationToken);
    return Results.Ok(findings);
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("GetFindings")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapGet("/api/rules", async (
    IRuleCatalogService ruleCatalogService,
    CancellationToken cancellationToken) =>
{
    var rules = await ruleCatalogService.ListRulesAsync(cancellationToken);
    return Results.Ok(rules);
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("GetRules")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapPost("/api/rules", async (
    CreateRuleRequest request,
    IRuleCatalogService ruleCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var rule = await ruleCatalogService.CreateRuleAsync(
            new CreateRuleCommand(
                request.Id,
                request.Title,
                request.Description,
                request.Engine,
                request.Severity,
                request.Weight,
                request.Enabled,
                request.ParserType,
                request.MatcherType,
                request.MatcherExpression,
                request.Remediation,
                request.SupportedEngines,
                request.NseScripts),
            cancellationToken);

        return Results.Created($"/api/rules/{rule.Id}", rule);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("CreateRule")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPut("/api/rules/{id}", async (
    string id,
    UpdateRuleRequest request,
    IRuleCatalogService ruleCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var rule = await ruleCatalogService.UpdateRuleAsync(
            id,
            new UpdateRuleCommand(
                request.Title,
                request.Description,
                request.Engine,
                request.Severity,
                request.Weight,
                request.Enabled,
                request.ParserType,
                request.MatcherType,
                request.MatcherExpression,
                request.Remediation,
                request.SupportedEngines,
                request.NseScripts),
            cancellationToken);

        return Results.Ok(rule);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("UpdateRule")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapGet("/api/rules/support-matrix", async (
    IRuleCatalogService ruleCatalogService,
    CancellationToken cancellationToken) =>
{
    var matrix = await ruleCatalogService.ListSupportMatrixAsync(cancellationToken);
    return Results.Ok(matrix);
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("GetRuleSupportMatrix")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapGet("/api/nse-scripts", async (
    INseScriptCatalogService nseScriptCatalogService,
    CancellationToken cancellationToken) =>
{
    var scripts = await nseScriptCatalogService.ListAsync(cancellationToken);
    return Results.Ok(scripts);
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("GetNseScripts")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapPost("/api/nse-scripts/sync", async (
    INseScriptSyncRequester nseScriptSyncRequester,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await nseScriptSyncRequester.RequestSyncAsync(cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
    {
        return Results.Json(
            new { error = "NSE script sync failed." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.Produces<NseScriptSyncResult>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status503ServiceUnavailable)
.WithName("SyncNseScripts")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPost("/api/nse-scripts", async (
    CreateNseScriptRequest request,
    INseScriptCatalogService nseScriptCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var script = await nseScriptCatalogService.CreateAsync(
            new CreateNseScriptCommand(
                request.Id,
                request.NseScriptName,
                request.DisplayName,
                request.Description,
                request.SupportedEngines,
                request.DefaultEnabled,
                request.ApprovalStatus,
                request.MatcherType,
                request.ParserType,
                request.MatcherExpression,
                request.Severity,
                request.Weight,
                request.Categories,
                request.RemediationTemplate,
                request.DocUrl),
            cancellationToken);

        return Results.Created($"/api/nse-scripts/{script.Id}", script);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("CreateNseScript")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPut("/api/nse-scripts/{id}", async (
    string id,
    UpdateNseScriptRequest request,
    INseScriptCatalogService nseScriptCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var script = await nseScriptCatalogService.UpdateAsync(
            id,
            new UpdateNseScriptCommand(
                request.DisplayName,
                request.Description,
                request.SupportedEngines,
                request.DefaultEnabled,
                request.ApprovalStatus,
                request.MatcherType,
                request.ParserType,
                request.MatcherExpression,
                request.Severity,
                request.Weight,
                request.Categories,
                request.RemediationTemplate,
                request.DocUrl),
            cancellationToken);

        return Results.Ok(script);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("UpdateNseScript")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapDelete("/api/nse-scripts/{id}", async (
    string id,
    INseScriptCatalogService nseScriptCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var deleted = await nseScriptCatalogService.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.Produces(StatusCodes.Status204NoContent)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("DeleteNseScript")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapGet("/api/targets", async (
    ITargetService targetService,
    CancellationToken cancellationToken) =>
{
    var targets = await targetService.ListAsync(cancellationToken);
    return Results.Ok(targets);
})
.WithName("GetTargets")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapPost("/api/targets", async (
    CreateTargetRequest request,
    ITargetService targetService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var target = await targetService.CreateAsync(
            new CreateTargetCommand(
                request.Name,
                request.Host,
                request.Port,
                ParseEngine(request.Engine),
                request.EnvironmentTag,
                request.Enabled,
                ParseConnectionMode(request.ConnectionMode),
                request.ConnectionString,
                MapCredentials(request.Credentials)),
            cancellationToken);

        return Results.Created($"/api/targets/{target.Id}", target);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.WithName("CreateTarget")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapPut("/api/targets/{id:guid}", async (
    Guid id,
    UpdateTargetRequest request,
    ITargetService targetService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var target = await targetService.UpdateAsync(
            id,
            new UpdateTargetCommand(
                request.Name,
                request.Host,
                request.Port,
                ParseEngine(request.Engine),
                request.EnvironmentTag,
                request.Enabled,
                MapCredentials(request.Credentials),
                request.ClearCredentials),
            cancellationToken);

        return Results.Ok(target);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.WithName("UpdateTarget")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapDelete("/api/targets/{id:guid}", async (
    Guid id,
    ITargetService targetService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var deleted = await targetService.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.WithName("DeleteTarget")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapGet("/api/discoveries", async (
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    var jobs = await discoveryService.ListAsync(cancellationToken);
    return Results.Ok(jobs);
})
.WithName("ListDiscoveryJobs")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapGet("/api/discoveries/{id:guid}", async (
    Guid id,
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    var job = await discoveryService.GetAsync(id, cancellationToken);
    return job is null ? Results.NotFound() : Results.Ok(job);
})
.WithName("GetDiscoveryJob")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapPost("/api/discoveries", async (
    CreateDiscoveryRequest request,
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var job = await discoveryService.CreateAsync(
            new CreateDiscoveryCommand(
                request.Name,
                request.Scope,
                request.Environment,
                ParseDiscoveryEngines(request.SupportedEngineFilter)),
            cancellationToken);
        return Results.Accepted($"/api/discoveries/{job.Id}", job);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.WithName("CreateDiscoveryJob")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapPost("/api/discoveries/{id:guid}/targets", async (
    Guid id,
    AddDiscoveryTargetsRequest request,
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var results = await discoveryService.AddTargetsAsync(id, request.ResultIds, cancellationToken);
        return Results.Ok(results);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.WithName("AddDiscoveryTargets")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapPost("/api/scans", async (
    CreateScanRequest request,
    IScanService scanService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var scan = await scanService.CreateScanAsync(
            request.TargetId,
            request.SelectedNseScriptIds,
            cancellationToken);
        return Results.Accepted($"/api/scans/{scan.Id}", scan.ToDto());
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.WithName("CreateScan")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapGet("/api/scans", async (
    IScanService scanService,
    CancellationToken cancellationToken) =>
{
    var scans = await scanService.ListScansAsync(cancellationToken);
    return Results.Ok(scans.Select(scan => scan.ToListItemDto()));
})
.WithName("ListScans")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapGet("/api/targets/{targetId:guid}/scans", async (
    Guid targetId,
    IScanService scanService,
    CancellationToken cancellationToken) =>
{
    var scans = await scanService.ListScansByTargetAsync(targetId, cancellationToken);
    return Results.Ok(scans.Select(scan => scan.ToListItemDto()));
})
.WithName("ListTargetScans")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapGet("/api/scans/{id:guid}", async (
    Guid id,
    IScanService scanService,
    CancellationToken cancellationToken) =>
{
    var scan = await scanService.GetScanAsync(id, cancellationToken);
    return scan is null ? Results.NotFound() : Results.Ok(scan.ToDto());
})
.WithName("GetScan")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapPost("/api/scans/{id:guid}/cancel", async (
    Guid id,
    IScanService scanService,
    CancellationToken cancellationToken) =>
{
    var cancelled = await scanService.CancelScanAsync(id, cancellationToken);
    return cancelled ? Results.Accepted($"/api/scans/{id}") : Results.NotFound();
})
.WithName("CancelScan")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapDelete("/api/scans/{id:guid}", async (
    Guid id,
    IScanService scanService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var deleted = await scanService.DeleteScanAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.WithName("DeleteScan")
.RequireAuthorization(AuthorizationPolicies.Admin);

static DatabaseEngine ParseEngine(string engine) =>
    DatabaseEngineExtensions.TryParseApiString(engine, out var parsed)
        && parsed != DatabaseEngine.Generic
        ? parsed
        : throw new InvalidOperationException("Unsupported database engine.");

static TargetConnectionMode ParseConnectionMode(string connectionMode) =>
    connectionMode.Trim().ToLowerInvariant() switch
    {
        "defaultvalues" => TargetConnectionMode.DefaultValues,
        "manualconnectionstring" => TargetConnectionMode.ManualConnectionString,
        _ => throw new InvalidOperationException("Unsupported target connection mode.")
    };

static IReadOnlyCollection<DatabaseEngine> ParseDiscoveryEngines(IReadOnlyList<string>? values)
{
    if (values is null || values.Count == 0)
    {
        return Array.Empty<DatabaseEngine>();
    }

    var engines = new List<DatabaseEngine>();
    foreach (var value in values)
    {
        if (!DatabaseEngineExtensions.TryParseApiString(value, out var engine)
            || engine is not (DatabaseEngine.MySql or DatabaseEngine.PostgreSql or DatabaseEngine.MsSql or DatabaseEngine.Oracle))
        {
            throw new InvalidOperationException("Discovery filter supports only MySQL, PostgreSQL, MSSQL and Oracle.");
        }

        engines.Add(engine);
    }

    return engines.Distinct().ToArray();
}

static DatabaseAuthentication? MapCredentials(DatabaseCredentialRequest? credentials) =>
    credentials is null
        ? null
        : new DatabaseAuthentication(
            credentials.Database,
            credentials.Username,
            credentials.Password,
            credentials.Encrypt,
            credentials.TrustServerCertificate,
            credentials.ConnectionTimeoutSeconds);

app.Run();
