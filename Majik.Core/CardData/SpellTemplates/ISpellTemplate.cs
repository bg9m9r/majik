// Majik.Core/CardData/SpellTemplates/ISpellTemplate.cs
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
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

    /// <summary>
    /// Strategic intent for the bot. Templates declare what kind of
    /// effect they produce; <c>HeuristicBotAgent</c> reads this to pick
    /// modes / targets / mana-hold decisions without parsing oracle text.
    /// Default <see cref="BotIntent.None"/> for templates not yet annotated
    /// — the bot falls back to legacy label sniffing for those cases.
    /// See <c>docs/superpowers/specs/2026-05-22-bot-intent-classifier-design.md</c>.
    /// </summary>
    BotIntent Intent => BotIntent.None;

    /// <summary>Return null if this template doesn't match.</summary>
    SpellDefinition? TryBind(SpellBindContext ctx);

    /// <summary>
    /// Whether the template's <see cref="Rehydrate"/> can produce a valid
    /// <see cref="SpellDefinition"/> with the supplied
    /// <see cref="SpellBindContext"/>. Used by the live <c>TryBind</c>
    /// path AND the compiled fast path to short-circuit before invoking
    /// <see cref="Rehydrate"/> on a context that's missing a dependency.
    ///
    /// Default: <c>true</c>. Templates that require optional services
    /// from <see cref="SpellBindContext"/> (e.g.
    /// <see cref="SpellBindContext.Effects"/> for static-effect templates)
    /// override and check those dependencies here. Returning <c>false</c>
    /// behaves the same as the template not matching at all — the registry
    /// moves on to the next candidate.
    /// </summary>
    bool CanBind(SpellBindContext ctx) => true;

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
    /// Context-aware overload of <see cref="TryExtractParams(string)"/>.
    /// Default impl delegates to the string overload using
    /// <see cref="SpellBindContext.Text"/> (post-<see cref="OracleTextNormalizer"/>),
    /// preserving the existing contract for every template that only
    /// looks at the normalized effect text.
    ///
    /// Templates whose detection needs the un-normalized
    /// <see cref="SpellBindContext.RawText"/> — e.g. leading-keyword
    /// detectors like Strive / Convoke or additional-cost prefixes —
    /// override this overload so the offline compile pipeline can see
    /// the same signal as the live binder.
    /// </summary>
    IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return TryExtractParams(ctx.Text);
    }

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
    Majik.Core.Stack.Stack? Stack,
    Majik.Core.Effects.ReplacementBus? Replacements = null,
    Majik.Core.Abilities.TriggerManager? Triggers = null,
    Majik.Core.Events.IEventBus? EventBus = null,
    Majik.Core.Services.ZoneService? Zones = null)
{
    public string Text => OracleTextNormalizer.Normalize(Entity.OracleText ?? string.Empty);

    /// <summary>
    /// Raw oracle text BEFORE <see cref="OracleTextNormalizer"/> strips
    /// any leading passive-keyword / additional-cost prefixes. Bespoke
    /// templates that need to detect those stripped prefixes (e.g.
    /// "As an additional cost to cast this spell, sacrifice a creature.")
    /// match against this rather than <see cref="Text"/>.
    /// </summary>
    public string RawText => Entity.OracleText ?? string.Empty;
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

        if (!template.CanBind(ctx)) return null;
        var @params = template.TryExtractParams(ctx.Text);
        return @params is null ? null : template.Rehydrate(@params, ctx);
    }
}
