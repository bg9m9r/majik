// Majik.Core/CardData/SpellTemplates/ISpellTemplate.cs
using Majik.Core.CardData.Database;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// A single pattern → SpellDefinition mapping. Implementations own their
/// regex (or other matching logic) and the spell construction.
/// Higher Priority wins when multiple templates would match.
///
/// Templates may opt into Phase-2 pre-compilation by overriding
/// <see cref="TryExtractParams"/> + <see cref="Rehydrate"/>. The
/// <see cref="TryBind"/> contract is unchanged — its default behavior
/// when the new methods are overridden is exposed via
/// <see cref="SpellTemplateBindHelper.DefaultTryBind"/> so opted-in
/// templates can keep one source of truth for the bind pipeline.
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

    // --------------------------------------------------------------------
    // Phase 2 seam: split parsing (oracle text → params) from construction
    // (params + ctx → SpellDefinition). Pre-compile pipeline runs
    // TryExtractParams offline and persists the params dictionary; runtime
    // skips regex and calls Rehydrate(persisted params, ctx).
    //
    // Default impls let unmigrated templates keep their current TryBind
    // behavior with no compile-time fanout. Templates that opt into the
    // pipeline override both methods.
    // --------------------------------------------------------------------

    /// <summary>
    /// Pure parse — given oracle text, return extracted parameters as a
    /// stable string dictionary (JSON-serializable) or <c>null</c> when
    /// the template does not match. Implementations must NOT consult
    /// engine state — the result is cached in the compiled DB.
    ///
    /// Default: returns <c>null</c>. Override to opt into pre-compilation.
    /// </summary>
    IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) => null;

    /// <summary>
    /// Given parameters previously produced by
    /// <see cref="TryExtractParams"/> (possibly round-tripped through
    /// JSON storage) and a live <see cref="SpellBindContext"/>, build the
    /// final <see cref="SpellDefinition"/>.
    ///
    /// Default: throws — only callable on templates that opted in. The
    /// throw is intentional rather than returning null so a mis-wired
    /// pipeline is loud, not silent.
    /// </summary>
    SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        throw new NotSupportedException(
            $"Template '{Name}' does not implement Rehydrate. " +
            "Override TryExtractParams + Rehydrate to opt into pre-compilation, " +
            "or use TryBind directly.");
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

/// <summary>
/// Bridges the Phase-1 <c>TryBind</c> contract with the Phase-2
/// <c>TryExtractParams + Rehydrate</c> split. Templates that override both
/// new methods can route their <c>TryBind</c> through
/// <see cref="DefaultTryBind"/> instead of duplicating the
/// "extract then build" flow.
/// </summary>
public static class SpellTemplateBindHelper
{
    public static SpellDefinition? DefaultTryBind(ISpellTemplate template, SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(ctx);

        var @params = template.TryExtractParams(ctx.Text);
        return @params is null ? null : template.Rehydrate(@params, ctx);
    }
}
