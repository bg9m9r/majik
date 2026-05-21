using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
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

/// <summary>
/// "Untap all creatures you control" — Mobilize, Refresh, Twiddle's
/// mass variant. Real effect: iterates caster's permanents and untaps
/// every tapped creature.
/// </summary>
public sealed class UntapAllYourCreaturesTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*untap\s+all\s+creatures\s+you\s+control\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "UntapAllYourCreatures";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var caster = ctx.Caster;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("untap your creatures", () =>
            {
                foreach (var p in caster.Zones.Battlefield.GetCards().OfType<Creature>())
                {
                    if (p.IsTapped) p.Untap();
                }
            }) });
    }
}

/// <summary>
/// "Target player sacrifices a creature" / "Each opponent sacrifices a
/// creature" — Diabolic Edict, Cruel Edict, Innocent Blood family.
/// v1 stub: deterministic pick (first non-token creature the target
/// controls) and move to graveyard. Targets one player.
/// </summary>
public sealed class TargetPlayerSacrificesCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+player\s+sacrifices\s+a\s+creature(?:\s+of\s+their\s+choice)?",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "TargetPlayerSacrificesCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("edict", () =>
                {
                    if (target is not Player tp) return;
                    var pick = tp.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .FirstOrDefault();
                    if (pick != null) OracleSpellBinder.MoveToGraveyard(pick);
                }) };
            });
    }
}

/// <summary>
/// "Put a +1/+1 counter on each creature you control" — Inspiring
/// Roar, Tribute to the Wild's friendly-side variants, etc.
/// </summary>
public sealed class PlusOneCounterEachYouControlTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*put\s+a\s+\+1/\+1\s+counter\s+on\s+each\s+creature\s+you\s+control\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "PlusOneCounterEachYouControl";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var caster = ctx.Caster;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("+1/+1 each you control", () =>
            {
                foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
                {
                    c.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);
                }
            }) });
    }
}

/// <summary>
/// "Regenerate target permanent" / "Regenerate target creature" — at
/// v1 regeneration shields aren't modeled, so the stub binds but
/// applies no effect at resolution. Catches Reknit, Mossbridge
/// Troll's activation pattern (instant-side spells), etc.
/// </summary>
public sealed class RegenerateTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*regenerate\s+target\s+(?:creature|permanent)\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "RegenerateTarget";

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
