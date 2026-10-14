namespace DbScanner.Contracts.AdminUsers;

public sealed record AdminUserPageDto(
    IReadOnlyList<AdminUserDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
