namespace Sample.Application.Configuration;

/// <summary>Feature flags, bound from the <c>Features</c> configuration section.</summary>
public sealed class FeatureFlagsOptions
{
    public const string SectionName = "Features";

    /// <summary>Sick leave is approved on submission instead of waiting for the manager.</summary>
    public bool AutoApproveSickLeave { get; set; }

    /// <summary>Public holidays come from the external calendar API instead of the built-in French calendar.</summary>
    public bool ExternalHolidayCalendar { get; set; }
}
