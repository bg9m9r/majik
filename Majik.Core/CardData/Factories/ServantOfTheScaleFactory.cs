using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Servant of the Scale (Aether Revolt, {G}).
///
/// Creature — Human Soldier 0/0. Oracle text (Scryfall, verified):
///   "This creature enters with a +1/+1 counter on it.
///    When this creature dies, put X +1/+1 counters on target creature you
///    control, where X is the number of +1/+1 counters on this creature."
///
/// ## Shape source
///
/// Card identity (name, {G}, 0/0, Creature — Human Soldier, green) is loaded
/// from <c>Majik.Core/CardData/Cards/servant-of-the-scale.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same data-driven identity pattern as
/// <see cref="NarnamRenegadeFactory"/>. The enters-with-counter replacement and
/// the dies-move-counters trigger are wired in code below.
///
/// ## Implemented (v1)
/// - {G} 0/0 Creature — Human Soldier, green, owner / controller stamped.
///   Printed 0/0; with its mandatory ETB +1/+1 counter it is a 1/1 on the
///   battlefield.
/// - <b>Enters-with-counter (CR 614.1d / CR 122.1g)</b> — wired through the
///   reusable <see cref="EntersWithCountersReplacement"/> with a FIXED amount
///   of one (no condition — same shape as <see cref="NarnamRenegadeFactory"/>'s
///   revolt replacement but unconditional, since Servant always enters with the
///   counter). Registered only when a <see cref="ReplacementBus"/> is supplied
///   so <see cref="Services.ZoneService"/> places the counter on landing and
///   Hardened Scales / Doubling Season can rewrite the amount (CR 614).
/// - <b>Dies trigger (CR 603.6c / CR 700.4)</b> — when Servant dies, put X
///   +1/+1 counters on one target creature you control, where X is the number
///   of +1/+1 counters on Servant. The dies condition + activeZones posture
///   mirrors <see cref="QuirionBeastcallerFactory"/> (Battlefield + Graveyard
///   so the trigger still matches after the ZoneService stamps the card into
///   the graveyard, CR 603.6d "looks back"). X is read off the dying card's
///   <see cref="Card.Counters"/> bag at resolution time (last-known-information,
///   CR 608.2g — the counters persist on the card object until the next cleanup
///   step per CR 514.2). Counters are placed via <see cref="Fx.PlaceCounter"/>.
///
/// ## Targeting
/// Single MANDATORY target creature the controller controls (MinTargets = 1,
/// MaxTargets = 1). Candidate pool is gathered from the controller's
/// battlefield only — "target creature you control" (CR 115.4 / CR 109.5).
/// </summary>
[CardName("Servant of the Scale")]
public static class ServantOfTheScaleFactory
{
    public const string CardName = "Servant of the Scale";

    /// <summary>One +1/+1 counter on ETB (unconditional, CR 122.1g).</summary>
    public const int EntersWithCounterAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("servant-of-the-scale");

    /// <summary>
    /// Construct Servant of the Scale with card identity + dies trigger only —
    /// no enters-with-counter replacement registered and no TriggerManager
    /// wiring. The dies trigger is attached structurally for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Servant of the Scale with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager. When supplied the dies trigger
    /// registers so a qualifying Battlefield → Graveyard move automatically
    /// queues the ability (CR 603.2).</param>
    /// <param name="replacements">ReplacementBus. When supplied an
    /// <see cref="EntersWithCountersReplacement"/> is registered so Servant
    /// enters with one +1/+1 counter (CR 614.1d).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 614.1d / CR 122.1g — "This creature enters with a +1/+1 counter on
        // it." Unconditional; wired only when a replacement bus is supplied so
        // ZoneService places the counter on landing (Hardened Scales / Doubling
        // Season rewrites apply, CR 614). Same reusable replacement as
        // Narnam Renegade, minus the revolt gate.
        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(
                new EntersWithCountersReplacement(card, EntersWithCounterAmount));
        }

        // ----------------------------------------------------------------
        // Dies trigger (CR 603.6c / CR 700.4):
        //   "When this creature dies, put X +1/+1 counters on target creature
        //    you control, where X is the number of +1/+1 counters on this
        //    creature."
        //
        // X is read off the dying card at resolution time (last-known-
        // information per CR 608.2g — Servant has already moved to the
        // graveyard but counters persist on the card object until the next
        // cleanup step, CR 514.2). All X counters go on the single chosen
        // target creature the controller controls.
        // ----------------------------------------------------------------
        TriggeredAbility? diesTrigger = null;

        var diesEffect = new Effect(
            $"{CardName}: put X +1/+1 counters on target creature you control",
            () =>
            {
                if (diesTrigger == null) return;
                if (diesTrigger.ChosenTargets.Count == 0
                    || diesTrigger.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (diesTrigger.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality: the target must still be
                // a creature on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 608.2g — last-known-information count off the dying card.
                var x = card.Counters.Count(CounterType.PlusOnePlusOne);
                if (x <= 0) return;

                Fx.PlaceCounter(target, CounterType.PlusOnePlusOne, x);
            });

        var controller = owner;
        diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            interveningIf: null,
            // ActiveZones = {Battlefield, Graveyard} — Quirion / Wurmcoil
            // posture so the trigger still matches after the ZoneService stamp.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    // Mandatory single target (CR 601.2c — "target creature you
                    // control", not "up to" / "any number of").
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    // "you control" — restrict candidates to the controller's
                    // battlefield (CR 115.4 / CR 109.5).
                    CandidateGatherer: ctx =>
                    {
                        var owningController = card.Controller ?? controller;
                        return owningController.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .Cast<object>()
                            .ToList();
                    }),
            });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
