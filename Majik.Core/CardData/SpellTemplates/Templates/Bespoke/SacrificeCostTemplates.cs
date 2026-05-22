using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Bespoke templates for "As an additional cost to cast this spell,
/// sacrifice a creature." cards (CR 601.2f). Each template detects the
/// additional-cost prefix on <see cref="SpellBindContext.RawText"/> (the
/// <see cref="OracleTextNormalizer"/> strips it before <c>Text</c>, so
/// any post-strip template would otherwise match a cost-free version of
/// the card).
///
/// Cards covered here are the small set whose effect resolves directly
/// against the sacrificed creature reference or has no other matching
/// template:
/// <list type="bullet">
///   <item>Blood for Bones — return-to-battlefield + return-to-hand combo</item>
///   <item>Infernal Plunge — Add {R}{R}{R}.</item>
///   <item>Fling / Thud — deals damage = sacrificed creature's power to any target</item>
///   <item>Ichor Explosion — All creatures get -X/-X where X = sacrificed power</item>
///   <item>Life's Legacy — Draw cards = sacrificed creature's power</item>
///   <item>Momentous Fall — Draw cards = power, gain life = toughness</item>
///   <item>Tormented Thoughts — target player discards N = sacrificed power</item>
///   <item>Hatred — Pay X life, target gets +X/+0</item>
/// </list>
///
/// The shared <see cref="SacrificeCostHelpers.AdditionalCosts"/> helper
/// produces an <see cref="IAdditionalCost"/> list keyed off the raw
/// oracle text — templates attach this to <see cref="SpellDefinition"/>
/// so <see cref="SpellCastFlow"/> charges the cost. Effects read the
/// paid creature via <see cref="ChosenSpellParams.AdditionalCostPayments"/>.
/// </summary>
internal static class SacrificeCostHelpers
{
    public static readonly Regex SacCreaturePrefix = new(
        @"^\s*as\s+an\s+additional\s+cost\s+to\s+cast\s+this\s+spell,\s+sacrifice\s+a\s+creature\.\s*",
        RegexOptions.IgnoreCase);

    public static readonly Regex SacArtifactPrefix = new(
        @"^\s*as\s+an\s+additional\s+cost\s+to\s+cast\s+this\s+spell,\s+sacrifice\s+an\s+artifact\.\s*",
        RegexOptions.IgnoreCase);

    public static readonly Regex PayXLifePrefix = new(
        @"^\s*as\s+an\s+additional\s+cost\s+to\s+cast\s+this\s+spell,\s+pay\s+x\s+life\.\s*",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Pulls the first <see cref="SacrificeACreatureAdditionalCost"/>
    /// from a chosen-spell params payment list — returns null when no
    /// such cost was paid (e.g. the spell was cast via a code path that
    /// bypassed the binder).
    /// </summary>
    public static Creature? SacrificedCreature(ChosenSpellParams p)
    {
        foreach (var cost in p.AdditionalCostPaymentsOrEmpty)
        {
            if (cost is SacrificeACreatureAdditionalCost sac && sac.Sacrificed is Creature c)
                return c;
        }
        return null;
    }
}

/// <summary>
/// Blood for Bones — "Return a creature card from your graveyard to the
/// battlefield, then return another creature card from your graveyard to
/// your hand." Plus the additional sacrifice cost.
/// </summary>
public sealed class BloodForBonesTemplate : ISpellTemplate
{
    public int Priority => 95;
    public string Name => "BloodForBones";
    public BotIntent Intent => BotIntent.Reanimate;

    private static bool Detect(string raw, string text) =>
        SacrificeCostHelpers.SacCreaturePrefix.IsMatch(raw) &&
        Regex.IsMatch(text,
            @"return\s+a\s+creature\s+card\s+from\s+your\s+graveyard\s+to\s+the\s+battlefield",
            RegexOptions.IgnoreCase) &&
        Regex.IsMatch(text,
            @"return\s+another\s+creature\s+card\s+from\s+your\s+graveyard\s+to\s+your\s+hand",
            RegexOptions.IgnoreCase);

    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Detect(ctx.RawText, ctx.Text) ? EmptyParams.Instance : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"BloodForBones Rehydrate could not bind '{ctx.Entity.Name}'.");

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (!Detect(ctx.RawText, ctx.Text)) return null;

