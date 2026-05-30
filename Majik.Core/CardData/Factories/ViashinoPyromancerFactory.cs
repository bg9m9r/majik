using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Viashino Pyromancer (Urza's Saga + Dominaria
/// United reprint, {1}{R}). Creature — Lizard Wizard 2/1. Oracle text
/// (verified against Scryfall):
///   "When this creature enters, it deals 2 damage to target player or
///    planeswalker."
///
/// The card's base shape (name, Creature, Lizard / Wizard subtypes,
/// {1}{R}, 2/1) is materialised from the embedded JSON definition
/// (<c>viashino-pyromancer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB triggered ability is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express targeted ETB damage triggers (same posture as
/// <see cref="StormscaleScionFactory"/> / <see cref="PlayWithFireFactory"/>,
/// whose behaviour outgrows the data-only schema).
///
/// ## Implemented (v1)
///
/// - 2/1 Lizard Wizard at printed cost {1}{R}, owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature enters,
///   it deals 2 damage to target player or planeswalker." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a single 1..1
///   <see cref="TargetRequest"/> ("target player or planeswalker"). The
///   request's <see cref="TargetRequest.CandidateGatherer"/> surfaces every
///   live <see cref="Player"/> plus every <see cref="Planeswalker"/> on any
///   battlefield (CR 115.1 — Viashino Pyromancer specifically excludes
///   creature targets), so the agent only sees legal picks. On resolution
///   the effect reads the trigger's
///   <see cref="TriggeredAbility.ChosenTargets"/>, gates to a
///   <see cref="Player"/> / <see cref="Planeswalker"/> (CR 608.2b), and
///   routes through <see cref="Fx.DealDamageAny(object, int)"/> so a
///   planeswalker target converts to loyalty removal (CR 306.8) — same
///   damage shape as <see cref="LavaAxeFactory"/> /
///   <see cref="FanaticalFirebrandFactory"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Damage source threading</b>: <see cref="Fx.DealDamageAny"/> is a
///   target-side helper that doesn't yet thread the Pyromancer through as
///   the damage source, so a future lifelink / "whenever a source you
///   control deals damage" grant won't observe it. Same primitive-level
///   posture as <see cref="VoldarenEpicureFactory"/> /
///   <see cref="QuestingBeastFactory"/>.
/// </summary>
[CardName("Viashino Pyromancer")]
public static class ViashinoPyromancerFactory
{
    public const string CardName = "Viashino Pyromancer";
    public const string Slug = "viashino-pyromancer";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>CR 119 — fixed 2 damage to the chosen target.</summary>
    public const int EtbDamageAmount = 2;

    /// <summary>
    /// Construct Viashino Pyromancer owned and controlled by
    /// <paramref name="owner"/>. The base shape comes from the embedded JSON
    /// definition; the ETB "deal 2 to target player or planeswalker" trigger
    /// is attached with a 1..1 "target player or planeswalker"
    /// <see cref="TargetRequest"/>. The trigger is attached structurally and
    /// is fully self-contained — no service wiring required. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Lizard / Wizard subtypes, {1}{R}, 2/1). The JSON carries no
        // abilities — the ETB trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 603.6a — ETB triggered ability with target.
        //   "When this creature enters, it deals 2 damage to target player
        //    or planeswalker."
        // Same shape as EarthshakerKhenraFactory's ETB-with-target trigger:
        // declare a 1..1 TargetRequest, read the chosen target out of
        // ChosenTargets at resolution, and apply the effect. Damage routes
        // through Fx.DealDamageAny so a Planeswalker target converts to
        // loyalty removal (CR 306.8).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: deal {EtbDamageAmount} damage to target player or planeswalker",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var target = chosen[0][0];

                // CR 608.2b — only Player and Planeswalker are legal targets;
                // no-op for any other resolved type (e.g. a creature via a
                // redirect). Fx.DealDamageAny routes Planeswalker damage as
                // loyalty removal (CR 306.8).
                if (target is Player || target is Planeswalker)
                {
                    Fx.DealDamageAny(target, EtbDamageAmount);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn,
                    // Live candidate gatherer (agent-prompt MVP). CR 115.1 —
                    // legal targets are players and planeswalkers only
                    // (creatures excluded). Every live player plus every
                    // planeswalker on any battlefield. The resolve-time
                    // Player/Planeswalker gate (CR 608.2b) further validates.
                    CandidateGatherer: ctx =>
                    {
                        var candidates = new List<object>(ctx.AllPlayers);
                        candidates.AddRange(ctx.AllPlayers
                            .SelectMany(p => p.Zones.Battlefield.GetCards())
                            .OfType<Planeswalker>());
                        return candidates;
                    }),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
