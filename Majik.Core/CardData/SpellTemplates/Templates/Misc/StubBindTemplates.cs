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

/// <summary>
/// "Put target [creature|permanent|nonland permanent|attacking creature]
/// on top of its owner's library." — Time Ebb, Totally Lost, Boomerang
/// variants, Condemn. Removes the target from the battlefield and
/// inserts at index 0 of the owner's library (top).
/// </summary>
public sealed class PutTargetOnTopOfLibraryTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"put\s+target\s+(?:[\w-]+\s+)*?(?:creature|permanent|card|nonland\s+permanent)\s+on\s+top\s+of\s+its\s+owner'?s?\s+library",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "PutTargetOnTopOfLibrary";

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
            TargetRequests: new[] { new TargetRequest("target permanent", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("to top of library", () =>
                {
                    if (target is not ICard card) return;
                    var owner = card.Owner;
                    if (owner == null) return;
                    if (card.Zone == Majik.Core.Zones.ZoneType.Battlefield) owner.Zones.Battlefield.RemoveCard(card);
                    else if (card.Zone == Majik.Core.Zones.ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(card);
                    else if (card.Zone == Majik.Core.Zones.ZoneType.Hand) owner.Zones.Hand.RemoveCard(card);
                    owner.Zones.Library.InsertCardAt(0, card);
                    card.SetZone(Majik.Core.Zones.ZoneType.Library);
                }) };
            });
    }
}

/// <summary>
/// "Put target [X] on the bottom of its owner's library." — Mystic
/// Repeal, Hindering Light. Mirrors PutTargetOnTopOfLibrary but
/// appends to the library tail.
/// </summary>
public sealed class PutTargetOnBottomOfLibraryTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"put\s+target\s+(?:[\w-]+\s+)*?(?:creature|permanent|card|nonland\s+permanent)\s+on\s+the\s+bottom\s+of\s+its\s+owner'?s?\s+library",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "PutTargetOnBottomOfLibrary";

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
            TargetRequests: new[] { new TargetRequest("target permanent", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("to bottom of library", () =>
                {
                    if (target is not ICard card) return;
                    var owner = card.Owner;
                    if (owner == null) return;
                    if (card.Zone == Majik.Core.Zones.ZoneType.Battlefield) owner.Zones.Battlefield.RemoveCard(card);
                    else if (card.Zone == Majik.Core.Zones.ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(card);
                    else if (card.Zone == Majik.Core.Zones.ZoneType.Hand) owner.Zones.Hand.RemoveCard(card);
                    owner.Zones.Library.AddCard(card);
                    card.SetZone(Majik.Core.Zones.ZoneType.Library);
                }) };
            });
    }
}

/// <summary>
/// "Target player draws X cards" — Stroke of Genius, Blue Sun's
/// Zenith (lossy on shuffle-self), Mind Spring. Variable-X draw with
/// player target. v1 uses the spell's X for the count.
/// </summary>
public sealed class TargetPlayerDrawsXTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+player\s+draws\s+x\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "TargetPlayerDrawsX";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                var x = p.X ?? 0;
                return new IEffect[] { new Effect($"draw X={x}", () =>
                {
                    if (target is not Player pl) return;
                    for (var i = 0; i < x; i++)
                    {
                        var top = pl.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null) return;
                        pl.Zones.Library.RemoveCard(top);
                        pl.Zones.Hand.AddCard(top);
                        top.SetZone(Majik.Core.Zones.ZoneType.Hand);
                    }
                }) };
            });
    }
}

/// <summary>
/// Mixed-sign pump — "Target creature gets +N/-M" or "-N/+M until end
/// of turn". Catches Lash of Malice (+2/-2), Belbe's Armor variants.
/// Lower priority than PumpCreature (+/+) and DebuffCreature (-/-) so
/// those win on their respective shapes.
/// </summary>
public sealed class MixedSignPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+creature\s+gets\s+(?<p>[+-]\d+)/(?<t>[+-]\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 40;
    public string Name => "MixedSignPump";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var p = m.Groups["p"].Value;
        var t = m.Groups["t"].Value;
        if ((p[0] == '+') == (t[0] == '+')) return null; // skip pure +/+ and -/-
        return new Dictionary<string, string> { ["p"] = p, ["t"] = t };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        Majik.Core.CardData.SpellTemplates.Templates.Counters.CountersSpellFactory.PumpSpell(
            int.Parse(@params["p"]),
            int.Parse(@params["t"]),
            ctx.Resolver);
}

/// <summary>
/// Variable-X pump — "Target creature gets +X/+0", "+X/+X", "+N/+X",
/// "-X/-X" until end of turn. Captures variants the fixed-numeric
/// PumpCreature/DebuffCreature templates miss. Routes through the
/// new CountersSpellFactory.PumpSpellX which respects the spell's X
/// at resolution.
/// </summary>
public sealed class VarXPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+creature(?:\s+(?:you\s+control|an\s+opponent\s+controls|you\s+don'?t\s+control))?\s+gets\s+(?<p>[+-](?:\d+|x))/(?<t>[+-](?:\d+|x))(?:\s+and\s+gains?\s+[\w\s,-]+?)?\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 70; // beats fixed PumpCreature (50) when X is in either axis
    public string Name => "VarXPump";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var p = m.Groups["p"].Value;
        var t = m.Groups["t"].Value;
        // Only fire when at least one axis is X — otherwise fixed-numeric
        // templates handle it.
        bool hasX = p.Contains('x', StringComparison.OrdinalIgnoreCase) ||
                    t.Contains('x', StringComparison.OrdinalIgnoreCase);
        if (!hasX) return null;
        return new Dictionary<string, string> { ["p"] = p, ["t"] = t };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        Majik.Core.CardData.SpellTemplates.Templates.Counters.CountersSpellFactory.PumpSpellX(
            @params["p"], @params["t"], ctx.Resolver);
}

/// <summary>
/// "Add {fixed mana cost}." — Dark Ritual, Cabal Ritual, Pyretic
/// Ritual, Seething Song. Parses the mana sequence and adds it to
/// the caster's mana pool at resolution.
/// </summary>
public sealed class AddFixedManaTemplate : ISpellTemplate
{
    // "Add {B}{B}{B}." with no trailing clause (single-line). Multi-clause
    // ritual variants (threshold-add, per-creature-add) fall through to
    // the composer or single-template path.
    private static readonly Regex Pattern = new(
        @"^\s*add\s+(?<mana>(?:\{[^\}]+\})+)\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "AddFixedMana";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["mana"] = m.Groups["mana"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var caster = ctx.Caster;
        // Strip braces — ManaCost.Parse accepts both bracketed and
        // bracket-less forms but normalizing keeps it predictable.
        var raw = @params["mana"];
        var compact = raw.Replace("{", "").Replace("}", "");
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect($"add {compact}", () =>
            {
                var cost = Majik.Core.ValueObjects.ManaCost.Parse(compact);
                caster.AddManaToPool(cost);
            }) });
    }
}
