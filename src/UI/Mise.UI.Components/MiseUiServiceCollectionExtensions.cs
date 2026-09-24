using Microsoft.Extensions.DependencyInjection;
using Mise.UI.Components.Feedback;

namespace Mise.UI.Components;

public static class MiseUiServiceCollectionExtensions
{
    /// <summary>Registers the RCL's own UI services. The host must also register <see cref="TimeProvider"/> and localization.</summary>
    public static IServiceCollection AddMiseUiComponents(this IServiceCollection services)
    {
        services.AddScoped<ToastService>();
        return services;
    }
}
