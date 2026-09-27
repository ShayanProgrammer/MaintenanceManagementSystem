using System.ComponentModel.DataAnnotations;

namespace MaintenanceManagementSystem.Api.Validation;

/// <summary>
/// Validation attribute for required text fields: null is ignored (that is
/// [Required]'s job) and any string that is empty or whitespace-only after
/// trimming is invalid. Services trim these fields before persisting them,
/// so this rule is what prevents whitespace-only input ("   ") from being
/// stored as an empty value.
///
/// Not expressed with RegularExpressionAttribute because it matches the
/// whole string against the pattern (implicit anchoring), which would need
/// a look-ahead pattern to test "contains at least one non-whitespace
/// character" — far less readable than this one rule.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotWhitespaceAttribute : ValidationAttribute
{
    public NotWhitespaceAttribute()
        : base("The {0} field must contain non-whitespace characters.")
    {
    }

    public override bool IsValid(object? value) =>
        value is not string text || text.Trim().Length > 0;
}
