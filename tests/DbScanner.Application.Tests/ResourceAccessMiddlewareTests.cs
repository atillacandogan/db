using System.Security.Claims;
using DbScanner.Api.Security;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ResourceAccessMiddlewareTests
{
    [Theory]
    [InlineData("GET", "/api/targets", 1)]
    [InlineData("GET", "/api/findings/export", 9)]
    [InlineData("GET", "/api/scans/one/export", 9)]
    [InlineData("GET", "/API/SCANS/one/EXPORT/", 9)]
    [InlineData("POST", "/api/scans", 3)]
    [InlineData("POST", "/api/scans/one/cancel", 3)]
    [InlineData("POST", "/API/SCANS/one/CANCEL/", 3)]
    [InlineData("PUT", "/api/targets/one", 5)]
    public void Endpoints_require_the_correct_resource_permission(string method, string path, int permission) =>
        Assert.Equal((TargetPermission)permission, ResourceAccessMiddleware.PermissionFor(method, path));

    [Theory]
    [InlineData("dbscanner-admin", null, 403)]
    [InlineData("dbscanner-admin", "other", 403)]
    [InlineData("dbscanner-admin", "customer-a", 200)]
    [InlineData("dbscanner-operator", "customer-a", 200)]
    [InlineData("dbscanner-viewer", "customer-a", 200)]
    public async Task Even_admin_cannot_cross_tenants_or_spoof_a_header(string role, string? tenantClaim, int expected)
    {
        var http = Context(role, "/api/targets", tenantClaim);
        http.Request.Headers["X-Tenant-Id"] = "customer-a"; // selector is NOT an authorization source
        var access = new ResourceAccessContext();
        var called = false;
        await new ResourceAccessMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(http, access,
            Options.Create(new TenantIsolationOptions { TenantId = "customer-a", DedicatedTenant = true }));
        Assert.Equal(expected, http.Response.StatusCode);
        Assert.Equal(expected == 200, called);
    }

    [Theory]
    [InlineData("dbscanner-viewer", "/api/discoveries", false, 403)]
    [InlineData("dbscanner-operator", "/api/discovery-allowlist", false, 403)]
    [InlineData("dbscanner-admin", "/api/admin/users", true, 403)]
    [InlineData("dbscanner-admin", "/API/ADMIN/USERS/", true, 403)]
    [InlineData("dbscanner-operator", "/API/DISCOVERIES/", false, 403)]
    [InlineData("dbscanner-admin", "/api/admin/users", false, 200)]
    [InlineData("dbscanner-admin", "/api/discoveries/local-networks", true, 403)]
    public async Task Network_inventory_and_shared_identity_admin_are_not_bypass_paths(string role, string path, bool dedicated, int expected)
    {
        var http = Context(role, path, "customer-a");
        var called = false;
        await new ResourceAccessMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(http, new(),
            Options.Create(new TenantIsolationOptions { TenantId = "customer-a", DedicatedTenant = dedicated }));
        Assert.Equal(expected, http.Response.StatusCode);
        Assert.Equal(expected == 200, called);
    }

    [Fact]
    public async Task Anonymous_requests_cannot_establish_a_resource_context()
    {
        var http = new DefaultHttpContext(); http.Request.Path = "/api/targets";
        var called = false;
        await new ResourceAccessMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(http, new(), Options.Create(new TenantIsolationOptions()));
        Assert.Equal(401, http.Response.StatusCode);
        Assert.False(called);
    }

    private static DefaultHttpContext Context(string role, string path, string? tenantClaim)
    {
        var claims = new List<Claim> { new("sub", "alice"), new("roles", role) };
        if (tenantClaim is not null) claims.Add(new Claim("dbscanner_tenant", tenantClaim));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test-only", null, "roles")) };
        http.Request.Path = path; http.Request.Method = "GET"; http.Response.Body = new MemoryStream();
        return http;
    }
}