        var caster = ctx.Caster;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("blood for bones", () =>
            {
                var gy = caster.Zones.Graveyard.GetCards()
                    .OfType<Creature>()
                    .ToList();
                if (gy.Count == 0) return;
                var first = gy[0];
                caster.Zones.Graveyard.RemoveCard(first);
                caster.Zones.Battlefield.AddCard(first);
                first.SetZone(ZoneType.Battlefield);
                first.SetController(caster);
                // pick a different one for hand return
                var second = gy.Skip(1).FirstOrDefault();
                if (second == null) return;
                caster.Zones.Graveyard.RemoveCard(second);
                caster.Zones.Hand.AddCard(second);
                second.SetZone(ZoneType.Hand);
            }) },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}

/// <summary>
/// Infernal Plunge — "Add {R}{R}{R}." with sacrifice-a-creature cost.
/// </summary>
public sealed class InfernalPlungeTemplate : ISpellTemplate
{
    public int Priority => 95;
    public string Name => "InfernalPlunge";
    public BotIntent Intent => BotIntent.Ramp;

    // Strict "Add {R}{R}{R}." match — Infernal Plunge is uniquely 3 red.
    private static bool Detect(string raw, string text) =>
        SacrificeCostHelpers.SacCreaturePrefix.IsMatch(raw) &&
        Regex.IsMatch(text, @"^\s*add\s+\{r\}\{r\}\{r\}\.\s*$", RegexOptions.IgnoreCase);

    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Detect(ctx.RawText, ctx.Text) ? EmptyParams.Instance : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"InfernalPlunge Rehydrate could not bind '{ctx.Entity.Name}'.");

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (!Detect(ctx.RawText, ctx.Text)) return null;

        var caster = ctx.Caster;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("add RRR", () =>
            {
                caster.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("RRR"));
            }) },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}

/// <summary>
/// Fling-style damage-equal-to-power-of-sacrificed-creature. Covers
/// Fling, Thud, Rite of Consumption (which is target player/planeswalker
/// but resolves identically for v1).
/// </summary>
public sealed class FlingLikeTemplate : ISpellTemplate
{
    private static readonly Regex DamageAnyTargetPattern = new(
        @"deals?\s+damage\s+equal\s+to\s+the\s+sacrificed\s+creature'?s\s+power\s+to\s+any\s+target",
        RegexOptions.IgnoreCase);

    private static readonly Regex DamageTargetPlayerOrPwPattern = new(
        @"deals?\s+damage\s+equal\s+to\s+the\s+sacrificed\s+creature'?s\s+power\s+to\s+target\s+player\s+or\s+planeswalker",
        RegexOptions.IgnoreCase);

    public int Priority => 95;
    public string Name => "FlingLike";
    public BotIntent Intent => BotIntent.Burn | BotIntent.Reach;

    private static bool Detect(string raw, string text) =>
        SacrificeCostHelpers.SacCreaturePrefix.IsMatch(raw) &&
        (DamageAnyTargetPattern.IsMatch(text) || DamageTargetPlayerOrPwPattern.IsMatch(text));

    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Detect(ctx.RawText, ctx.Text) ? EmptyParams.Instance : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"FlingLike Rehydrate could not bind '{ctx.Entity.Name}'.");

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (!SacrificeCostHelpers.SacCreaturePrefix.IsMatch(ctx.RawText)) return null;

        bool anyTarget = DamageAnyTargetPattern.IsMatch(ctx.Text);
        bool playerOrPw = !anyTarget && DamageTargetPlayerOrPwPattern.IsMatch(ctx.Text);
        if (!anyTarget && !playerOrPw) return null;

        var resolver = ctx.Resolver;
        var lifeGain = Regex.IsMatch(ctx.Text,
            @"you\s+gain\s+life\s+equal\s+to\s+the\s+damage", RegexOptions.IgnoreCase);
        var caster = ctx.Caster;

        var targetLabel = anyTarget ? "any target" : "target player or planeswalker";

        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest(targetLabel, 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                var sacrificed = SacrificeCostHelpers.SacrificedCreature(p);
                return new IEffect[] { new Effect("fling-like damage", () =>
                {
                    if (sacrificed == null) return;
                    var dmg = sacrificed.Power;
                    if (dmg <= 0) return;
                    OracleSpellBinder.DealDamage(target, dmg);
                    if (lifeGain) caster.GainLife(dmg);
                }) };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}

