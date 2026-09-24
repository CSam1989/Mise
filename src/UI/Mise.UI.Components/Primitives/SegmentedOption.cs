namespace Mise.UI.Components.Primitives;

/// <summary><paramref name="Key"/> is the stable, culture-independent part of the option's test id.</summary>
public sealed record SegmentedOption<TValue>(string Key, TValue Value, string Label);
