namespace Mise.ApiService.Staff;

internal sealed record RegisterStaffRequest(
    Guid OperationId, string Username, string Password, string FullName, string Role);
