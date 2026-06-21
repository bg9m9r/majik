namespace Majik.Core.Rules;

/// <summary>
/// CR 100.2 / 100.4 — deck-construction constants. The minimum
/// Constructed deck size is consumed by card behaviour (e.g. Yorion's
/// 80-card requirement) and companion validation.
/// </summary>
public static class DeckValidator
{
    public const int ConstructedMinimum = 60;
}

public sealed record DeckValidationResult(bool IsValid, IReadOnlyList<string> Errors);
