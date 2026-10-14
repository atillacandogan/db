using DbScanner.Application;
using DbScanner.Api.Contracts;
using DbScanner.Api.Mapping;
using DbScanner.Api.Security;
using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.ScanChecks;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure;
using DbScanner.Infrastructure.Observability;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:3000", "http://127.0.0.1:3000"];

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddDbScannerTelemetry(builder.Configuration, "DbScanner.Api");
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

await app.Services.VerifyDbScannerTenantBeforeMigrationAsync();
if (builder.Configuration.GetValue("Persistence:ApplyMigrations", true))
{
    await app.Services.ApplyDbScannerMigrationsAsync();
}
await app.Services.VerifyDbScannerTenantAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("WebClient");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ResourceAccessMiddleware>();
app.MapResourceAccess();
app.MapHealthChecks("/health");

app.MapGet("/api/status", () => Results.Ok(new
{
    service = "DbScanner.Api",
    version = builder.Configuration["Telemetry:DeploymentVersion"]
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "unknown",
    instance = Environment.MachineName,
    environment = builder.Configuration["Telemetry:Environment"]
        ?? app.Environment.EnvironmentName,
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

app.MapGet("/api/findings/export", async (
    IFindingCatalogService findingCatalogService,
    CancellationToken cancellationToken) =>
{
    var findings = await findingCatalogService.ListAllAsync(cancellationToken);
    return Results.Ok(findings);
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("ExportFindings")
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
                request.NseScripts,
                request.ScanChecks),
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
                request.NseScripts,
                request.ScanChecks),
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
            new { error = exception.Message },
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

app.MapGet("/api/scan-checks", async (
    IScanCheckCatalogService scanCheckCatalogService,
    CancellationToken cancellationToken) =>
{
    var checks = await scanCheckCatalogService.ListAsync(cancellationToken);
    return Results.Ok(checks);
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("GetScanChecks")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapGet("/api/scan-checks/compatibility-readiness", async (
    IScanCompatibilityReadinessService readinessService,
    CancellationToken cancellationToken) =>
{
    var readiness = await readinessService.GetAsync(cancellationToken);
    return Results.Ok(readiness);
})
.Produces<ScanCompatibilityReadiness>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("GetScanCheckCompatibilityReadiness")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapGet("/api/scan-checks/executors", (IScanExecutorStrategyResolver resolver) =>
    Results.Ok(resolver.RegisteredCapabilities.Select(capability =>
        new ScanExecutorCapabilityDto(
            capability.ExecutorType,
            capability.DisplayName,
            capability.Description,
            capability.SupportedEngines
                .Select(engine => engine.ToApiString())
                .OrderBy(engine => engine, StringComparer.Ordinal)
                .ToArray(),
            capability.AllowsManualCheckCreation,
            capability.AllowsCheckNameEditing,
            capability.SupportsConfigurationSchema))))
.Produces<IReadOnlyList<ScanExecutorCapabilityDto>>(StatusCodes.Status200OK)
.WithName("GetScanExecutorCapabilities")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPost("/api/scan-checks", async (
    CreateScanCheckRequest request,
    IScanCheckCatalogService scanCheckCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var check = await scanCheckCatalogService.CreateAsync(
            new CreateScanCheckCommand(
                request.Id,
                request.Name,
                request.DisplayName,
                request.Description,
                request.ExecutorType,
                request.SupportedEngines,
                request.Categories,
                request.DefaultEnabled,
                request.RequiresCredentials,
                request.IsIntrusive,
                request.ConfigurationSchema,
                request.ParserType,
                request.MatcherType,
                request.MatcherExpression,
                request.Severity,
                request.Weight,
                request.RemediationTemplate,
                request.DocumentationUrl,
                request.ApprovalStatus),
            cancellationToken);

        return Results.Created($"/api/scan-checks/{check.Id}", check);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("CreateScanCheck")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPut("/api/scan-checks/{id}", async (
    string id,
    UpdateScanCheckRequest request,
    IScanCheckCatalogService scanCheckCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var check = await scanCheckCatalogService.UpdateAsync(
            id,
            new UpdateScanCheckCommand(
                request.Name,
                request.DisplayName,
                request.Description,
                request.ExecutorType,
                request.SupportedEngines,
                request.Categories,
                request.DefaultEnabled,
                request.RequiresCredentials,
                request.IsIntrusive,
                request.ConfigurationSchema,
                request.ParserType,
                request.MatcherType,
                request.MatcherExpression,
                request.Severity,
                request.Weight,
                request.RemediationTemplate,
                request.DocumentationUrl,
                request.ApprovalStatus),
            cancellationToken);

        return Results.Ok(check);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
})
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithName("UpdateScanCheck")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapDelete("/api/scan-checks/{id}", async (
    string id,
    IScanCheckCatalogService scanCheckCatalogService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var deleted = await scanCheckCatalogService.DeleteAsync(id, cancellationToken);
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
.WithName("DeleteScanCheck")
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
    catch (TargetAlreadyExistsException exception)
    {
        return Results.Json(
            new
            {
                code = TargetAlreadyExistsException.ErrorCode,
                message = TargetAlreadyExistsException.UserMessage,
                existingTargetId = exception.ExistingTargetId
            },
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "TARGET_VALIDATION_FAILED", message = exception.Message });
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
    catch (TargetAlreadyExistsException exception)
    {
        return Results.Json(
            new
            {
                code = TargetAlreadyExistsException.ErrorCode,
                message = TargetAlreadyExistsException.UserMessage,
                existingTargetId = exception.ExistingTargetId
            },
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "TARGET_VALIDATION_FAILED", message = exception.Message });
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

app.MapGet("/api/discovery-allowlist", async (
    string? environment,
    IDiscoveryAllowlistService allowlistService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await allowlistService.ListAsync(environment, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "ALLOWLIST_VALIDATION_FAILED", message = exception.Message });
    }
})
.WithName("ListDiscoveryAllowlist")
.RequireAuthorization(AuthorizationPolicies.Viewer);

app.MapPost("/api/discovery-allowlist", async (
    CreateDiscoveryAllowlistRequest request,
    IDiscoveryAllowlistService allowlistService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var created = await allowlistService.CreateAsync(ToAllowlistCommand(request), cancellationToken);
        return Results.Created($"/api/discovery-allowlist/{created.Id}", created);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "ALLOWLIST_VALIDATION_FAILED", message = exception.Message });
    }
})
.WithName("CreateDiscoveryAllowlist")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPut("/api/discovery-allowlist/{id:guid}", async (
    Guid id,
    UpdateDiscoveryAllowlistRequest request,
    IDiscoveryAllowlistService allowlistService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await allowlistService.UpdateAsync(id, ToUpdateAllowlistCommand(request), cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "ALLOWLIST_VALIDATION_FAILED", message = exception.Message });
    }
})
.WithName("UpdateDiscoveryAllowlist")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPatch("/api/discovery-allowlist/{id:guid}/active", async (
    Guid id,
    SetDiscoveryAllowlistActiveRequest request,
    IDiscoveryAllowlistService allowlistService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await allowlistService.SetActiveAsync(id, request.IsActive, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "ALLOWLIST_VALIDATION_FAILED", message = exception.Message });
    }
})
.WithName("SetDiscoveryAllowlistActive")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapDelete("/api/discovery-allowlist/{id:guid}", async (
    Guid id,
    IDiscoveryAllowlistService allowlistService,
    CancellationToken cancellationToken) =>
    await allowlistService.DeleteAsync(id, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound())
.WithName("DeleteDiscoveryAllowlist")
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

app.MapGet("/api/discoveries/local-networks", async (
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await discoveryService.ListLocalNetworksAsync(cancellationToken)))
.WithName("ListLocalDiscoveryNetworks")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapPost("/api/discoveries/local-network", async (
    CreateLocalNetworkDiscoveryRequest request,
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var job = await discoveryService.CreateLocalNetworkAsync(
            request.Name,
            request.Environment,
            ParseDiscoveryEngines(request.SupportedEngineFilter),
            request.Networks,
            cancellationToken);
        return Results.Accepted($"/api/discoveries/{job.Id}", job);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "LOCAL_NETWORK_DISCOVERY_FAILED", message = exception.Message });
    }
})
.WithName("CreateLocalNetworkDiscovery")
.RequireAuthorization(AuthorizationPolicies.Operator);

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

