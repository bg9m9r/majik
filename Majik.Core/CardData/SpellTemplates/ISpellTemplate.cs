// Majik.Core/CardData/SpellTemplates/ISpellTemplate.cs
using Majik.Core.CardData.Database;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// A single pattern → SpellDefinition mapping. Implementations own their
/// regex (or other matching logic) and the spell construction.
/// Higher Priority wins when multiple templates would match.
/// </summary>
public interface ISpellTemplate
{
    /// <summary>Higher = checked first. Use 100+ for very specific
    /// templates that must beat a more general one (e.g. CounterUnlessPay
    /// must beat CounterTargetSpell). Default catch-alls use 10.</summary>
    int Priority { get; }

    /// <summary>Stable identifier used in coverage reports and logs.</summary>
    string Name { get; }

    /// <summary>Return null if this template doesn't match.</summary>
    SpellDefinition? TryBind(SpellBindContext ctx);
}

public sealed record SpellBindContext(
    CardEntity Entity,
    Player Caster,
    Func<object, object> Resolver,
    Majik.Core.Effects.ContinuousEffectsService? Effects,
    Majik.Core.Stack.Stack? Stack)
{
    public string Text => Entity.OracleText ?? string.Empty;
}

/// <summary>Shared parsing helpers used across templates.</summary>
public static class SpellTemplateHelpers
{
    /// <summary>Translate "a"/"an"/"one".."ten" or a digit string to int.
    /// Returns 0 when neither.</summary>
    public static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var n) ? n : 0,
        };
}
