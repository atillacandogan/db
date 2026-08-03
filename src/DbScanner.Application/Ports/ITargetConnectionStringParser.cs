using DbScanner.Application.Models;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface ITargetConnectionStringParser
{
    ParsedTargetConnection Parse(string connectionString, DatabaseEngine engine);
}
