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
/// Named-card factory for Spellseeker (Battlebond, {2}{U}).
///
/// Creature — Human Wizard 1/1. Oracle text:
///   "When Spellseeker enters, you may search your library for an instant
///    or sorcery card with mana value 2 or less, reveal it, put it into
///    your hand, then shuffle."
///
/// ## Implemented (v1)
/// - 1/1 Human Wizard with mana cost {1}{U}.
/// - <b>ETB tutor (CR 603.1 / CR 701.19a)</b>: when Spellseeker enters the
///   battlefield, the controller's library is scanned for instant or
///   sorcery cards whose <see cref="ValueObjects.ManaCost.TotalValue"/>
///   is ≤ 2. The agent's <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///   selects a pick (deterministic first-match fallback when no agent is
///   registered); a null pick is a legal decline (CR 701.19a — "you may"
///   permits zero).
/// - When a pick is selected the chosen card is moved Library → Hand and
///   the library is shuffled via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a). Shape mirrors <see cref="TrinketMageFactory"/>
///   (mv-gated artifact tutor) and <see cref="StoneforgeMysticFactory"/>
///   (Equipment tutor).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the picked card moves Library → Hand without
///   emitting a CardRevealedEvent; same gap as the other tutor factories.
/// </summary>
[CardName("Spellseeker")]
public static class SpellseekerFactory
{
    public const string CardName = "Spellseeker";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Spellseeker with no live TriggerManager wiring (the
    /// shape/dispatcher path). The ETB trigger is attached but not
    /// registered — suitable for unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Spellseeker with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    /// to the battlefield places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = eventBus; // reserved for parity / future reveal-event wiring.

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Spellseeker enters, you may search your library for an
        //    instant or sorcery card with mana value 2 or less, reveal it,
        //    put it into your hand, then shuffle."
        // Predicate: (HasType(Instant) OR HasType(Sorcery)) AND mv ≤ 2.
        // Agent picker mirrors MysticalTutorFactory; deterministic
        // first-match fallback when no agent is registered.
        // CR 701.20a shuffle wired via LibraryShuffle.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor an instant/sorcery (mv ≤ 2) to hand",
            () =>
            {
                var controller = card.Controller ?? owner;

                var candidates = controller.Zones.Library.GetCards()
                    .OfType<Card>()
                    .Where(c => (c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
                        && c.ManaCostValue.TotalValue <= 2)
                    .Cast<ICard>()
                    .ToList();
                if (candidates.Count == 0) return; // CR 701.19a — empty = no-op.

                var agent = AgentRegistry.Get(controller);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates,
                        "instant or sorcery card with mana value 2 or less")
                        .GetAwaiter().GetResult()
                    : candidates[0];
                if (pick == null) return; // CR 701.19a — caster declined.

                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.20a — shuffle after the search resolves.
                LibraryShuffle.ShuffleLibrary(controller, "spellseeker");
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
