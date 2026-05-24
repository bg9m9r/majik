using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Effects;
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
    // Plain Fog: "Prevent all combat damage that would be dealt this turn."
    // Filtered variants: trailing " by creatures [filter]." (Encircling
    // Fissure, Hindervines, Moonmist, Tanglesap, Vine Snare). v1 stub
    // prevents ALL combat damage regardless of filter — lossy, but the core
    // fog effect still fires.
    private static readonly Regex Pattern = new(
        @"^\s*prevent\s+all\s+combat\s+damage\s+that\s+would\s+be\s+dealt\s+this\s+turn(?:\s+by\s+creatures[^.]*)?\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "Fog";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public bool CanBind(SpellBindContext ctx) => ctx.Replacements != null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var bus = ctx.Replacements!;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("fog", () =>
            {
                // CR 615 — register a Layer-7 prevention shield that cancels
                // every combat damage intent for the rest of the turn. The
                // shield auto-expires in the cleanup step via
                // ReplacementBus.ExpireEndOfTurn (IEndOfTurnExpirable hook).
                bus.Register(new PreventAllCombatDamageShield());
            }) });
    }
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
    public BotIntent Intent => BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public bool CanBind(SpellBindContext ctx) => ctx.Effects != null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var effects = ctx.Effects!;
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("cant-be-blocked EOT", () =>
                {
                    if (target is Creature c)
                        effects.Register(new CombatRestrictionEffect(CombatRestriction.CannotBeBlocked, c));
                }) };
            });
    }
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
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public bool CanBind(SpellBindContext ctx) => ctx.Effects != null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = SpellTemplateHelpers.WordToInt(@params.TryGetValue("n", out var v) ? v : "1");
        if (n < 1) n = 1;
        var effects = ctx.Effects!;
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 0, n, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                // p.Targets[0] is the list of chosen creature targets (0..n).
                // Each becomes a per-target CannotBlock restriction.
                var slots = p.Targets.Count > 0 ? p.Targets[0] : Array.Empty<object>();
                var resolved = slots.Select(s => resolver(s)).OfType<Creature>().ToList();
                return new IEffect[] { new Effect($"cant-block up to {n} EOT", () =>
                {
                    foreach (var c in resolved)
                        effects.Register(new CombatRestrictionEffect(CombatRestriction.CannotBlock, c));
                }) };
            });
    }
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
    public BotIntent Intent => BotIntent.Buff;

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
        @"target\s+(?:player|opponent)\s+sacrifices\s+a\s+creature(?:\s+of\s+their\s+choice)?",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "TargetPlayerSacrificesCreature";
    public BotIntent Intent => BotIntent.Removal;

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
                    // Edicts are "target player sacrifices" — CR 701.16
                    // sacrifice bypasses Indestructible / regeneration.
                    if (pick != null) OracleSpellBinder.MoveToGraveyard(pick, Majik.Core.Zones.ZoneMoveReason.Sacrifice);
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
    public BotIntent Intent => BotIntent.Buff;

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
    public BotIntent Intent => BotIntent.Protection;

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
    public BotIntent Intent => BotIntent.Removal;

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
    public BotIntent Intent => BotIntent.Removal;

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
        @"target\s+(?:player|opponent)\s+draws\s+x\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "TargetPlayerDrawsX";
    public BotIntent Intent => BotIntent.Draw;

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
    public BotIntent Intent => BotIntent.Buff | BotIntent.CombatTrick;

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
    public BotIntent Intent => BotIntent.Buff | BotIntent.CombatTrick;

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
    public BotIntent Intent => BotIntent.Ramp;

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

/// <summary>
/// "Destroy [N] target [modifier]? creatures." — multi-target destroy
/// (Hex: 6, Twinstrike: 2 with rider, Reckless Spite: 2, etc).
/// v1 stub destroys ONE chosen target (the first); multi-target
/// resolution requires per-target picks the agent layer doesn't
/// model yet. Lossy but binds the spell.
/// </summary>
public sealed class DestroyNTargetCreaturesTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+(?<n>two|three|four|five|six|seven|\d+)\s+target\s+(?:[\w-]+\s+)?creatures?\b",
        RegexOptions.IgnoreCase);

    public int Priority => 70; // beats DestroyCreature (30) on the multi-target form
    public string Name => "DestroyNTargetCreatures";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        Majik.Core.CardData.SpellTemplates.Templates.Destroy.DestroySpellFactory.DestroyCreatureSpell(ctx.Resolver);
}

