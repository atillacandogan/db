using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using DbScanner.Api.Security;
using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Identity;
using DbScanner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ResourceAccessIdentityEndpointTests
{
    [Fact]
    public async Task Project_details_include_only_its_targets_and_grants_and_never_target_credentials()
    {
        await using var host = await Harness.Start();
        await host.EditDb(db =>
        {
            var other = new AccessProject { Name = "Other" };
            db.AddRange(other,
                new Target { Name = "Other target", ProjectId = other.Id, Host = "hidden.internal", Port = 5432, Engine = DatabaseEngine.PostgreSql },
                new AccessGrant { TeamId = host.TeamId, ProjectId = host.ProjectId, Permissions = TargetPermission.Read },
                new AccessGrant { TeamId = host.TeamId, ProjectId = other.Id, Permissions = TargetPermission.All });
            return Task.CompletedTask;
        });
        using var response = await host.Client.GetAsync($"/api/access/projects/{host.ProjectId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(host.ProjectId, json.RootElement.GetProperty("project").GetProperty("id").GetGuid());
        Assert.False(json.RootElement.GetProperty("project").GetProperty("isDefault").GetBoolean());
        Assert.Equal(host.TargetId, Assert.Single(json.RootElement.GetProperty("targets").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(host.ProjectId, Assert.Single(json.RootElement.GetProperty("grants").EnumerateArray()).GetProperty("projectId").GetGuid());
        Assert.Single(json.RootElement.GetProperty("teams").EnumerateArray());
        Assert.DoesNotContain("hidden.internal", body);
        Assert.DoesNotContain("connectionString", body);
        Assert.DoesNotContain("host", body);
        Assert.DoesNotContain("password", body);
    }

    [Fact]
    public async Task Project_rename_is_trimmed_audited_and_noop_does_not_add_history()
    {
        await using var host = await Harness.Start();
        using var renamed = await host.Client.PutAsJsonAsync($"/api/access/projects/{host.ProjectId}", new { name = "  Renamed project  " });
        using var noop = await host.Client.PutAsJsonAsync($"/api/access/projects/{host.ProjectId}", new { name = "Renamed project" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        Assert.Equal("Renamed project", await host.ReadDb(db => db.AccessProjects.Where(p => p.Id == host.ProjectId).Select(p => p.Name).SingleAsync()));
        using var audit = await host.Client.GetAsync($"/api/access/audit?entityType=project&entityId={host.ProjectId}");
        using var json = JsonDocument.Parse(await audit.Content.ReadAsStringAsync());
        var row = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("project.renamed", row.GetProperty("action").GetString());
        Assert.Equal("test-admin", row.GetProperty("actorSubject").GetString());
        Assert.Contains("Test project", row.GetProperty("beforeJson").GetString());
        Assert.Contains("Renamed project", row.GetProperty("afterJson").GetString());
        Assert.True(row.GetProperty("teamId").ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invalid_project_name_does_not_change_project_or_audit(string? name)
    {
        await using var host = await Harness.Start();
        using var response = await host.Client.PutAsJsonAsync($"/api/access/projects/{host.ProjectId}", new { name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Test project", await host.ReadDb(db => db.AccessProjects.Where(p => p.Id == host.ProjectId).Select(p => p.Name).SingleAsync()));
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
    }

    [Fact]
    public async Task Project_name_validation_allows_160_trimmed_characters_but_not_161()
    {
        await using var host = await Harness.Start();
        using var invalid = await host.Client.PutAsJsonAsync($"/api/access/projects/{host.ProjectId}", new { name = new string('x', 161) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
        using var valid = await host.Client.PutAsJsonAsync($"/api/access/projects/{host.ProjectId}", new { name = " " + new string('x', 160) + " " });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task Default_project_can_be_renamed_but_cannot_be_deleted_even_without_dependents()
    {
        await using var host = await Harness.Start();
        using var detail = await host.Client.GetAsync($"/api/access/projects/{AccessProject.DefaultId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("project").GetProperty("isDefault").GetBoolean());
        using var rename = await host.Client.PutAsJsonAsync($"/api/access/projects/{AccessProject.DefaultId}", new { name = "Default renamed" });
        using var deleted = await host.Client.DeleteAsync($"/api/access/projects/{AccessProject.DefaultId}");
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Contains("PROJECT_DEFAULT", await deleted.Content.ReadAsStringAsync());
        Assert.True(await host.ReadDb(db => db.AccessProjects.AnyAsync(p => p.Id == AccessProject.DefaultId)));
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync(a => a.Action == "project.deleted")));
    }

    [Fact]
    public async Task Empty_project_deletion_is_audited_once_and_missing_project_delete_is_idempotent()
    {
        await using var host = await Harness.Start();
        var project = new AccessProject { Name = "Disposable empty project" };
        await host.EditDb(db => { db.AccessProjects.Add(project); return Task.CompletedTask; });
        using var first = await host.Client.DeleteAsync($"/api/access/projects/{project.Id}");
        using var second = await host.Client.DeleteAsync($"/api/access/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.False(await host.ReadDb(db => db.AccessProjects.AnyAsync(p => p.Id == project.Id)));
        var audit = await host.ReadDb(db => db.ResourceAccessAudits.SingleAsync(a => a.EntityId == project.Id.ToString()));
        Assert.Equal("project.deleted", audit.Action);
        Assert.Contains("Disposable empty project", audit.BeforeJson);
        Assert.Null(audit.AfterJson);
        Assert.Equal("test-admin", audit.ActorSubject);
        Assert.True(await host.ReadDb(db => db.AccessProjects.AnyAsync(p => p.Id == host.ProjectId)));
    }

    [Fact]
    public async Task Project_with_target_cannot_be_deleted_and_target_and_scan_history_are_preserved()
    {
        await using var host = await Harness.Start();
        var scan = new Scan { TargetId = host.TargetId };
        await host.EditDb(db => { db.Scans.Add(scan); return Task.CompletedTask; });
        using var deleted = await host.Client.DeleteAsync($"/api/access/projects/{host.ProjectId}");
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Contains("PROJECT_HAS_TARGETS", await deleted.Content.ReadAsStringAsync());
        Assert.True(await host.ReadDb(db => db.AccessProjects.AnyAsync(p => p.Id == host.ProjectId)));
        Assert.True(await host.ReadDb(db => db.Targets.AnyAsync(t => t.Id == host.TargetId)));
        Assert.True(await host.ReadDb(db => db.Scans.AnyAsync(s => s.Id == scan.Id)));
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_with_team_grant_cannot_be_deleted_even_if_team_is_archived(bool archived)
    {
        await using var host = await Harness.Start();
        var empty = new AccessProject { Name = "Granted empty project" };
        var grant = new AccessGrant { TeamId = host.TeamId, ProjectId = empty.Id, Permissions = TargetPermission.Read };
        await host.EditDb(async db =>
        {
            db.AddRange(empty, grant);
            (await db.AccessTeams.SingleAsync(t => t.Id == host.TeamId)).IsArchived = archived;
        });
        using var deleted = await host.Client.DeleteAsync($"/api/access/projects/{empty.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Contains("PROJECT_HAS_GRANTS", await deleted.Content.ReadAsStringAsync());
        Assert.True(await host.ReadDb(db => db.AccessProjects.AnyAsync(p => p.Id == empty.Id)));
        Assert.True(await host.ReadDb(db => db.AccessGrants.AnyAsync(g => g.Id == grant.Id)));
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
    }

    [Fact]
    public async Task Missing_project_details_and_rename_are_not_found_without_audit()
    {
        await using var host = await Harness.Start();
        var id = Guid.NewGuid();
        using var detail = await host.Client.GetAsync($"/api/access/projects/{id}");
        using var rename = await host.Client.PutAsJsonAsync($"/api/access/projects/{id}", new { name = "Name" });
        using var deleted = await host.Client.DeleteAsync($"/api/access/projects/{id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("dbscanner-viewer", HttpStatusCode.Forbidden)]
    [InlineData("dbscanner-operator", HttpStatusCode.Forbidden)]
    public async Task Project_details_rename_and_delete_are_admin_only(string? role, HttpStatusCode expected)
    {
        await using var host = await Harness.Start(role: role);
        using var detail = await host.Client.GetAsync($"/api/access/projects/{host.ProjectId}");
        using var rename = await host.Client.PutAsJsonAsync($"/api/access/projects/{host.ProjectId}", new { name = "Forbidden" });
        using var deleted = await host.Client.DeleteAsync($"/api/access/projects/{host.ProjectId}");
        Assert.Equal(expected, detail.StatusCode);
        Assert.Equal(expected, rename.StatusCode);
        Assert.Equal(expected, deleted.StatusCode);
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
    }

    [Fact]
    public async Task Audit_entity_filters_do_not_include_other_project_or_team_changes()
    {
        await using var host = await Harness.Start();
        using var one = await host.Client.PutAsJsonAsync($"/api/access/projects/{host.ProjectId}", new { name = "Project one" });
        using var two = await host.Client.PutAsJsonAsync($"/api/access/projects/{AccessProject.DefaultId}", new { name = "Project two" });
        using var three = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}", new { name = "Team" });
        using var selected = await host.Client.GetAsync($"/api/access/audit?entityType=project&entityId={host.ProjectId}&action=project.renamed");
        using var json = JsonDocument.Parse(await selected.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        var row = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(host.ProjectId.ToString(), row.GetProperty("entityId").GetString());
        using var mismatched = await host.Client.GetAsync($"/api/access/audit?entityType=team&entityId={host.ProjectId}");
        using var mismatchJson = JsonDocument.Parse(await mismatched.Content.ReadAsStringAsync());
        Assert.Equal(0, mismatchJson.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("entityType", 33)]
    [InlineData("entityId", 257)]
    [InlineData("action", 65)]
    public async Task Oversized_audit_filters_are_rejected(string field, int length)
    {
        await using var host = await Harness.Start();
        using var response = await host.Client.GetAsync($"/api/access/audit?{field}={new string('x', length)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Grant_reference_disappearing_after_validation_returns_sanitized_conflict_without_audit()
    {
        var interceptor = new ForeignKeyFailureInterceptor();
        await using var host = await Harness.Start(interceptor: interceptor);
        interceptor.Enabled = true;
        using var response = await host.Client.PutAsJsonAsync("/api/access/grants",
            new { teamId = host.TeamId, projectId = host.ProjectId, permissions = 1 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PROJECT_CONCURRENT_CHANGE", body);
        Assert.DoesNotContain("raw-provider-secret", body);
        Assert.Equal(0, await host.ReadDb(db => db.AccessGrants.CountAsync()));
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
    }

    [Theory]
    [InlineData("dbscanner-viewer", true, 15, false, 9)]
    [InlineData("dbscanner-operator", true, 15, false, 15)]
    [InlineData("dbscanner-viewer", true, 3, false, 1)]
    [InlineData("dbscanner-operator", true, 15, true, 0)]
    [InlineData("dbscanner-operator", true, 0, false, 0)]
    [InlineData("dbscanner-admin", true, 0, false, 15)]
    [InlineData("dbscanner-admin", false, 15, false, 0)]
    [InlineData(null, true, 15, false, 0)]
    public async Task Preview_matches_current_role_grant_and_archive_state(string? role, bool enabled, int grant, bool archived, int expected)
    {
        await using var host = await Harness.Start();
        host.Identity.CurrentUser = IdentityStub.User with { Role = role, Enabled = enabled };
        await host.EditDb(async db =>
        {
            (await db.AccessTeams.SingleAsync(t => t.Id == host.TeamId)).IsArchived = archived;
            db.AccessTeamMembers.Add(new AccessTeamMember { TeamId = host.TeamId, Subject = IdentityStub.User.Id });
            if (grant != 0) db.AccessGrants.Add(new AccessGrant { TeamId = host.TeamId, ProjectId = host.ProjectId, Permissions = (TargetPermission)grant });
        });
        using var response = await host.Client.GetAsync($"/api/access/preview?subject={IdentityStub.User.Id}&targetId={host.TargetId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("effectivePermissions").GetInt32());
        Assert.Equal(4, json.RootElement.GetProperty("decisions").GetArrayLength());
        if (enabled)
        {
            foreach (var permission in new[] { TargetPermission.Read, TargetPermission.Scan, TargetPermission.Manage, TargetPermission.Export })
                Assert.Equal(((TargetPermission)expected).HasFlag(permission), await host.CanReadTarget(role, permission));
        }
    }

    [Fact]
    public async Task Multiple_team_sources_combine_but_other_project_and_target_grants_do_not()
    {
        await using var host = await Harness.Start();
        host.Identity.CurrentUser = IdentityStub.User with { Role = "dbscanner-operator" };
        await host.EditDb(db =>
        {
            var second = new AccessTeam { Name = "Second" };
            var otherProject = new AccessProject { Name = "Other" };
            db.AddRange(second, otherProject,
                new AccessTeamMember { TeamId = host.TeamId, Subject = IdentityStub.User.Id },
                new AccessTeamMember { TeamId = second.Id, Subject = IdentityStub.User.Id },
                new AccessGrant { TeamId = host.TeamId, ProjectId = host.ProjectId, Permissions = TargetPermission.Read | TargetPermission.Scan },
                new AccessGrant { TeamId = second.Id, ProjectId = host.ProjectId, TargetId = host.TargetId, Permissions = TargetPermission.Read | TargetPermission.Export },
                new AccessGrant { TeamId = second.Id, ProjectId = otherProject.Id, Permissions = TargetPermission.All });
            return Task.CompletedTask;
        });
        using var response = await host.Client.GetAsync($"/api/access/preview?subject={IdentityStub.User.Id}&targetId={host.TargetId}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(11, json.RootElement.GetProperty("effectivePermissions").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public async Task Changes_have_actor_and_before_after_audit_noops_and_validation_errors_do_not()
    {
        await using var host = await Harness.Start();
        using var member = await host.AddMember(IdentityStub.User.Id);
        using var duplicate = await host.AddMember(IdentityStub.User.Id);
        using var rename = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}", new { name = "Renamed" });
        using var invalid = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}", new { name = " " });
        using var archive = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/archive", new { archived = true });
        using var archiveAgain = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/archive", new { archived = true });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var response = await host.Client.GetAsync($"/api/access/audit?teamId={host.TeamId}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Equal("test-admin", row.GetProperty("actorSubject").GetString()));
        var change = Assert.Single(rows, row => row.GetProperty("action").GetString() == "team.renamed");
        Assert.Contains("Test team", change.GetProperty("beforeJson").GetString());
        Assert.Contains("Renamed", change.GetProperty("afterJson").GetString());
        Assert.DoesNotContain("private@example.test", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await host.MemberCount());
        using var detail = await host.Client.GetAsync($"/api/access/teams/{host.TeamId}");
        Assert.Contains("\"isArchived\":true", await detail.Content.ReadAsStringAsync());
        using var blockedMember = await host.AddMember(IdentityStub.User.Id);
        using var blockedBatch = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/members/batch", new { subjects = new[] { IdentityStub.User.Id } });
        Assert.Equal(HttpStatusCode.Conflict, blockedMember.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, blockedBatch.StatusCode);
        using var blockedGrant = await host.Client.PutAsJsonAsync("/api/access/grants", new { teamId = host.TeamId, projectId = host.ProjectId, permissions = 1 });
        Assert.Equal(HttpStatusCode.Conflict, blockedGrant.StatusCode);
    }

    [Fact]
    public async Task Grant_create_update_and_revoke_have_exact_snapshots_and_no_duplicate_audit()
    {
        await using var host = await Harness.Start();
        using var created = await host.Client.PutAsJsonAsync("/api/access/grants", new { teamId = host.TeamId, projectId = host.ProjectId, permissions = 3 });
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString();
        using var duplicate = await host.Client.PutAsJsonAsync("/api/access/grants", new { teamId = host.TeamId, projectId = host.ProjectId, permissions = 3 });
        using var updated = await host.Client.PutAsJsonAsync("/api/access/grants", new { teamId = host.TeamId, projectId = host.ProjectId, permissions = 9 });
        using var removed = await host.Client.DeleteAsync($"/api/access/grants/{id}");
        using var removedAgain = await host.Client.DeleteAsync($"/api/access/grants/{id}");
        using var audit = await host.Client.GetAsync($"/api/access/audit?teamId={host.TeamId}");
        using var auditJson = JsonDocument.Parse(await audit.Content.ReadAsStringAsync());
        var rows = auditJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        var change = Assert.Single(rows, row => row.GetProperty("action").GetString() == "grant.updated");
        Assert.Contains("\"permissions\":3", change.GetProperty("beforeJson").GetString());
        Assert.Contains("\"permissions\":9", change.GetProperty("afterJson").GetString());
    }

    [Fact]
    public async Task Restore_and_membership_removal_are_audited_once_and_recompute_preview()
    {
        await using var host = await Harness.Start();
        host.Identity.CurrentUser = IdentityStub.User with { Role = "dbscanner-viewer" };
        using var member = await host.AddMember(IdentityStub.User.Id);
        using var grant = await host.Client.PutAsJsonAsync("/api/access/grants", new { teamId = host.TeamId, projectId = host.ProjectId, permissions = 9 });
        using var archive = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/archive", new { archived = true });
        Assert.False(await host.CanReadTarget("dbscanner-viewer", TargetPermission.Read));
        using var restore = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/archive", new { archived = false });
        using var restoreAgain = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/archive", new { archived = false });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.True(await host.CanReadTarget("dbscanner-viewer", TargetPermission.Export));
        using var restoredAudit = await host.Client.GetAsync($"/api/access/audit?teamId={host.TeamId}&action=team.restored");
        using var restoredJson = JsonDocument.Parse(await restoredAudit.Content.ReadAsStringAsync());
        Assert.Equal(1, restoredJson.RootElement.GetProperty("totalCount").GetInt32());
        using var removed = await host.Client.DeleteAsync($"/api/access/teams/{host.TeamId}/members/{IdentityStub.User.Id}");
        using var removedAgain = await host.Client.DeleteAsync($"/api/access/teams/{host.TeamId}/members/{IdentityStub.User.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0, await host.MemberCount());
        Assert.False(await host.CanReadTarget("dbscanner-viewer", TargetPermission.Read));
        using var preview = await host.Client.GetAsync($"/api/access/preview?subject={IdentityStub.User.Id}&targetId={host.TargetId}");
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        Assert.Equal(0, previewJson.RootElement.GetProperty("effectivePermissions").GetInt32());
        using var removedAudit = await host.Client.GetAsync($"/api/access/audit?teamId={host.TeamId}&action=member.removed");
        using var removedJson = JsonDocument.Parse(await removedAudit.Content.ReadAsStringAsync());
        Assert.Equal(1, removedJson.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Creation_history_is_paginated_and_filtered_without_leaking_other_team_history()
    {
        await using var host = await Harness.Start();
        using var created = await host.Client.PostAsJsonAsync("/api/access/teams", new { name = "New team" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        for (var index = 0; index < 22; index++)
        {
            using var changed = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}", new { name = $"Version {index}" });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }
        using var first = await host.Client.GetAsync($"/api/access/audit?teamId={host.TeamId}&action=team.renamed&page=1");
        using var second = await host.Client.GetAsync($"/api/access/audit?teamId={host.TeamId}&action=team.renamed&page=2");
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(22, firstJson.RootElement.GetProperty("totalCount").GetInt32());
        var firstRows = firstJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var secondRows = secondJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(20, firstRows.Length);
        Assert.Equal(2, secondRows.Length);
        Assert.Empty(firstRows.Select(row => row.GetProperty("id").GetString()).Intersect(secondRows.Select(row => row.GetProperty("id").GetString())));
        using var invalid = await host.Client.GetAsync("/api/access/audit?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var creation = await host.Client.GetAsync("/api/access/audit?action=team.created");
        using var creationJson = JsonDocument.Parse(await creation.Content.ReadAsStringAsync());
        Assert.Equal(1, creationJson.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("dbscanner-viewer", HttpStatusCode.Forbidden)]
    [InlineData("dbscanner-operator", HttpStatusCode.Forbidden)]
    public async Task Insights_and_team_changes_are_admin_only(string? role, HttpStatusCode expected)
    {
        await using var host = await Harness.Start(role: role);
        foreach (var path in new[] { "/api/access/audit", $"/api/access/teams/{host.TeamId}", $"/api/access/preview?subject={IdentityStub.User.Id}&targetId={host.TargetId}" })
        { using var response = await host.Client.GetAsync(path); Assert.Equal(expected, response.StatusCode); }
        using var rename = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}", new { name = "Not allowed" });
        using var archive = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/archive", new { archived = true });
        Assert.Equal(expected, rename.StatusCode); Assert.Equal(expected, archive.StatusCode);
    }

    [Fact]
    public async Task Preview_is_fail_closed_when_tenant_directory_is_not_configured_or_identity_is_unavailable()
    {
        await using var dedicated = await Harness.Start(dedicated: true);
        using var blocked = await dedicated.Client.GetAsync($"/api/access/preview?subject={IdentityStub.User.Id}&targetId={dedicated.TargetId}");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(0, dedicated.Identity.GetCalls);
        await using var local = await Harness.Start();
        local.Identity.Fail = true;
        using var unavailable = await local.Client.GetAsync($"/api/access/preview?subject={IdentityStub.User.Id}&targetId={local.TargetId}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("raw-provider-secret", await unavailable.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Search_returns_provider_ids_and_names_without_email_or_credentials()
    {
        await using var host = await Harness.Start();
        using var response = await host.Client.GetAsync("/api/access/users?search=%20ada%20&page=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var user = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(IdentityStub.User.Id, user.GetProperty("id").GetString());
        Assert.Equal("ada", user.GetProperty("username").GetString());
        Assert.False(user.TryGetProperty("email", out _));
        Assert.False(user.TryGetProperty("mfaConfigured", out _));
        Assert.Equal("ada", host.Identity.Search);
        Assert.Equal(2, host.Identity.Page);
        Assert.Equal(10, host.Identity.PageSize);
    }

    [Fact]
    public async Task Full_uuid_search_looks_up_the_actual_user_not_the_username_search()
    {
        await using var host = await Harness.Start();
        using var response = await host.Client.GetAsync($"/api/access/users?search={IdentityStub.User.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(IdentityStub.User.Id, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Identity.ListCalls);
        Assert.Equal(1, host.Identity.GetCalls);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("dbscanner-viewer", HttpStatusCode.Forbidden)]
    [InlineData("dbscanner-operator", HttpStatusCode.Forbidden)]
    public async Task Only_admin_can_search_or_add_members(string? role, HttpStatusCode expected)
    {
        await using var host = await Harness.Start(role: role);
        using var lookup = await host.Client.GetAsync("/api/access/users");
        using var membership = await host.AddMember(IdentityStub.User.Id);
        using var batch = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/members/batch", new { subjects = new[] { IdentityStub.User.Id } });
        Assert.Equal(expected, lookup.StatusCode);
        Assert.Equal(expected, membership.StatusCode);
        Assert.Equal(expected, batch.StatusCode);
        Assert.Equal(0, host.Identity.ListCalls + host.Identity.GetCalls);
        Assert.Equal(0, await host.MemberCount());
    }

    [Fact]
    public async Task Shared_realm_directory_is_not_exposed_to_dedicated_tenant_admin()
    {
        await using var host = await Harness.Start(dedicated: true);
        using var lookup = await host.Client.GetAsync("/api/access/users");
        using var membership = await host.AddMember(IdentityStub.User.Id);
        using var batch = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/members/batch", new { subjects = new[] { IdentityStub.User.Id } });
        Assert.Equal(HttpStatusCode.Forbidden, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, membership.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, batch.StatusCode);
        Assert.Equal(0, host.Identity.ListCalls + host.Identity.GetCalls);
        Assert.Equal(0, await host.MemberCount());
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("disabled", HttpStatusCode.Conflict)]
    [InlineData(" ada ", HttpStatusCode.BadRequest)]
    [InlineData("", HttpStatusCode.BadRequest)]
    public async Task Invalid_or_deleted_or_disabled_identity_cannot_create_membership(string subject, HttpStatusCode expected)
    {
        await using var host = await Harness.Start();
        using var response = await host.AddMember(subject);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, await host.MemberCount());
    }

    [Fact]
    public async Task Actual_identity_is_stored_by_immutable_id_and_add_is_idempotent()
    {
        await using var host = await Harness.Start();
        using var first = await host.AddMember(IdentityStub.User.Id);
        using var second = await host.AddMember(IdentityStub.User.Id);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(1, await host.MemberCount());
        Assert.Equal(2, host.Identity.GetCalls);
        // Selection is stale if the identity is subsequently removed: revalidation rejects it.
        host.Identity.Removed = true;
        using var stale = await host.AddMember(IdentityStub.User.Id);
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
    }

    [Fact]
    public async Task Batch_membership_adds_multiple_users_with_individual_audit_and_is_idempotent()
    {
        await using var host = await Harness.Start();
        var second = IdentityStub.User with { Id = "7c6fd9ae-42e0-44b4-a46c-34e3b6618f6f", Username = "grace" };
        var third = IdentityStub.User with { Id = "1617ee04-cc8d-4f41-a5ae-4df4a235aeb9", Username = "katherine" };
        host.Identity.AdditionalUsers.AddRange([second, third]);

        using var response = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/members/batch",
            new { subjects = new[] { IdentityStub.User.Id, second.Id, third.Id, second.Id } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("addedCount").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("alreadyMemberCount").GetInt32());
        Assert.Equal(3, await host.MemberCount());
        Assert.Equal(3, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync(a => a.Action == "member.added")));

        using var repeated = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/members/batch",
            new { subjects = new[] { IdentityStub.User.Id, second.Id, third.Id } });
        using var repeatedJson = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(0, repeatedJson.RootElement.GetProperty("addedCount").GetInt32());
        Assert.Equal(3, repeatedJson.RootElement.GetProperty("alreadyMemberCount").GetInt32());
        Assert.Equal(3, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync(a => a.Action == "member.added")));
    }

    [Fact]
    public async Task Batch_membership_validates_all_identities_before_writing_anything()
    {
        await using var host = await Harness.Start();
        foreach (var (secondSubject, expected) in new[]
        {
            ("disabled", HttpStatusCode.Conflict),
            ("missing", HttpStatusCode.NotFound),
        })
        {
            using var response = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/members/batch",
                new { subjects = new[] { IdentityStub.User.Id, secondSubject } });
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal(0, await host.MemberCount());
            Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
        }
    }

    [Fact]
    public async Task Batch_membership_rejects_invalid_size_or_subjects_without_identity_calls()
    {
        await using var host = await Harness.Start();
        var path = $"/api/access/teams/{host.TeamId}/members/batch";
        using var empty = await host.Client.PutAsJsonAsync(path, new { subjects = Array.Empty<string>() });
        using var oversized = await host.Client.PutAsJsonAsync(path, new { subjects = Enumerable.Range(0, 51).Select(index => $"user-{index}").ToArray() });
        using var malformed = await host.Client.PutAsJsonAsync(path, new { subjects = new string?[] { IdentityStub.User.Id, " bad " } });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(0, host.Identity.GetCalls);
        Assert.Equal(0, await host.MemberCount());
        Assert.Equal(0, await host.ReadDb(db => db.ResourceAccessAudits.CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outage_is_safe_and_does_not_write_membership(bool rejected)
    {
        await using var host = await Harness.Start();
        host.Identity.Fail = true;
        host.Identity.Rejected = rejected;
        using var lookup = await host.Client.GetAsync("/api/access/users");
        using var membership = await host.AddMember(IdentityStub.User.Id);
        using var batch = await host.Client.PutAsJsonAsync($"/api/access/teams/{host.TeamId}/members/batch", new { subjects = new[] { IdentityStub.User.Id } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, membership.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, batch.StatusCode);
        Assert.DoesNotContain("raw-provider-secret", await lookup.Content.ReadAsStringAsync());
        Assert.DoesNotContain("raw-provider-secret", await membership.Content.ReadAsStringAsync());
        Assert.DoesNotContain("raw-provider-secret", await batch.Content.ReadAsStringAsync());
        Assert.Equal(0, await host.MemberCount());
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=10001")]
    [InlineData("?page=-1")]
    public async Task Invalid_pagination_never_calls_provider(string query)
    {
        await using var host = await Harness.Start();
        using var response = await host.Client.GetAsync("/api/access/users" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Identity.ListCalls);
    }

    private sealed class Harness(WebApplication app, HttpClient client, IdentityStub identity, Guid teamId, Guid projectId, Guid targetId) : IAsyncDisposable
    {
        public Guid TeamId => teamId;
        public Guid ProjectId => projectId;
        public Guid TargetId => targetId;
        public async Task<T> ReadDb<T>(Func<DbScannerDbContext, Task<T>> action)
        {
            using var scope = app.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ResourceAccessContext>().IsAdministrator = true;
            return await action(scope.ServiceProvider.GetRequiredService<DbScannerDbContext>());
        }
        public async Task EditDb(Func<DbScannerDbContext, Task> action)
        {
            using var scope = app.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ResourceAccessContext>().IsAdministrator = true;
            var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
            await action(db); await db.SaveChangesAsync();
        }
        public async Task<bool> CanReadTarget(string? role, TargetPermission permission)
        {
            using var scope = app.Services.CreateScope();
            var access = scope.ServiceProvider.GetRequiredService<ResourceAccessContext>();
            access.Subject = IdentityStub.User.Id;
            access.IsAdministrator = role == "dbscanner-admin";
            access.RolePermissions = TargetAccessPolicy.RolePermissions(role);
            access.RequiredPermission = TargetPermission.Read | permission;
            return await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().Targets.AnyAsync(t => t.Id == targetId);
        }
        public HttpClient Client => client;
        public IdentityStub Identity => identity;
        public Task<HttpResponseMessage> AddMember(string subject) => client.PutAsJsonAsync($"/api/access/teams/{teamId}/members", new { subject });
        public async Task<int> MemberCount()
        {
            using var scope = app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().AccessTeamMembers.CountAsync();
        }
        public static async Task<Harness> Start(string? role = "dbscanner-admin", bool dedicated = false, SaveChangesInterceptor? interceptor = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            var identity = new IdentityStub();
            builder.Services.AddSingleton<IKeycloakAdminService>(identity);
            builder.Services.Configure<TenantIsolationOptions>(options => { options.TenantId = "test"; options.DedicatedTenant = dedicated; });
            builder.Services.AddScoped<ResourceAccessContext>();
            var databaseName = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<DbScannerDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
            builder.Services.AddAuthorizationBuilder()
                .AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("dbscanner-admin"))
                .AddPolicy("Viewer", policy => policy.RequireAuthenticatedUser().RequireRole("dbscanner-admin", "dbscanner-operator", "dbscanner-viewer"));
            var app = builder.Build();
            app.UseAuthentication(); app.UseAuthorization(); app.UseMiddleware<ResourceAccessMiddleware>();
            app.MapResourceAccess();
            await app.StartAsync();
            var teamId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<ResourceAccessContext>().IsAdministrator = true;
                var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
                db.AccessProjects.Add(new AccessProject { Id = AccessProject.DefaultId, Name = "Varsayılan proje" });
                db.AccessTeams.Add(new AccessTeam { Id = teamId, Name = "Test team" });
                db.AccessProjects.Add(new AccessProject { Id = projectId, Name = "Test project" });
                db.Targets.Add(new Target { Id = targetId, ProjectId = projectId, Name = "Test target", Host = "127.0.0.1", Port = 5432, Engine = DatabaseEngine.PostgreSql });
                await db.SaveChangesAsync();
            }
            var client = app.GetTestClient();
            if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
            return new Harness(app, client, identity, teamId, projectId, targetId);
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class ForeignKeyFailureInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new DbUpdateException("raw-provider-secret",
                new PostgresException("raw-provider-secret", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation));
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim("sub", "test-admin"), new Claim("roles", role.ToString()), new Claim("dbscanner_tenant", "test") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, null, "roles")), Scheme.Name)));
        }
    }

    private sealed class IdentityStub : IKeycloakAdminService
    {
        public static readonly IdentityUser User = new("dc55ff30-7475-4fb9-a8f6-a73c6b9793d1", "ada", "Ada", "Lovelace", "private@example.test", "dbscanner-viewer", true, false, null, false, null);
        public IdentityUser CurrentUser { get; set; } = User;
        public bool Fail { get; set; }
        public bool Rejected { get; set; }
        public bool Removed { get; set; }
        public string? Search { get; private set; }
        public int Page { get; private set; }
        public int PageSize { get; private set; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }
        public List<IdentityUser> AdditionalUsers { get; } = [];
        public Task<IdentityUserPage> ListUsersAsync(string? search, string? role, bool? enabled, int page, int pageSize, CancellationToken ct = default)
        {
            ListCalls++; Search = search; Page = page; PageSize = pageSize;
            if (Fail) throw Rejected ? new InvalidOperationException("raw-provider-secret") : new IdentityManagementUnavailableException("raw-provider-secret");
            return Task.FromResult(new IdentityUserPage([User], 11));
        }
        public Task<IdentityUser?> GetUserAsync(string id, CancellationToken ct = default)
        {
            GetCalls++;
            if (Fail) throw Rejected ? new InvalidOperationException("raw-provider-secret") : new IdentityManagementUnavailableException("raw-provider-secret");
            var found = id == "disabled" ? User with { Id = id, Enabled = false }
                : id == User.Id && !Removed ? CurrentUser
                : AdditionalUsers.FirstOrDefault(user => user.Id == id);
            return Task.FromResult<IdentityUser?>(found);
        }
        public Task<IdentityUser> CreateUserAsync(CreateAdminUserCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IdentityUser> UpdateUserAsync(string id, UpdateAdminUserCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IdentityUser> SetUserRoleAsync(string id, string role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IdentityUser> SetUserEnabledAsync(string id, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IdentityUser> SetMfaRequiredAsync(string id, bool required, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountActiveUsersWithRoleAsync(string role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendActionsEmailAsync(string id, IReadOnlyCollection<string> actions, CancellationToken ct = default) => throw new NotSupportedException();
        public Task LogoutUserAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
