using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Mise.UI.Components;

namespace Mise.Client.UnitTests;

/// <summary>English culture, real RCL localization, a <see cref="FakeTimeProvider"/> and bUnit's fake auth — every RCL component test starts here.</summary>
public abstract class MiseComponentTestContext : BunitContext
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    protected MiseComponentTestContext()
    {
        UseCulture("en");

        Time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 17, 4, 30, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddLocalization();
        Services.AddMiseUiComponents();

        Auth = AddAuthorization();
    }

    protected FakeTimeProvider Time { get; }

    protected BunitAuthorizationContext Auth { get; }

    protected static void UseCulture(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    protected override void Dispose(bool disposing)
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        base.Dispose(disposing);
    }
}