app.MapDelete("/api/discoveries/{id:guid}", async (
    Guid id,
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await discoveryService.DeleteAsync(id, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { code = "DISCOVERY_JOB_DELETE_BLOCKED", message = exception.Message });
    }
})
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict)
.WithName("DeleteDiscoveryJob")
.RequireAuthorization(AuthorizationPolicies.Operator);

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
                ParseDiscoveryEngines(request.SupportedEngineFilter),
                request.UseAuthorizedScope ?? string.IsNullOrWhiteSpace(request.Scope)),
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

app.MapPost("/api/discoveries/validate-scope", async (
    ValidateDiscoveryScopeRequest request,
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await discoveryService.ValidateScopeAsync(
            request.Scope,
            request.Environment,
            ParseDiscoveryEngines(request.SupportedEngineFilter),
            cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "DISCOVERY_SCOPE_VALIDATION_FAILED", message = exception.Message });
    }
})
.WithName("ValidateDiscoveryScope")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapPost("/api/discoveries/{id:guid}/targets", async (
    Guid id,
    AddDiscoveryTargetsRequest request,
    IDiscoveryService discoveryService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("DiscoveryTargetEndpoints");
    try
    {
        var results = await discoveryService.AddTargetsAsync(
            id,
            request.ResultIds,
            request.IsBulk,
            cancellationToken);
        logger.LogInformation(
            "Discovery target add completed for job {DiscoveryJobId}: {AddedCount} added, {ExistingCount} existing, {FailedCount} failed.",
            id,
            results.AddedCount,
            results.AlreadyExistingCount,
            results.FailedCount);
        return Results.Ok(results);
    }
    catch (TargetAlreadyExistsException exception)
    {
        logger.LogInformation(
            "Discovery target already exists for job {DiscoveryJobId}. Existing target: {ExistingTargetId}.",
            id,
            exception.ExistingTargetId);
        return Results.Json(
            new
            {
                code = TargetAlreadyExistsException.ErrorCode,
                message = TargetAlreadyExistsException.UserMessage,
                existingTargetId = exception.ExistingTargetId
            },
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (DiscoveryTargetNotSupportedException)
    {
        logger.LogInformation(
            "Unsupported discovery result was rejected for job {DiscoveryJobId}.",
            id);
        return Results.BadRequest(new
        {
            code = DiscoveryTargetNotSupportedException.ErrorCode,
            message = DiscoveryTargetNotSupportedException.UserMessage
        });
    }
    catch (InvalidOperationException exception)
    {
        logger.LogWarning(exception, "Discovery target add failed validation for job {DiscoveryJobId}.", id);
        return Results.BadRequest(new { code = "DISCOVERY_TARGET_ADD_FAILED", message = exception.Message });
    }
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status409Conflict)
.WithName("AddDiscoveryTargets")
.RequireAuthorization(AuthorizationPolicies.Operator);

app.MapPost("/api/discoveries/{id:guid}/allowlist", async (
    Guid id,
    ApproveDiscoveryAllowlistRequest request,
    IDiscoveryService discoveryService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await discoveryService.ApproveAllowlistAsync(
            id,
            request.ResultIds,
            cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { code = "DISCOVERY_ALLOWLIST_APPROVAL_FAILED", message = exception.Message });
    }
})
.WithName("ApproveDiscoveryAllowlist")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapGet("/api/admin/users", async (
    string? search,
    string? role,
    bool? enabled,
    int? page,
    int? pageSize,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await adminUserService.ListAsync(
            search,
            role,
            enabled,
            page ?? 1,
            pageSize ?? 20,
            cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("ListAdminUsers")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPost("/api/admin/users", async (
    CreateAdminUserRequest request,
    HttpContext httpContext,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await adminUserService.CreateAsync(
            new CreateAdminUserCommand(
                request.Username,
                request.FirstName,
                request.LastName,
                request.Email,
                request.Role,
                request.RequireMfa,
                request.TemporaryPassword),
            GetAdminUserActor(httpContext.User),
            cancellationToken);
        return Results.Created($"/api/admin/users/{result.User.Id}", result);
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("CreateAdminUser")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapGet("/api/admin/users/{id}", async (
    string id,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var user = await adminUserService.GetAsync(id, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("GetAdminUser")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPut("/api/admin/users/{id}", async (
    string id,
    UpdateAdminUserRequest request,
    HttpContext httpContext,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var user = await adminUserService.UpdateAsync(
            id,
            new UpdateAdminUserCommand(request.FirstName, request.LastName, request.Email),
            GetAdminUserActor(httpContext.User),
            cancellationToken);
        return Results.Ok(user);
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("UpdateAdminUser")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPut("/api/admin/users/{id}/roles", async (
    string id,
    UpdateAdminUserRoleRequest request,
    HttpContext httpContext,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var user = await adminUserService.ChangeRoleAsync(
            id,
            request.Role,
            GetAdminUserActor(httpContext.User),
            cancellationToken);
        return Results.Ok(user);
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("UpdateAdminUserRole")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPatch("/api/admin/users/{id}/status", async (
    string id,
    UpdateAdminUserStatusRequest request,
    HttpContext httpContext,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var user = await adminUserService.ChangeStatusAsync(
            id,
            request.Enabled,
            GetAdminUserActor(httpContext.User),
            cancellationToken);
        return Results.Ok(user);
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("UpdateAdminUserStatus")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPut("/api/admin/users/{id}/mfa", async (
    string id,
    UpdateAdminUserMfaRequest request,
    HttpContext httpContext,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var user = await adminUserService.ChangeMfaRequirementAsync(
            id,
            request.Required,
            GetAdminUserActor(httpContext.User),
            cancellationToken);
        return Results.Ok(user);
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("UpdateAdminUserMfa")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPost("/api/admin/users/{id}/reset-password", async (
    string id,
    HttpContext httpContext,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await adminUserService.RequestPasswordResetAsync(
            id,
            GetAdminUserActor(httpContext.User),
            cancellationToken);
        return Results.NoContent();
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("ResetAdminUserPassword")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPost("/api/admin/users/{id}/logout", async (
    string id,
    HttpContext httpContext,
    IAdminUserService adminUserService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await adminUserService.LogoutAsync(
            id,
            GetAdminUserActor(httpContext.User),
            cancellationToken);
        return Results.NoContent();
    }
    catch (Exception exception) when (IsAdminUserException(exception))
    {
        return MapAdminUserException(exception);
    }
})
.WithName("LogoutAdminUserSessions")
.RequireAuthorization(AuthorizationPolicies.Admin);

app.MapPost("/api/scans", async (
    CreateScanRequest request,
    IScanService scanService,
    DbScannerDbContext database,
    CancellationToken cancellationToken) =>
{
    try
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var visible = await database.Targets.FromSqlInterpolated($"SELECT * FROM targets WHERE \"Id\" = {request.TargetId} FOR UPDATE")
            .AnyAsync(cancellationToken);
        if (!visible) return Results.NotFound();
        var scan = await scanService.CreateScanAsync(
            request.TargetId,
            ScanCheckSelectionCompatibility.Resolve(
                request.SelectedCheckIds,
                request.SelectedNseScriptIds),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

app.MapGet("/api/scans/{id:guid}/export", async (Guid id, IScanService scanService, CancellationToken cancellationToken) =>
{
    var scan = await scanService.GetScanAsync(id, cancellationToken);
    return scan is null ? Results.NotFound() : Results.Ok(scan.ToDto());
}).WithName("ExportScan").RequireAuthorization(AuthorizationPolicies.Viewer);

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

static UpsertDiscoveryAllowlistCommand ToAllowlistCommand(CreateDiscoveryAllowlistRequest request) =>
    new(
        request.Name,
        request.Environment,
        request.TargetType,
        request.TargetValue,
        request.AllowedEngines,
        request.AllowedPorts,
        request.IsActive,
        request.Description);

static UpsertDiscoveryAllowlistCommand ToUpdateAllowlistCommand(UpdateDiscoveryAllowlistRequest request) =>
    new(
        request.Name,
        request.Environment,
        request.TargetType,
        request.TargetValue,
        request.AllowedEngines,
        request.AllowedPorts,
        request.IsActive,
        request.Description);

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

static AdminUserActor GetAdminUserActor(System.Security.Claims.ClaimsPrincipal principal) =>
    new(
        principal.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated user subject is required."),
        principal.FindFirst("preferred_username")?.Value ?? principal.Identity?.Name,
        principal.FindFirst("email")?.Value);

static bool IsAdminUserException(Exception exception) => exception is
    InvalidOperationException
    or IdentityManagementUnavailableException
    or IdentityEmailDeliveryException;

static IResult MapAdminUserException(Exception exception) => exception switch
{
    IdentityManagementUnavailableException => Results.Json(
        new
        {
            code = "IDENTITY_SERVICE_UNAVAILABLE",
            message = "Kimlik yönetimi servisine şu anda ulaşılamıyor."
        },
        statusCode: StatusCodes.Status503ServiceUnavailable),
    IdentityEmailDeliveryException => Results.Json(
        new
        {
            code = "IDENTITY_EMAIL_UNAVAILABLE",
            message = "Kimlik yönetimi e-posta servisine şu anda ulaşılamıyor."
        },
        statusCode: StatusCodes.Status503ServiceUnavailable),
    IdentityUserNotFoundException => Results.Json(
        new { code = "IDENTITY_USER_NOT_FOUND", message = "Kullanıcı bulunamadı." },
        statusCode: StatusCodes.Status404NotFound),
    IdentityManagementConflictException conflict => Results.Json(
        new { code = "IDENTITY_USER_CONFLICT", message = conflict.Message },
        statusCode: StatusCodes.Status409Conflict),
    AdminUserOperationNotAllowedException blocked => Results.Json(
        new { code = "ADMIN_USER_OPERATION_BLOCKED", message = blocked.Message },
        statusCode: StatusCodes.Status409Conflict),
    InvalidOperationException validation => Results.Json(
        new { code = "ADMIN_USER_VALIDATION_FAILED", message = validation.Message },
        statusCode: StatusCodes.Status400BadRequest),
    _ => Results.Json(
        new { code = "ADMIN_USER_OPERATION_FAILED", message = "Kullanıcı yönetimi işlemi tamamlanamadı." },
        statusCode: StatusCodes.Status500InternalServerError)
};

app.Run();

public partial class Program;
