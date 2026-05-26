using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ravenous Chupacabra (Rivals of Ixalan, {2}{B}{B}).
///
/// Creature — Beast Horror 2/2. Oracle text:
///   "When Ravenous Chupacabra enters, destroy target creature an opponent
///    controls."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Beast Horror, mana cost {2}{B}{B}, owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b> — wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; same trigger shape as
///   <see cref="ReclamationSageFactory"/> and <see cref="EternalWitnessFactory"/>.
///   - Mandatory 1..1 <see cref="TargetRequest"/> for "target creature an
///     opponent controls" — NOT a "may" ability (printed oracle has no
///     optional clause). <see cref="TargetRequest.CandidateGatherer"/>
///     enumerates every creature controlled by a player OTHER than the
///     Chupacabra's controller (CR 109.1 — opponent = any other player in
///     the game), tagged with <see cref="BotIntent.Removal"/> so the bot's
///     ranker picks the highest-value opposing creature.
///   - On resolution: validates the chosen card is still a Creature on the
///     Battlefield (CR 608.2b — illegal target → clean no-op) AND is still
///     controlled by an opponent of the Chupacabra's controller. If valid:
///     destroy via <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///     <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///     CR 702.12 cancels; regeneration CR 701.15 shield is consumed).
///   - Empty-candidate / no-target path is a clean no-op (CR 608.2b).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached. No
///   TriggerManager wiring; suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, IEventBus, TriggerManager)"/> — full wiring:
///   ETB registered for automatic firing on Chupacabra's own
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> into
///   <see cref="ZoneType.Battlefield"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: validator does not yet
///   enforce "creature an opponent controls" at trigger-resolution
///   declaration — resolution-time guard handles illegal targets
///   (CR 608.2b). Same posture as <see cref="ReclamationSageFactory"/> /
///   <see cref="CausticCaterpillarFactory"/>.
/// - <b>Single-target only — mandatory pick</b>: if no opposing creatures
///   exist, the ETB still triggers (the printed ability has no "if able"
///   clause) and falls through to a no-op at resolution. Per CR 603.6c the
///   trigger still goes on the stack; v1 collapses that to a clean no-op
///   without target prompt — matches existing factory posture.
/// </summary>
[CardName("Ravenous Chupacabra")]
public static class RavenousChupacabraFactory
{
    public const string CardName = "Ravenous Chupacabra";
    public const string PrintedManaCost = "{2}{B}{B}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Ravenous Chupacabra with the ETB trigger attached for shape
    /// inspection. Trigger is NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Ravenous Chupacabra with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus — reserved for parity with other
    /// ETB factories. May be null.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger remains attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Beast, CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a):
        //   "When Ravenous Chupacabra enters, destroy target creature an
        //    opponent controls."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy target creature an opponent controls",
            () =>
            {
                if (etbTrigger == null) return;
                if (etbTrigger.ChosenTargets.Count == 0
                    || etbTrigger.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (etbTrigger.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality check.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 109.1 — "opponent" = any player other than Chupacabra's
                // controller. Re-check at resolution time in case control of
                // either side changed between trigger and resolve.
                var myController = card.Controller ?? owner;
                if (ReferenceEquals(target.Controller, myController)) return;

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // regeneration (CR 701.15) shield is consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
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
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
