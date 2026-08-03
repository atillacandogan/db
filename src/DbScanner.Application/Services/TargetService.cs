using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Targets;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class TargetService(
    ITargetRepository targetRepository,
    IScanRepository scanRepository,
    IScanScopeRepository scanScopeRepository,
    ITargetConnectionStringParser connectionStringParser,
    IScanCredentialProtector credentialProtector) : ITargetService
{
    public async Task<IReadOnlyList<TargetDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var targets = await targetRepository.ListAsync(cancellationToken);
        return targets.Select(MapTarget).ToArray();
    }

    public async Task<TargetDto> CreateAsync(CreateTargetCommand command, CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();
        var environmentTag = command.EnvironmentTag?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Target name is required.");
        }

        if (string.IsNullOrWhiteSpace(environmentTag))
        {
            throw new InvalidOperationException("Target environment is required.");
        }

        var (host, port) = ResolveEndpoint(command);

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Target host is required.");
        }

        if (port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("Target port must be between 1 and 65535.");
        }

        var isAllowed = await scanScopeRepository.IsAllowedAsync(host, port, cancellationToken);
        if (!isAllowed)
        {
            throw new InvalidOperationException("Target is outside the approved scan scope.");
        }

        var existingTargets = await targetRepository.ListAsync(cancellationToken);
        var duplicateExists = existingTargets.Any(target =>
            target.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            && target.Port == port
            && target.Engine == command.Engine);

        if (duplicateExists)
        {
            throw new InvalidOperationException("A target with the same host, port and engine already exists.");
        }

        var target = new Target
        {
            Name = name,
            Host = host,
            Port = port,
            Engine = command.Engine,
            ConnectionMode = command.ConnectionMode,
            EnvironmentTag = environmentTag,
            ProtectedCredentials = ProtectCredentials(command.Credentials),
            Enabled = command.Enabled
        };

        await targetRepository.AddAsync(target, cancellationToken);
        await targetRepository.SaveChangesAsync(cancellationToken);

        return MapTarget(target);
    }

    public async Task<TargetDto> UpdateAsync(
        Guid targetId,
        UpdateTargetCommand command,
        CancellationToken cancellationToken = default)
    {
        var target = await targetRepository.GetByIdAsync(targetId, cancellationToken)
            ?? throw new InvalidOperationException("Target was not found.");

        if (await scanRepository.HasActiveScanAsync(targetId, cancellationToken))
        {
            throw new InvalidOperationException("Target cannot be updated while an active scan exists.");
        }

        var name = command.Name.Trim();
        var host = command.Host.Trim();
        var environmentTag = command.EnvironmentTag?.Trim();
        ValidateTargetValues(name, host, command.Port, environmentTag);

        if (!await scanScopeRepository.IsAllowedAsync(host, command.Port, cancellationToken))
        {
            throw new InvalidOperationException("Target is outside the approved scan scope.");
        }

        var duplicateExists = (await targetRepository.ListAsync(cancellationToken)).Any(candidate =>
            candidate.Id != targetId
            && candidate.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            && candidate.Port == command.Port
            && candidate.Engine == command.Engine);
        if (duplicateExists)
        {
            throw new InvalidOperationException("A target with the same host, port and engine already exists.");
        }

        var engineChanged = target.Engine != command.Engine;
        target.Name = name;
        target.Host = host;
        target.Port = command.Port;
        target.Engine = command.Engine;
        target.EnvironmentTag = environmentTag;
        target.Enabled = command.Enabled;

        if (command.ClearCredentials || (engineChanged && command.Credentials is null))
        {
            target.ProtectedCredentials = null;
        }
        else if (command.Credentials is not null)
        {
            target.ProtectedCredentials = ProtectCredentials(command.Credentials);
        }

        await targetRepository.SaveChangesAsync(cancellationToken);
        return MapTarget(target);
    }

    private (string Host, int Port) ResolveEndpoint(CreateTargetCommand command)
    {
        if (command.ConnectionMode == TargetConnectionMode.ManualConnectionString)
        {
            if (string.IsNullOrWhiteSpace(command.ConnectionString))
            {
                throw new InvalidOperationException("Connection string is required.");
            }

            var parsedConnection = connectionStringParser.Parse(command.ConnectionString, command.Engine);
            return (parsedConnection.Host.Trim(), parsedConnection.Port);
        }

        return (command.Host?.Trim() ?? string.Empty, command.Port ?? 0);
    }

    private string? ProtectCredentials(DatabaseAuthentication? credentials)
    {
        if (credentials is null)
        {
            return null;
        }

        ValidateCredentials(credentials);
        return credentialProtector.Protect(credentials);
    }

    private static void ValidateCredentials(DatabaseAuthentication credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.Database) || credentials.Database.Length > 128)
        {
            throw new InvalidOperationException("Database name is required and must not exceed 128 characters.");
        }

        if (string.IsNullOrWhiteSpace(credentials.Username) || credentials.Username.Length > 128)
        {
            throw new InvalidOperationException("Database username is required and must not exceed 128 characters.");
        }

        if (string.IsNullOrEmpty(credentials.Password) || credentials.Password.Length > 128)
        {
            throw new InvalidOperationException("Database password is required and must not exceed 128 characters.");
        }

        if (credentials.ConnectionTimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("Database connection timeout must be between 1 and 30 seconds.");
        }
    }

    private static void ValidateTargetValues(
        string name,
        string host,
        int port,
        string? environmentTag)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Target name is required.");
        }

        if (string.IsNullOrWhiteSpace(environmentTag))
        {
            throw new InvalidOperationException("Target environment is required.");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Target host is required.");
        }

        if (port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("Target port must be between 1 and 65535.");
        }
    }

    public async Task<bool> DeleteAsync(Guid targetId, CancellationToken cancellationToken = default)
    {
        var target = await targetRepository.GetByIdAsync(targetId, cancellationToken);
        if (target is null)
        {
            return false;
        }

        var hasActiveScan = await scanRepository.HasActiveScanAsync(targetId, cancellationToken);
        if (hasActiveScan)
        {
            throw new InvalidOperationException("Target cannot be deleted while an active scan exists.");
        }

        await targetRepository.DeleteAsync(target, cancellationToken);
        await targetRepository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static TargetDto MapTarget(Target target)
    {
        var lastScan = target.Scans
            .OrderByDescending(scan => scan.CreatedAt)
            .FirstOrDefault();

        return new TargetDto(
            target.Id,
            target.Name,
            target.Host,
            target.Port,
            ToEngine(target.Engine),
            ToConnectionMode(target.ConnectionMode),
            target.EnvironmentTag,
            !string.IsNullOrWhiteSpace(target.ProtectedCredentials),
            target.Enabled,
            lastScan is null
                ? null
                : new TargetScanSummaryDto(
                    lastScan.Id,
                    ToStatus(lastScan.Status),
                    lastScan.RiskScore,
                    lastScan.OverallSeverity?.ToString().ToLowerInvariant(),
                    lastScan.CreatedAt,
                    lastScan.FinishedAt));
    }

    private static string ToEngine(DatabaseEngine engine) => engine.ToApiString();

    private static string ToConnectionMode(TargetConnectionMode connectionMode) =>
        connectionMode switch
        {
            TargetConnectionMode.ManualConnectionString => "manualConnectionString",
            _ => "defaultValues"
        };

    private static string ToStatus(ScanStatus status) =>
        status switch
        {
            ScanStatus.Pending => "pending",
            ScanStatus.Scanning => "scanning",
            ScanStatus.Scanned => "scanned",
            ScanStatus.Failed => "failed",
            ScanStatus.Timeout => "timeout",
            ScanStatus.Cancelled => "cancelled",
            _ => "pending"
        };
}
