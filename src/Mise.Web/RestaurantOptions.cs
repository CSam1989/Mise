using System.ComponentModel.DataAnnotations;

namespace Mise.Web;

public sealed class RestaurantOptions
{
    public const string SectionName = "Restaurant";

    private TimeZoneInfo? _timeZone;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string TimeZoneId { get; set; } = string.Empty;

    public TimeZoneInfo TimeZone => _timeZone ??= TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