/// <summary>
/// Ichor Explosion — "All creatures get -X/-X until end of turn, where
/// X is the sacrificed creature's power."
/// </summary>
public sealed class IchorExplosionTemplate : ISpellTemplate
{
    public int Priority => 95;
    public string Name => "IchorExplosion";
    public BotIntent Intent => BotIntent.Removal;

    // NOTE: Detect intentionally does NOT require ctx.Effects (offline
    // compile has no Effects service). The Effects gate is enforced in
    // CanBind below, which both the live TryBind and the compiled fast
    // path consult — so a row gets persisted even though Rehydrate at a
    // vanilla-cast site would be rejected.
    private static bool Detect(string raw, string text) =>
        SacrificeCostHelpers.SacCreaturePrefix.IsMatch(raw) &&
        Regex.IsMatch(text,
            @"all\s+creatures\s+get\s+-x/-x\s+until\s+end\s+of\s+turn,\s+where\s+x\s+is\s+the\s+sacrificed\s+creature'?s\s+power",
            RegexOptions.IgnoreCase);

    public bool CanBind(SpellBindContext ctx) => ctx.Effects is not null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Detect(ctx.RawText, ctx.Text) ? EmptyParams.Instance : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"IchorExplosion Rehydrate could not bind '{ctx.Entity.Name}'.");

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (!Detect(ctx.RawText, ctx.Text)) return null;
        if (ctx.Effects is null) return null;

        var effects = ctx.Effects;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var sacrificed = SacrificeCostHelpers.SacrificedCreature(p);
                return new IEffect[] { new Effect("ichor explosion", () =>
                {
                    if (sacrificed == null) return;
                    var x = sacrificed.Power;
                    if (x <= 0) return;
                    foreach (var pl in p.AllPlayers ?? Array.Empty<Player>())
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                        {
                            if (c.ActiveEffects is not null)
                            {
                                c.ActiveEffects.Register(
                                    new Majik.Core.Effects.PumpUntilEndOfTurnEffect(c, -x, -x));
                            }
                        }
                    }
                }) };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}

/// <summary>
/// Life's Legacy — "Draw cards equal to the sacrificed creature's
/// power." Momentous Fall is similar but also gains life equal to
/// toughness.
/// </summary>
public sealed class LifesLegacyTemplate : ISpellTemplate
{
    public int Priority => 95;
    public string Name => "LifesLegacy";
    public BotIntent Intent => BotIntent.Draw;

    private static (bool drawOnly, bool drawAndGain) Detect(string raw, string text)
    {
        if (!SacrificeCostHelpers.SacCreaturePrefix.IsMatch(raw)) return (false, false);
        var drawOnly = Regex.IsMatch(text,
            @"^\s*draw\s+cards\s+equal\s+to\s+the\s+sacrificed\s+creature'?s\s+power\.\s*$",
            RegexOptions.IgnoreCase);
        var drawAndGain = Regex.IsMatch(text,
            @"you\s+draw\s+cards\s+equal\s+to\s+the\s+sacrificed\s+creature'?s\s+power,\s+then\s+you\s+gain\s+life\s+equal\s+to\s+its\s+toughness",
            RegexOptions.IgnoreCase);
        return (drawOnly, drawAndGain);
    }

    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var (a, b) = Detect(ctx.RawText, ctx.Text);
        return a || b ? EmptyParams.Instance : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"LifesLegacy Rehydrate could not bind '{ctx.Entity.Name}'.");

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var (drawOnly, drawAndGain) = Detect(ctx.RawText, ctx.Text);
        if (!drawOnly && !drawAndGain) return null;

        var caster = ctx.Caster;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var sacrificed = SacrificeCostHelpers.SacrificedCreature(p);
                return new IEffect[] { new Effect("life's legacy", () =>
                {
                    if (sacrificed == null) return;
                    var n = sacrificed.Power;
                    for (var i = 0; i < n; i++)
                    {
                        var top = caster.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null) break;
                        caster.Zones.Library.RemoveCard(top);
                        caster.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }
                    if (drawAndGain)
                    {
                        var t = sacrificed.Toughness;
                        if (t > 0) caster.GainLife(t);
                    }
                }) };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}

