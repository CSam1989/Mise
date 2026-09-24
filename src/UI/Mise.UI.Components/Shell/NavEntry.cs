using Microsoft.AspNetCore.Components.Routing;

namespace Mise.UI.Components.Shell;

public enum NavGroup
{
    Main,
    Manager,
}

public sealed record NavEntry(string Href, string LabelKey, string TestId, NavGroup Group, NavLinkMatch Match = NavLinkMatch.Prefix);

/// <summary>Only screens that exist are listed; each Phase 10 step adds its own entry.</summary>
public static class MiseNavigation
{
    public static IReadOnlyList<NavEntry> Entries { get; } =
    [
        new("reservations", "NavReservations", TestIds.NavReservations, NavGroup.Main),
    ];
}
