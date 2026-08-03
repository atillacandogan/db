namespace DbScanner.Application.Models;

public sealed record ParsedTargetConnection(
    string Host,
    int Port,
    string? Database,
    string? Username);
