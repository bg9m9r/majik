using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Boggart Trawler // Boggart Bog (Bloomburrow, {2}{B}).
///
/// Creature — Goblin 3/1. Oracle text (front):
///   "When this creature enters, exile target player's graveyard."
///
/// Back face — <see cref="BoggartBogFactory"/> (Land — {T}: Add {B};
/// "As this land enters, you may pay 3 life. If you don't, it enters
/// tapped.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Modal Double-Faced Card: each face has its own complete characteristics.
/// At cast / play time the controller chooses which face to use. Modelled
/// by giving each printed face its own <c>[CardName]</c>-dispatched factory:
/// <list type="bullet">
///   <item>Casting the front face → <see cref="NamedCardFactory"/>
///     resolves <c>"Boggart Trawler"</c> → this factory → a
///     <see cref="Creature"/> with the ETB exile effect.</item>
///   <item>Playing the back face → <see cref="NamedCardFactory"/>
///     resolves <c>"Boggart Bog"</c> →
///     <see cref="BoggartBogFactory"/> → a <see cref="Land"/> with
///     the painland-style ETB + {T}: Add {B}.</item>
/// </list>
/// Both face cards carry an <see cref="MdfcState"/> tracker so callers
/// (hand UI / bot policy / serialisation) can see the printed back-face
/// name without holding two object handles.
///
/// ## Implemented (v1)
/// - Creature {2}{B} 3/1, Goblin subtype. Black (from the {B} pip per
///   CR 202.2c). Owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Boggart Trawler",
///   back = "Boggart Bog") so the back-face name is observable from
///   the front-face card object.
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b> —
///   "When this creature enters, exile target player's graveyard."
///   Single 1..1 "target player" <see cref="TargetRequest"/>.
///   On resolution reads <c>ChosenTargets[0][0]</c>, snapshots that
///   player's graveyard, and moves every card to that player's
///   <see cref="ZoneType.Exile"/>. CR 608.2b — empty graveyard is a
///   clean no-op. Falls back to the controller when no target was set
///   (v1 deterministic path — mirrors
///   <see cref="BojukaBogFactory"/> / <see cref="TormodsCryptFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Target player agent prompt</b>: v1 reads ChosenTargets[0][0] and
///   falls back to the controller. Full agent-prompt targeting deferred.
/// </summary>
[CardName("Boggart Trawler")]
public static class BoggartTrawlerFactory
{
    public const string CardName = "Boggart Trawler";
    public const string BackName = "Boggart Bog";
    public const string PrintedManaCost = "{2}{B}";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Boggart Trawler with no live wiring. The ETB trigger is
    /// attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Boggart Trawler with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s
    /// published on the bus route it to the stack.
    /// </summary>
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
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Boggart Bog) is observable from the front-face
        // card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, exile target player's graveyard."
        // Single 1..1 "target player" TargetRequest; on resolution
        // snapshots the chosen player's graveyard (CR 608.2b — empty
        // graveyard is a clean no-op) and moves each card to that
        // player's Exile zone. Mirrors BojukaBog's ETB exile shape.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var exileEffect = new Effect(
            $"{CardName}: exile target player's graveyard (when this creature enters)",
            () =>
            {
                if (etbTrigger == null) return;

                // Resolve target player from ChosenTargets; fall back to
                // the controller (v1 deterministic path mirrors
                // Tormod's Crypt / Nihil Spellbomb / Bojuka Bog).
                Player targetPlayer;
                if (etbTrigger.ChosenTargets.Count > 0
                    && etbTrigger.ChosenTargets[0].Count > 0
                    && etbTrigger.ChosenTargets[0][0] is Player chosenPlayer)
                {
                    targetPlayer = chosenPlayer;
                }
                else
                {
                    targetPlayer = owner;
                }

                // Snapshot before mutating — CR 608.2b empty-graveyard
                // case is a clean no-op (the loop body simply doesn't
                // execute).
                var graveyardCards = targetPlayer.Zones.Graveyard.GetCards().ToList();
                foreach (var c in graveyardCards)
                {
                    targetPlayer.Zones.Graveyard.RemoveCard(c);
                    targetPlayer.Zones.Exile.AddCard(c);
                    c.SetZone(ZoneType.Exile);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
