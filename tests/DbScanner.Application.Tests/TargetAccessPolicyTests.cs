using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class TargetAccessPolicyTests
{
    [Theory]
    [InlineData("dbscanner-admin", true, 0, 15)]
    [InlineData("dbscanner-admin", false, 15, 0)]
    [InlineData("dbscanner-operator", true, 15, 15)]
    [InlineData("dbscanner-operator", true, 0, 0)]
    [InlineData("dbscanner-viewer", true, 15, 9)]
    [InlineData("dbscanner-viewer", true, 3, 1)]
    [InlineData("dbscanner-viewer", false, 15, 0)]
    [InlineData(null, true, 15, 0)]
    [InlineData("untrusted-role", true, 15, 0)]
    [InlineData("dbscanner-operator", true, 2, 0)]
    public void Effective_permissions_obey_role_ceiling_read_prerequisite_and_account_state(string? role, bool enabled, int grant, int expected) =>
        Assert.Equal((TargetPermission)expected, TargetAccessPolicy.EffectivePermissions(role, enabled, [(TargetPermission)grant]));

    [Fact]
    public void Permissions_from_multiple_active_teams_are_additive() =>
        Assert.Equal(TargetPermission.Read | TargetPermission.Scan | TargetPermission.Export,
            TargetAccessPolicy.EffectivePermissions("dbscanner-operator", true,
                [TargetPermission.Read | TargetPermission.Scan, TargetPermission.Read | TargetPermission.Export]));
}
