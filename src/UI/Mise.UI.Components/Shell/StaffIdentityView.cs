using System.Security.Claims;
using Mise.UI.Abstractions;

namespace Mise.UI.Components.Shell;

internal sealed record StaffIdentityView(string FullName, string ShortName, string Initials, string RoleLabelKey, int AvatarIndex)
{
    private const int AvatarPaletteSize = 6;

    public static StaffIdentityView From(ClaimsPrincipal user)
    {
        var fullName = user.Identity?.Name?.Trim() ?? string.Empty;
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var shortName = parts.Length > 1 ? $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}." : fullName;
        var initials = parts.Length switch
        {
            0 => string.Empty,
            1 => char.ToUpperInvariant(parts[0][0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[^1][0])),
        };
        var roleLabelKey = user.IsInRole(StaffRoles.Manager) ? "RoleManager" : "RoleFloorStaff";

        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? fullName;
        var avatarIndex = id.Aggregate(0, (hash, c) => (hash * 31 + c) & int.MaxValue) % AvatarPaletteSize;

        return new StaffIdentityView(fullName, shortName, initials, roleLabelKey, avatarIndex);
    }
}