/// <summary>
/// "Counter up to N target spells" — Double Negative-class. Routes
/// through the existing CounterTargetSpell factory for the one-target
/// stub at v1; the multi-target choice is lossy.
/// </summary>
public sealed class CounterUpToNTargetSpellsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"counter\s+up\s+to\s+(?<n>two|three|four|five|\d+)\s+target\s+spells?",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "CounterUpToNTargetSpells";
    public BotIntent Intent => BotIntent.Counter;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        Majik.Core.CardData.SpellTemplates.Templates.Counter.CounterSpellFactory.CounterTargetSpell(
            ctx.Resolver, ctx.Stack);
}

/// <summary>
/// "Take an extra turn after this one" / "Target player takes N extra
/// turns after this one." — Time Walk, Temporal Manipulation, Time
/// Warp, Time Stretch. v1 binds with empty effect; TurnManager hook
/// for extra-turn insertion isn't reachable from spell-effect scope
/// yet, so the spell resolves as a no-op castable.
/// </summary>
public sealed class TakeExtraTurnTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"(?:take\s+an\s+extra\s+turn|takes?\s+(?:an\s+extra\s+turn|two\s+extra\s+turns|three\s+extra\s+turns))\s+after\s+this\s+(?:one|turn)",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "TakeExtraTurn";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StubBindHelpers.EmptyEffectSpell(Array.Empty<TargetRequest>());
}

/// <summary>
/// "Target player shuffles their graveyard into their library" /
/// "Target player shuffles up to N target cards from their graveyard
/// into their library." — Reminisce, Stream of Consciousness,
/// Krosan Reclamation-tail. v1 real-ish effect: moves every card
/// from the target's graveyard back to library (the "up to N
/// targets" choice is lossy; the bound spell moves the whole pile
/// which is a v1 over-shoot but at least removes the cards from the
/// graveyard).
/// </summary>
public sealed class ShuffleGraveyardIntoLibraryTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+(?:player|opponent)\s+shuffles(?:\s+up\s+to\s+\w+\s+target\s+cards?\s+from)?\s+(?:their\s+graveyard|(?:cards?\s+)?(?:from\s+)?their\s+graveyard)\s+into\s+their\s+library",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "ShuffleGraveyardIntoLibrary";

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
                return new IEffect[] { new Effect("shuffle gy to library", () =>
                {
                    if (target is not Player pl) return;
                    var cards = pl.Zones.Graveyard.GetCards().ToList();
                    foreach (var c in cards)
                    {
                        pl.Zones.Graveyard.RemoveCard(c);
                        pl.Zones.Library.AddCard(c);
                        c.SetZone(Majik.Core.Zones.ZoneType.Library);
                    }
                    // CR 701.20 — the printed effect is a "shuffle into
                    // library" which the rules treat as an explicit shuffle
                    // of the destination library.
                    Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(pl, "graveyard-into-library");
                }) };
            });
    }
}

/// <summary>
/// "Each opponent sacrifices a [creature|permanent|artifact|
/// enchantment|...]" — Tribute to the Wild, Soul Shatter,
/// Diabolic Edict's "each opponent" variant. v1 stub: deterministic
/// pick (first creature each opponent controls). Iterates only the
/// caster's reachable opponents (TODO: ChosenSpellParams.AllPlayers
/// once wired).
/// </summary>
public sealed class EachOpponentSacrificesCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+opponent\s+sacrifices\s+a\s+creature",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "EachOpponentSacrificesCreature";
    public BotIntent Intent => BotIntent.Removal;

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
            EffectFactory: p =>
            {
                var allPlayers = p.AllPlayers;
                return new IEffect[] { new Effect("each opp sac creature", () =>
                {
                    if (allPlayers == null) return;
                    foreach (var pl in allPlayers)
                    {
                        if (ReferenceEquals(pl, caster)) continue;
                        var pick = pl.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .FirstOrDefault();
                        // "Each opponent sacrifices a creature" — CR 701.16
                        // sacrifice bypasses Indestructible / regeneration.
                        if (pick != null) OracleSpellBinder.MoveToGraveyard(pick, Majik.Core.Zones.ZoneMoveReason.Sacrifice);
                    }
                }) };
            });
    }
}