/// <summary>
/// Tormented Thoughts — "Target player discards a number of cards equal
/// to the sacrificed creature's power."
/// </summary>
public sealed class TormentedThoughtsTemplate : ISpellTemplate
{
    public int Priority => 95;
    public string Name => "TormentedThoughts";
    public BotIntent Intent => BotIntent.Discard;

    private static bool Detect(string raw, string text) =>
        SacrificeCostHelpers.SacCreaturePrefix.IsMatch(raw) &&
        Regex.IsMatch(text,
            @"target\s+player\s+discards\s+a\s+number\s+of\s+cards\s+equal\s+to\s+the\s+sacrificed\s+creature'?s\s+power",
            RegexOptions.IgnoreCase);

    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Detect(ctx.RawText, ctx.Text) ? EmptyParams.Instance : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"TormentedThoughts Rehydrate could not bind '{ctx.Entity.Name}'.");

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (!Detect(ctx.RawText, ctx.Text)) return null;

        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                var sacrificed = SacrificeCostHelpers.SacrificedCreature(p);
                return new IEffect[] { new Effect("tormented thoughts", () =>
                {
                    if (sacrificed == null) return;
                    if (target is not Player victim) return;
                    var n = sacrificed.Power;
                    for (var i = 0; i < n; i++)
                    {
                        var hand = victim.Zones.Hand.GetCards().ToList();
                        if (hand.Count == 0) break;
                        var pick = hand[0];
                        victim.Zones.Hand.RemoveCard(pick);
                        victim.Zones.Graveyard.AddCard(pick);
                        pick.SetZone(ZoneType.Graveyard);
                    }
                }) };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}

/// <summary>
/// Hatred — "Target creature gets +X/+0 until end of turn." with
/// additional cost "pay X life". The X cost is picked by the agent via
/// the normal X-cost path; the life payment uses that same X value.
/// </summary>
public sealed class HatredTemplate : ISpellTemplate
{
    public int Priority => 95;
    public string Name => "Hatred";
    public BotIntent Intent => BotIntent.Buff;

    // Detect doesn't require ctx.Effects so the offline compile can
    // persist a row; live-bind / Rehydrate still gate on Effects.
    private static bool Detect(string raw, string text) =>
        SacrificeCostHelpers.PayXLifePrefix.IsMatch(raw) &&
        Regex.IsMatch(text,
            @"target\s+creature\s+gets\s+\+x/\+0\s+until\s+end\s+of\s+turn",
            RegexOptions.IgnoreCase);

    public bool CanBind(SpellBindContext ctx) => ctx.Effects is not null;

    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Detect(ctx.RawText, ctx.Text) ? EmptyParams.Instance : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"Hatred Rehydrate could not bind '{ctx.Entity.Name}'.");

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (!Detect(ctx.RawText, ctx.Text)) return null;
        if (ctx.Effects is null) return null;

        var resolver = ctx.Resolver;
        var caster = ctx.Caster;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: true,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                var x = p.X ?? 0;
                return new IEffect[]
                {
                    // Pay X life as the additional cost. The cost object
                    // can't capture X at bind time (X is chosen later in
                    // the cast flow) so we pay it here in the effect
                    // sequence. CR 601.2f timing is slightly off (cost
                    // should resolve before mana payment); v1 acceptable.
                    new Effect($"pay {x} life", () => { if (x > 0) caster.LoseLife(x); }),
                    new Effect($"pump +{x}/+0", () =>
                    {
                        if (target is not Creature c) return;
                        if (x == 0) return;
                        if (c.ActiveEffects is null) return;
                        c.ActiveEffects.Register(
                            new Majik.Core.Effects.PumpUntilEndOfTurnEffect(c, x, 0));
                    })
                };
            });
    }
}
