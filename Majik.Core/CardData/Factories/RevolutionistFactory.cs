using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Revolutionist (Shadows over Innistrad, {5}{R}).
///
/// Creature — Human Wizard 3/3. Oracle text (verified against Scryfall
/// 2026-06-16):
///   "When this creature enters, return target instant or sorcery card from
///    your graveyard to your hand.
///    Madness {3}{R} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Implemented (v1)
///
/// - <b>3/3 Creature — Human Wizard at {5}{R}.</b>
///
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>:
///   "When this creature enters, return target instant or sorcery card from
///    your graveyard to your hand." Fires on self-ETB via
///   <see cref="EventTriggerCondition{T}"/> over <see cref="CardMovedEvent"/>
///   (the exact shape <see cref="PinnacleMonkFactory"/> uses). Declares a 1..1
///   <see cref="TargetRequest"/> for an instant or sorcery card in the
///   controller's graveyard, with a live
///   <see cref="TargetRequest.CandidateGatherer"/> that enumerates the
///   controller's graveyard restricted to instant / sorcery cards (the new
///   instant-or-sorcery graveyard candidate predicate this factory wires onto
///   the existing gatherer system — sibling of
///   <see cref="DowsingShamanFactory"/>'s enchantment-card gatherer). On
///   resolution it moves the chosen card to the controller's hand via
///   <see cref="Fx.ReturnFromGraveyardToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>.
///   CR 608.2b — if the target is no longer a legal instant/sorcery in the
///   controller's graveyard at resolution, the effect no-ops.
///
/// ## Madness (NOT wired here — intrinsic)
/// Madness {3}{R} works intrinsically for every catalogued card (CR 702.35)
/// via <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the
/// central discard funnel <see cref="Fx.DiscardCard"/>; "Revolutionist" is
/// catalogued at {3}{R}, so the madness line needs no factory code.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape. The ETB trigger is attached to
///   the card; not registered with any <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — the source-generated
///   triggers-aware dispatch path; additionally registers the ETB trigger with
///   the supplied <see cref="TriggerManager"/> so it fires on self-ETB.
///
/// ## Rules citations
/// - CR 603.1 / CR 603.6a — Triggered ability; battlefield-active.
/// - CR 608.2b — illegal-on-resolution: target must still be a legal
///   instant/sorcery in the controller's graveyard.
/// - CR 701.11 — Return to hand.
/// - CR 702.35 — Madness (intrinsic via MadnessCatalog).
/// </summary>
[CardName("Revolutionist")]
public static class RevolutionistFactory
{
    public const string CardName = "Revolutionist";
    public const string PrintedManaCost = "{5}{R}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Revolutionist with no live trigger registration. The ETB
    /// graveyard-recur trigger is attached to the card shape. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Revolutionist. When <paramref name="triggers"/> is supplied
    /// (the source-generated triggers-aware dispatch path) the ETB
    /// graveyard-recur trigger is registered so it fires on self-ETB.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, return target instant or sorcery
        //    card from your graveyard to your hand."
        // Self-ETB via EventTriggerCondition<CardMovedEvent> (Pinnacle Monk /
        // Snapcaster Mage condition shape). Declares a 1..1 TargetRequest with
        // a live CandidateGatherer scoped to the controller's graveyard,
        // restricted to instant / sorcery cards. CR 608.2b applied at
        // resolution: the target must still be an instant or sorcery in the
        // controller's graveyard.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: return target instant or sorcery card from your graveyard to your hand (ETB)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Card target) return;

                // CR 608.2b — illegal-on-resolution check. The target must
                // still be (a) in the controller's graveyard and (b) an
                // instant or sorcery card.
                if (target.Zone != ZoneType.Graveyard) return;
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(target.Owner, controller)) return;
                if (!IsInstantOrSorcery(target)) return;

                // CR 701.11 — return to hand.
                Fx.ReturnFromGraveyardToHand(target);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>(),
                    Intent: BotIntent.Draw,
                    // Live instant-or-sorcery graveyard candidate pool (CR 110.4 —
                    // a card in a graveyard). Scoped to the controller's
                    // graveyard ("your graveyard" — CR 109.5 / 110.4).
                    CandidateGatherer: _ => GraveyardInstantOrSorceryCards(card.Controller ?? owner)),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Instant / sorcery card type predicate (CR 205.2a). The
    /// instant-or-sorcery graveyard target filter at the heart of this card.
    /// </summary>
    private static bool IsInstantOrSorcery(ICard card) =>
        card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery);

    /// <summary>
    /// Candidate pool for the ETB recursion target — instant / sorcery CARDS
    /// in the controller's graveyard (CR 110.4 — a card in a graveyard, not a
    /// permanent).
    /// </summary>
    private static IReadOnlyList<object> GraveyardInstantOrSorceryCards(Player controller) =>
        controller.Zones.Graveyard.GetCards()
            .Where(IsInstantOrSorcery)
            .Cast<object>()
            .ToList();
}