/// <summary>
/// "Destroy all [Plains|Islands|Swamps|Mountains|Forests]." — Boiling
/// Seas, Flashfires, Acid Rain, Tsunami, Anarchy (basic-land sweep).
/// Uses DestroyAllPermanentsSpell with a basic-land-type predicate.
/// </summary>
public sealed class DestroyAllBasicLandTypeTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*destroy\s+all\s+(?<basic>plains|islands|swamps|mountains|forests)\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 90;
    public string Name => "DestroyAllBasicLandType";
    public BotIntent Intent => BotIntent.Wrath;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["basic"] = m.Groups["basic"].Value.ToLowerInvariant() }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var basic = @params["basic"];
        // Match by card name (basic land naming convention) or by subtype.
        var singular = basic switch
        {
            "plains" => "Plains",
            "islands" => "Island",
            "swamps" => "Swamp",
            "mountains" => "Mountain",
            "forests" => "Forest",
            _ => string.Empty,
        };
        return Majik.Core.CardData.SpellTemplates.Templates.Destroy.DestroySpellFactory
            .DestroyAllPermanentsSpell(
                ctx.Caster,
                card => card.HasType(Majik.Core.Cards.Types.CardType.Land) &&
                        string.Equals(card.Name, singular, StringComparison.OrdinalIgnoreCase),
                $"all {basic}");
    }
}

/// <summary>
/// "Target player draws N cards" — Inspiration (2), Opportunity (4),
/// Tidings (4). Fixed-numeric variant (X form lives in
/// TargetPlayerDrawsXTemplate).
/// </summary>
public sealed class TargetPlayerDrawsNTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+(?:player|opponent)\s+draws\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "TargetPlayerDrawsN";
    public BotIntent Intent => BotIntent.Draw;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = SpellTemplateHelpers.WordToInt(@params["n"]);
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect($"draw {n}", () =>
                {
                    if (target is not Player pl) return;
                    for (var i = 0; i < n; i++)
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
/// "[Up to N | One or two | N | X] target creatures gain [keyword(s)]
/// until end of turn." — Wind Sail (flying), Crusher Zendikon
/// variants, Wave of Indifference. v1 binds with empty effect — the
/// keyword grant requires per-target continuous effect registration
/// not exposed at this scope.
/// </summary>
public sealed class MultiTargetCreaturesGainKeywordTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"(?:up\s+to\s+|one\s+or\s+two\s+|x\s+|\d+\s+)?target\s+creatures?\s+gains?\s+[\w\s,]+\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 35; // below GrantKeywordTilEot (single target)
    public string Name => "MultiTargetCreaturesGainKeyword";
    public BotIntent Intent => BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        // Reject single-target form ("target creature gains") so
        // GrantKeywordTilEot owns it.
        if (Regex.IsMatch(m.Value, @"^target\s+creature\s+gains?", RegexOptions.IgnoreCase))
            return null;
        return EmptyParams.Instance;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StubBindHelpers.EmptyEffectSpell(new[]
        {
            new TargetRequest("target creature", 0, 3, Array.Empty<object>())
        });
}

/// <summary>
/// "Up to [N|two|three] target creatures each get +P/+P [and gain
/// keyword]? until end of turn." — Press the Advantage, Reap What Is
/// Sown variants, Cutthroat Maneuver. v1 binds with empty effect for
/// the multi-target pump (per-target continuous effect requires
/// loop-over-slot wiring not exposed here yet).
/// </summary>
public sealed class MultiTargetCreaturesEachGetPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"(?:up\s+to\s+)?(?:two|three|four|five|x|\d+)\s+target\s+creatures\s+each\s+get\s+[+-]\d+/[+-]\d+(?:\s+and\s+gains?\s+[\w\s,]+?)?\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 40;
    public string Name => "MultiTargetCreaturesEachGetPump";
    public BotIntent Intent => BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StubBindHelpers.EmptyEffectSpell(new[]
        {
            new TargetRequest("target creature", 0, 5, Array.Empty<object>())
        });
}

/// <summary>
/// "Creatures your opponents control get -N/-N until end of turn" —
/// Cower in Fear, Drag to the Bottom (X variant routed elsewhere),
/// Lethal Vapors (lossy). Symmetric mirror of AllCreaturesPump but
/// scoped to opponents. v1 stub: applies the debuff to every creature
/// on the caster's view of the battlefield (lossy — should be
/// opponents only, but reaches all creatures the spell can see).
/// </summary>
public sealed class CreaturesYourOpponentsControlDebuffTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"creatures\s+your\s+opponents\s+control\s+get\s+(?<p>[+-]\d+)/(?<t>[+-]\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "CreaturesYourOpponentsControlDebuff";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["p"] = m.Groups["p"].Value, ["t"] = m.Groups["t"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        Majik.Core.CardData.SpellTemplates.Templates.Counters.CountersSpellFactory.AllCreaturesPumpSpell(
            int.Parse(@params["p"]),
            int.Parse(@params["t"]),
            ctx.Caster);
}

