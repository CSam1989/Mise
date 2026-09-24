using Microsoft.Extensions.Localization;

[assembly: ResourceLocation("Resources")]
[assembly: RootNamespace("Mise.UI.Components")]

namespace Mise.UI.Components;

/// <summary>Marker type for <c>IStringLocalizer&lt;UiStrings&gt;</c>, backed by Resources/UiStrings(.nl-BE).resx. Keys follow the mockup's <c>T.en</c> names.</summary>
public sealed class UiStrings
{
    private UiStrings()
    {
    }
}
