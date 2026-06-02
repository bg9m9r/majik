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
/// Named-card factory for Kessig Malcontents (Innistrad: Midnight Hunt,
/// {2}{R}). Creature — Human Warrior 3/1. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, it deals damage to target player or
///    planeswalker equal to the number of Humans you control."
///
/// The card's base shape (name, Creature, Human / Warrior subtypes,
/// {2}{R}, 3/1) is materialised from the embedded JSON definition
/// (<c>kessig-malcontents.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB triggered ability is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express targeted ETB damage triggers, let alone a count-derived amount
/// (same posture as <see cref="ViashinoPyromancerFactory"/>, whose fixed
/// "deal 2 to target player or planeswalker" ETB this mirrors).
///
/// ## Implemented (v1)
///
/// - 3/1 Human Warrior at printed cost {2}{R}, owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature enters,
///   it deals damage to target player or planeswalker equal to the number
///   of Humans you control." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a single 1..1
///   <see cref="TargetRequest"/> ("target player or planeswalker"). The
///   request's <see cref="TargetRequest.CandidateGatherer"/> surfaces every
///   live <see cref="Player"/> plus every <see cref="Planeswalker"/> on any
///   battlefield (CR 115.1 — creature targets are excluded), so the agent
///   only sees legal picks. On resolution the effect reads the trigger's
///   <see cref="TriggeredAbility.ChosenTargets"/>, gates to a
///   <see cref="Player"/> / <see cref="Planeswalker"/> (CR 608.2b), counts
///   the controller's Humans, and routes through
///   <see cref="Fx.DealDamageAny(object, int)"/> so a planeswalker target
///   converts to loyalty removal (CR 306.8).
/// - <b>"Humans you control" count</b>: counted at resolution (CR 608.2g —
///   the amount is determined as the ability resolves, not when it was put
///   on the stack). Kessig Malcontents itself is a Human (CR 205.3) and, by
///   the time the ETB ability resolves, is on the battlefield under its
///   controller, so it counts itself — a lone Kessig Malcontents deals 1
///   (same self-counting posture as Champion of the Parish /
///   <see cref="ThaliaLieutenantFactory"/>'s "each Human you control").
///
/// ## Deferred (v1 gaps)
///
/// - <b>Damage source threading</b>: <see cref="Fx.DealDamageAny"/> is a
///   target-side helper that doesn't yet thread Kessig Malcontents through
///   as the damage source, so a future lifelink / "whenever a source you
///   control deals damage" grant won't observe it. Same primitive-level
///   posture as <see cref="ViashinoPyromancerFactory"/> /
///   <see cref="VoldarenEpicureFactory"/>.
/// </summary>
[CardName("Kessig Malcontents")]
public static class KessigMalcontentsFactory
{
    public const string CardName = "Kessig Malcontents";
    public const string Slug = "kessig-malcontents";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>
    /// Count the Humans <paramref name="controller"/> controls on their
    /// battlefield (CR 205.3 — a permanent is a "Human" if it has the Human
    /// creature type). Counted at resolution per CR 608.2g.
    /// </summary>
    private static int CountHumansControlled(Player controller) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Human));

    /// <summary>
    /// Construct Kessig Malcontents owned and controlled by
    /// <paramref name="owner"/>. The base shape comes from the embedded JSON
    /// definition; the ETB "deal damage to target player or planeswalker
    /// equal to the number of Humans you control" trigger is attached with a
    /// 1..1 "target player or planeswalker" <see cref="TargetRequest"/>. The
    /// trigger is attached structurally and is fully self-contained — no
    /// service wiring required. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human / Warrior subtypes, {2}{R}, 3/1). The JSON carries no
        // abilities — the ETB trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 603.6a — ETB triggered ability with target.
        //   "When this creature enters, it deals damage to target player or
        //    planeswalker equal to the number of Humans you control."
        // Same shape as ViashinoPyromancerFactory's ETB-with-target trigger:
        // declare a 1..1 TargetRequest, read the chosen target out of
        // ChosenTargets at resolution, and apply the effect. The amount is
        // the controller's live Human count, computed at resolution time
        // (CR 608.2g). Damage routes through Fx.DealDamageAny so a
        // Planeswalker target converts to loyalty removal (CR 306.8).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: deal damage to target player or planeswalker equal to the number of Humans you control",
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
                    // CR 608.2g — count Humans you control as the ability
                    // resolves. Kessig Malcontents is itself a Human and is
                    // on the battlefield by now, so it is included.
                    var amount = CountHumansControlled(card.Controller ?? owner);
                    if (amount > 0)
                    {
                        Fx.DealDamageAny(target, amount);
                    }
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
