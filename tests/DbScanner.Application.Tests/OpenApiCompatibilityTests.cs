using System.Net;
using System.Text.Json;
using DbScanner.Api.Contracts;
using DbScanner.Contracts.Scans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class OpenApiCompatibilityTests
{
    [Fact]
    public async Task Patched_OpenApi_library_generates_a_document_for_real_scan_contracts()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi();
        await using var app = builder.Build();
        app.MapPost("/api/scans", (CreateScanRequest request) => Results.Ok(new { request.TargetId }))
            .Produces<ScanDto>().WithName("CreateScan");
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/scans", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("ScanDto", out _));
        Assert.True(schemas.GetProperty("CreateScanRequest").GetProperty("properties").TryGetProperty("selectedCheckIds", out _));
    }
}
