namespace Sample.Domain.Common;

/// <summary>
/// Marks a property holding personal data (GDPR). Anything carrying this attribute must never leave
/// production in clear text: the preview data seeder pseudonymises it and a test enforces that rule.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class PersonalDataAttribute : Attribute;
