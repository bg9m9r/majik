using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Misc;

/// <summary>
/// Single-effect spells whose runtime resolution requires engine
/// subsystems that don't exist yet (damage-prevention service,
/// "can't-be-blocked" state, etc). v1 stubs bind the spell with an
/// empty effect list so the card *is castable and resolvable*; the
/// effect is lossy but the card no longer falls through to the
/// vanilla shell.
///
/// Bundled in one file because each template is ~10 lines and
/// they share the empty-effect resolution pattern.
/// </summary>
internal static class StubBindHelpers
{
    internal static SpellDefinition EmptyEffectSpell(IReadOnlyList<TargetRequest> targets) => new(
        Modes: Array.Empty<string>(),
        HasVariableX: false,
        TargetRequests: targets,
        EffectFactory: _ => Array.Empty<IEffect>());
}

/// <summary>
/// Fog template — "Prevent all combat damage that would be dealt
/// this turn." Single-clause, no targets. v1 lossy (damage isn't
/// actually prevented). Catches Fog, Holy Day, Druid's Deliverance,
/// Tangle, Spore Cloud's first clause, and similar.
/// </summary>
public sealed class FogTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*prevent\s+all\s+combat\s+damage\s+that\s+would\s+be\s+dealt\s+this\s+turn\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "Fog";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StubBindHelpers.EmptyEffectSpell(Array.Empty<TargetRequest>());
}

/// <summary>
/// "Target creature can't be blocked this turn" — evasion-grant spells
/// (Trailblazer, Slip Through Space, etc). v1 lossy (cannot install
/// the cant-be-blocked state without a per-turn-modifier service).
/// </summary>
public sealed class TargetCantBeBlockedTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+creature\s+can'?t\s+be\s+blocked\s+this\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "TargetCantBeBlocked";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StubBindHelpers.EmptyEffectSpell(new[]
        {
            new TargetRequest("target creature", 1, 1, Array.Empty<object>())
        });
}

/// <summary>
/// "Up to N target creatures can't block this turn" — Panic Attack,
/// Unearthly Blizzard, Crusher Zendikon-style. Same lossy posture as
/// <see cref="TargetCantBeBlockedTemplate"/>.
/// </summary>
public sealed class UpToNCantBlockTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"up\s+to\s+(?<n>\d+|one|two|three|four|five)\s+target\s+creatures?\s+can'?t\s+block\s+this\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "UpToNCantBlock";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StubBindHelpers.EmptyEffectSpell(new[]
        {
            new TargetRequest("target creature", 0, 3, Array.Empty<object>())
        });
}
