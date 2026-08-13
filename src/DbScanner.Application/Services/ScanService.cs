using DbScanner.Application.Ports;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class ScanService(
    ITargetRepository targetRepository,
    IScanRepository scanRepository,
    IScanScopeRepository scanScopeRepository,
    IScanJobPublisher scanJobPublisher,
    IScanCredentialProtector credentialProtector) : IScanService
{
    public Task<Scan> CreateScanAsync(Guid targetId, CancellationToken cancellationToken = default) =>
        CreateScanAsync(targetId, selectedNseScriptIds: null, cancellationToken);

    public async Task<Scan> CreateScanAsync(
        Guid targetId,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        CancellationToken cancellationToken)
    {
        var target = await targetRepository.GetByIdAsync(targetId, cancellationToken)
            ?? throw new InvalidOperationException("Target was not found.");

        if (!target.Enabled)
        {
            throw new InvalidOperationException("Target is disabled.");
        }

        var isInScope = await scanScopeRepository.IsAllowedAsync(
            target.Host,
            target.Port,
            target.EnvironmentTag ?? string.Empty,
            target.Engine,
            cancellationToken);
        if (!isInScope)
        {
            throw new InvalidOperationException("Target is outside the approved scan scope.");
        }

        var hasActiveScan = await scanRepository.HasActiveScanAsync(target.Id, cancellationToken);
        if (hasActiveScan)
        {
            throw new InvalidOperationException("Target already has an active scan.");
        }

        var normalizedScriptIds = NormalizeSelectedScriptIds(selectedNseScriptIds);
        var authentication = LoadTargetAuthentication(target);
        ValidateAuthentication(target, authentication);

        var scan = new Scan
        {
            TargetId = target.Id
        };

        await scanRepository.AddAsync(scan, cancellationToken);
        await scanRepository.SaveChangesAsync(cancellationToken);

        await scanJobPublisher.PublishScanRequestedAsync(
            scan,
            target,
            normalizedScriptIds,
            authentication,
            cancellationToken);

        return scan;
    }

    private DatabaseAuthentication? LoadTargetAuthentication(Target target)
    {
        if (target.Engine is not (DatabaseEngine.MsSql or DatabaseEngine.Oracle)
            || string.IsNullOrWhiteSpace(target.ProtectedCredentials))
        {
            return null;
        }

        try
        {
            return credentialProtector.Unprotect(target.ProtectedCredentials);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("Stored target credentials could not be loaded.");
        }
    }

    public Task<Scan?> GetScanAsync(Guid scanId, CancellationToken cancellationToken = default) =>
        scanRepository.GetByIdAsync(scanId, cancellationToken);

    public Task<IReadOnlyList<Scan>> ListScansAsync(CancellationToken cancellationToken = default) =>
        scanRepository.ListAsync(cancellationToken);

    public Task<IReadOnlyList<Scan>> ListScansByTargetAsync(Guid targetId, CancellationToken cancellationToken = default) =>
        scanRepository.ListByTargetAsync(targetId, cancellationToken);

    public async Task<bool> CancelScanAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        var scan = await scanRepository.GetByIdAsync(scanId, cancellationToken);
        if (scan is null)
        {
            return false;
        }

        if (scan.Status is not (Domain.Enums.ScanStatus.Pending or Domain.Enums.ScanStatus.Scanning))
        {
            return false;
        }

        await scanRepository.UpdateStatusAsync(scanId, Domain.Enums.ScanStatus.Cancelled, cancellationToken: cancellationToken);
        await scanRepository.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteScanAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        var scan = await scanRepository.GetByIdAsync(scanId, cancellationToken);
        if (scan is null)
        {
            return false;
        }

        if (scan.Status is Domain.Enums.ScanStatus.Pending or Domain.Enums.ScanStatus.Scanning)
        {
            throw new InvalidOperationException("Active scans must be stopped before deletion.");
        }

        await scanRepository.DeleteAsync(scan, cancellationToken);
        await scanRepository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static IReadOnlyCollection<string>? NormalizeSelectedScriptIds(IReadOnlyCollection<string>? selectedNseScriptIds)
    {
        if (selectedNseScriptIds is null)
        {
            return null;
        }

        var normalizedScriptIds = selectedNseScriptIds
            .Where(scriptId => !string.IsNullOrWhiteSpace(scriptId))
            .Select(scriptId => scriptId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedScriptIds.Length == 0)
        {
            throw new InvalidOperationException("At least one NSE script must be selected.");
        }

        return normalizedScriptIds;
    }

    private static void ValidateAuthentication(Target target, DatabaseAuthentication? authentication)
    {
        if (authentication is null)
        {
            return;
        }

        if (target.Engine is not (DatabaseEngine.MsSql or DatabaseEngine.Oracle))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(authentication.Database) || authentication.Database.Length > 128)
        {
            throw new InvalidOperationException("Database or service name is required and must not exceed 128 characters.");
        }

        if (string.IsNullOrWhiteSpace(authentication.Username) || authentication.Username.Length > 128)
        {
            throw new InvalidOperationException("Database username is required and must not exceed 128 characters.");
        }

        if (string.IsNullOrEmpty(authentication.Password) || authentication.Password.Length > 128)
        {
            throw new InvalidOperationException("Database password is required and must not exceed 128 characters.");
        }

        if (authentication.ConnectionTimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("Database connection timeout must be between 1 and 30 seconds.");
        }
    }
}