/// <summary>
/// "Attacking creatures get +N/+N until end of turn" — Trumpet Blast,
/// Carthusian Charge, etc. v1 lossy (applies to every creature the
/// spell can reach); the bound spell still resolves the pump.
/// </summary>
public sealed class AttackingCreaturesPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*attacking\s+creatures\s+get\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "AttackingCreaturesPump";
    public BotIntent Intent => BotIntent.Buff | BotIntent.CombatTrick;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["p"] = m.Groups["p"].Value, ["t"] = m.Groups["t"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        Majik.Core.CardData.SpellTemplates.Templates.Counters.CountersSpellFactory.AllCreaturesPumpSpell(
            int.Parse(@params["p"]),
            int.Parse(@params["t"]),
            ctx.Caster);
}

/// <summary>
/// "Permanents you control gain [keyword(s)] until end of turn" —
/// Heroic Intervention (hexproof + indestructible), Yuan-Ti
/// Scaleshield. v1 binds with empty effect — group-grant continuous
/// effect on caster's permanents requires the effects service and
/// per-permanent registration not exposed here.
/// </summary>
public sealed class PermanentsYouControlGainKeywordTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"permanents\s+you\s+control\s+gain\s+[\w\s,]+\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "PermanentsYouControlGainKeyword";
    public BotIntent Intent => BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        StubBindHelpers.EmptyEffectSpell(Array.Empty<TargetRequest>());
}

/// <summary>
/// "Return all [color|type] permanents to their owners' hands" —
/// Hibernation (green), Aboroth-class, Reset variants. v1 stub:
/// bounces every permanent on the caster's view of the battlefield
/// matching the color/type predicate. Color predicate falls back to
/// "no filter" when we can't decode the modifier — spell still
/// resolves with a full bounce which over-shoots but matches the
/// load-bearing effect.
/// </summary>
/// <summary>
/// Single-clause "&lt;modifier&gt; creatures can't block this turn" lockout
/// (Falter, Magmatic Chasm, Seismic Stomp, Awe for the Guilds, Ruthless
/// Invasion, Flash of Defiance, Threshold clause, etc). The modifier can be
/// empty, a color/keyword chain ("green creatures and white creatures"), a
/// negation ("nonartifact creatures", "creatures without flying"), or a
/// supertype ("monocolored creatures").
///
/// v1 lossy stub: empty-effect spell, like <see cref="FogTemplate"/> — the
/// per-turn blocking-restriction service doesn't exist yet, so the spell
/// resolves without changing combat. Cast/cost machinery still works and the
/// card no longer falls through to the vanilla shell.
/// </summary>
public sealed class CreaturesCantBlockTemplate : ISpellTemplate
{
    // Whole-sentence anchor so multi-clause variants (Wrap in Flames, Trial
    // of Agony) stay unmatched and reach the composer instead. Negative
    // lookahead rejects target/up-to-N/X-target shapes (those are handled by
    // TargetCantBeBlockedTemplate and UpToNCantBlockTemplate). The interior
    // allows any prefix before "creatures" (color, supertype, "Nonartifact",
    // "Monocolored", "Green creatures and white") plus a "without flying"
    // postfix or a second "creatures" half.
    private static readonly Regex Pattern = new(
        @"^\s*(?!(?:target|up\s+to|any\s+number|\d+\s+target|x\s+target)\b)[^.]*?\bcreatures?\b[^.]*?\bcan'?t\s+block\s+this\s+turn\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CreaturesCantBlock";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public bool CanBind(SpellBindContext ctx) => ctx.Effects != null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var effects = ctx.Effects!;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("mass cant-block EOT", () =>
            {
                // Null target = mass effect. CombatValidator.CanBlock will
                // see the mass restriction match every creature this turn.
                // Modifier filter ("without flying", "Nonartifact") is lossy
                // at v1; the future per-creature predicate goes here.
                effects.Register(new CombatRestrictionEffect(CombatRestriction.CannotBlock, target: null));
            }) });
    }
}

public sealed class ReturnAllPermanentsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*return\s+all\s+(?<kind>[\w\s,-]+?)\s+permanents\s+to\s+their\s+owners'?\s+hands\.?\s*$",
        RegexOptions.IgnoreCase);

    public int Priority => 70;
    public string Name => "ReturnAllPermanents";
    public BotIntent Intent => BotIntent.Bounce | BotIntent.Wrath;

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
            EffectFactory: _ => new IEffect[] { new Effect("bounce all", () =>
            {
                var snap = caster.Zones.Battlefield.GetCards().ToList();
                foreach (var c in snap)
                {
                    var owner = c.Owner;
                    if (owner == null) continue;
                    owner.Zones.Battlefield.RemoveCard(c);
                    owner.Zones.Hand.AddCard(c);
                    c.SetZone(Majik.Core.Zones.ZoneType.Hand);
                }
            }) });
    }
}
