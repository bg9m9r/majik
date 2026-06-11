using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the instant Abandon Reason (Eventide, {1}{R}).
///
/// Oracle text (verified against Scryfall 2026-06-10):
///   "Up to two target creatures each get +1/+0 and gain first strike
///    until end of turn.
///    Madness {1}{R} (If you discard this card, discard it into exile.
///    When you do, you may cast it for its madness cost. If you don't,
///    put it into your graveyard.)"
///
/// ## Madness — intrinsic, NOT wired here (CR 702.35)
/// Madness works for every catalogued card via
/// <c>Majik.Core/Keywords/MadnessCatalog.cs</c> (name → cost) consulted by
/// the central discard funnel <c>Fx.DiscardCard</c>: a discarded madness
/// card is routed to exile and offered for its madness cost automatically.
/// Abandon Reason is catalogued at {1}{R}, so the "Madness {1}{R}" line
/// needs no factory code. This factory implements ONLY the spell body.
///
/// ## Implemented (v1)
/// - Instant identity at {1}{R} (red, mana value 2), built from the embedded
///   JSON def (<c>abandon-reason.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>"Up to two target creatures each get +1/+0"</b> — a fixed Layer 7c
///   pump (CR 613.4d) registered per legal target via
///   <see cref="PumpUntilEndOfTurnEffect"/> (+1/+0). Up-to-two targeting is
///   MinTargets 0 / MaxTargets 2 (CR 601.2c), mirroring the multi-target
///   shape of <see cref="ForkedBoltFactory"/>.
/// - <b>"and gain first strike until end of turn"</b> — Layer 6 grant
///   (CR 613.1c) per target via
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> ("First strike",
///   CR 702.7). Same grant pattern as <see cref="LegionLeadershipFactory"/>.
/// - CR 514.2 — both effects expire at cleanup.
/// - <b>CR 608.2b guards</b>: each chosen target is independently resolved
///   and dropped if it is not a Creature on the battlefield with a live
///   continuous-effects service; remaining legal targets still resolve.
/// </summary>
[CardName("Abandon Reason")]
public static class AbandonReasonFactory
{
    public const string CardName = "Abandon Reason";
    public const string Slug = "abandon-reason";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>Granted keyword — CR 702.7 First strike.</summary>
    public const string GrantedKeyword = "First strike";

    /// <summary>Pump applied to each target (CR 613.4d).</summary>
    public const int PumpPower = 1;
    public const int PumpToughness = 0;

    /// <summary>
    /// Build Abandon Reason as an Instant from the embedded JSON def, with
    /// owner / controller wired. Suitable for identity / shape / dispatcher
    /// tests.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Abandon Reason.
    ///
    /// One 0..2 "target creature" request (CR 601.2c — "up to two"). On
    /// resolution each chosen target that is still a Creature on the
    /// battlefield (CR 608.2b) gets +1/+0 and first strike until end of turn.
    /// </summary>
    /// <param name="targetResolver">Maps each agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to two target creatures",
                    MinTargets: 0,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CombatTrick),
            },
            EffectFactory: chosen =>
            {
                var rawTargets = chosen.Targets[0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: up to two creatures get +1/+0 and first strike until end of turn",
                        () =>
                        {
                            foreach (var token in rawTargets)
                            {
                                Resolve(targetResolver(token));
                            }
                        }),
                };
            });
    }

    // -------------------------------------------------------------------------
    // Resolution body — per target
    // -------------------------------------------------------------------------

    private static void Resolve(object resolved)
    {
        // CR 608.2b — illegal target: only Creatures on the battlefield with a
        // live continuous-effects service are affected.
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613.4d Layer 7c — fixed +1/+0 until end of turn.
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

        // CR 613.1c Layer 6 — grant First strike until end of turn (CR 702.7).
        // CR 514.2 — both effects expire at cleanup.
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
    }
}
