using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heliod's Pilgrim (Theros Beyond Death, {1}{W}).
///
/// Creature — Human Cleric 1/2. Oracle text:
///   "When Heliod's Pilgrim enters, you may search your library for an
///    Aura card, reveal it, put it into your hand, then shuffle."
///
/// ## Implemented (v1)
/// - 1/2 Human Cleric with mana cost {1}{W}.
/// - <b>ETB tutor (CR 701.19a / CR 603.1)</b>: When Heliod's Pilgrim
///   enters the battlefield, the controller's library is searched
///   deterministically for the first Aura card; if found, it is moved
///   Library → Hand and the library is shuffled. Per CR 701.19a the
///   search is a "may" — when no Aura is found (or in any future picker
///   that declines) the effect resolves as a no-op. The single-arg
///   factory attaches the trigger to the card but does NOT register it
///   with a <see cref="TriggerManager"/>; tests exercise the ETB effect
///   by firing the trigger manually or by routing the card through
///   <see cref="Services.ZoneService"/> (which publishes the
///   <see cref="CardMovedEvent"/> the trigger consumes).
/// - <b>Aura predicate (CR 303.4)</b>: an Aura is an Enchantment with
///   subtype Aura. v1 filters via
///   <c>HasSubtype(CardSubtype.Aura)</c> — Aura is exclusively printed
///   on Enchantment cards, so the subtype check alone is sufficient
///   (matches the engine's broader subtype-only posture for
///   Artificer / Equipment family filters).
/// - <b>Shuffle (CR 701.20a)</b>: routed via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> so the post-search
///   shuffle publishes a <see cref="LibraryShuffledEvent"/> when an
///   <see cref="IEventBus"/> is registered with the library.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven tutor prompt</b>: the ETB hard-codes "first Aura
///   in library". A full implementation would prompt the controller's
///   <see cref="Players.Agents.IPlayerAgent"/> for which Aura to take,
///   including the "you may" opt-out clause (same posture as
///   Stoneforge Mystic's deferred prompt).
/// - <b>Reveal event</b>: the ETB tutor moves the card to hand without
///   emitting a <c>CardRevealedEvent</c>. Wire a reveal once the
///   reveal-event plumbing is exercised by an in-engine prompt path
///   (same gap shared with Stoneforge Mystic).
/// </summary>
[CardName("Heliod's Pilgrim")]
public static class HeliodsPilgrimFactory
{
    public const string CardName = "Heliod's Pilgrim";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Heliod's Pilgrim with no live TriggerManager wiring.
    /// The ETB trigger is attached but not registered. Suitable for unit /
    /// shape tests — fire the trigger manually via
    /// <c>card.Abilities.OfType&lt;TriggeredAbility&gt;().Single()</c>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Heliod's Pilgrim with an optional
    /// <see cref="TriggerManager"/>. When supplied, the ETB trigger is
    /// registered so a Battlefield <see cref="CardMovedEvent"/> for this
    /// card places the trigger on the stack automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Heliod's Pilgrim enters, you may search your library
        //    for an Aura card, reveal it, put it into your hand, then
        //    shuffle."
        // v1: deterministic — take the first Aura card in the library
        // (HasSubtype(Aura) — Aura is exclusively printed on Enchantment
        // cards per CR 303.4). CR 701.20a shuffle is wired via
        // LibraryShuffle (publishes LibraryShuffledEvent when the library
        // has an EventBus registered). Reveal-event emission deferred.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor an Aura to hand (CR 701.19a)",
            () =>
            {
                var controller = card.Controller ?? owner;

                var pick = controller.Zones.Library.GetCards()
                    .FirstOrDefault(c => c.HasSubtype(CardSubtype.Aura));
                if (pick == null) return; // CR 701.19a — no candidate / decline = no-op.

                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);

                // CR 701.20a — shuffle after the search resolves.
                Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "heliods-pilgrim");
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
