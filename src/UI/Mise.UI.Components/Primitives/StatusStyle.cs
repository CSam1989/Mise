using Microsoft.Extensions.Localization;
using Mise.UI.Abstractions;

namespace Mise.UI.Components.Primitives;

internal static class StatusStyle
{
    public static void EnsureSupported(Enum status)
    {
        if (status is not (TableStatus or ReservationStatus))
        {
            throw new ArgumentException($"Unsupported status type {status.GetType().Name}.", nameof(status));
        }
    }

    public static string CssClass(Enum status) => $"mise-status-{status.ToString().ToLowerInvariant()}";

    public static string Label(IStringLocalizer<UiStrings> localizer, Enum status) => localizer[$"Status_{status}"];
}
