using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IScanCredentialProtector
{
    string Protect(DatabaseAuthentication authentication);

    DatabaseAuthentication Unprotect(string protectedCredentials);
}
