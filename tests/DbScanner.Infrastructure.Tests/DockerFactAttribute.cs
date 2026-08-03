using System.Diagnostics;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!CanRun("docker", "version --format {{.Server.Version}}"))
        {
            Skip = "Docker daemon is unavailable; the database container integration test was not run.";
        }
    }

    internal static bool CanRun(string executable, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            if (process is null || !process.WaitForExit(5_000))
            {
                try
                {
                    process?.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DockerAndNmapFactAttribute : FactAttribute
{
    public DockerAndNmapFactAttribute()
    {
        if (!DockerFactAttribute.CanRun("docker", "version --format {{.Server.Version}}"))
        {
            Skip = "Docker daemon is unavailable; the Microsoft SQL Server Nmap test was not run.";
        }
        else if (!DockerFactAttribute.CanRun("nmap", "--version"))
        {
            Skip = "Nmap is unavailable; the real Microsoft SQL Server Nmap test was not run.";
        }
    }
}
