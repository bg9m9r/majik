using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thalia's Lancers (Shadows over Innistrad, {3}{W}{W}).
///
/// Creature — Human Knight 4/4. Oracle text:
///   "First strike."
///   "When Thalia's Lancers enters, you may search your library for a
///    legendary card, reveal it, put it into your hand, then shuffle."
///
/// ## Implementation
///
/// - 4/4 Human Knight at {3}{W}{W}. Both <see cref="CardSubtype.Human"/>
///   and <see cref="CardSubtype.Knight"/> assigned.
/// - <b>First strike</b> — wired via <see cref="KeywordAbility"/>("First
///   strike"); the combat damage sequencer reads the marker through
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>
///   (same posture as <see cref="BorosReckonerFactory"/>).
/// - <b>ETB tutor (CR 603.6a / 701.19a)</b>: "When Thalia's Lancers
///   enters, you may search your library for a legendary card, reveal
///   it, put it into your hand, then shuffle." Trigger condition uses
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; resolution walks the
///   controller's library, deterministically picks the first card with
///   <see cref="CardSupertype.Legendary"/> (v1 picker — same posture as
///   <see cref="StoneforgeMysticFactory"/>'s Equipment tutor), moves it
///   Library → Hand, then shuffles via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a). The "may"
///   clause + no-candidate path both resolve as a no-op with the shuffle
///   still firing (CR 701.19a — declined / no eligible candidate is
///   legal; shuffle runs because a search occurred).
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> path attaches the trigger
/// for shape tests without <see cref="TriggerManager"/> registration.
/// Use the (owner, triggers) overload for bus-driven, fully-wired
/// behaviour where a <see cref="CardMovedEvent"/> to the battlefield
/// automatically queues the ability.
///
/// ## Deferred
///
/// - <b>Agent-driven library pick</b> — the v1 deterministic picker
///   takes the first legendary card in library order. A full
///   implementation would prompt the controller via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> with the
///   "you may" decline branch (CR 701.19a).
/// - <b>Reveal event emission</b> — the tutor moves the card to hand
///   without publishing a CardRevealedEvent. Wire a reveal when the
///   reveal-event plumbing is exercised by an in-engine prompt path
///   (same gap as <see cref="StoneforgeMysticFactory"/>).
/// </summary>
[CardName("Thalia's Lancers")]
public static class ThaliasLancersFactory
{
    public const string CardName = "Thalia's Lancers";
    public const string Cost = "{3}{W}{W}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Thalia's Lancers with no live <see cref="TriggerManager"/>
    /// wiring. The ETB tutor trigger is attached to the card shape for
    /// structural / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Thalia's Lancers with optional trigger manager. When
    /// <paramref name="triggers"/> is supplied, the ETB tutor trigger is
    /// registered so a qualifying <see cref="CardMovedEvent"/> to the
    /// battlefield automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // First strike — CR 702.7. Combat sequencer reads the marker via
        // CombatAbilities.HasFirstStrike. Same wiring as Boros Reckoner.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("First strike", source: card, controller: owner));

        // ----------------------------------------------------------------
        // ETB tutor — CR 603.6a / 701.19a.
        //   "When Thalia's Lancers enters, you may search your library for
        //    a legendary card, reveal it, put it into your hand, then
        //    shuffle."
        // v1: deterministic picker (first legendary card in library
        // order). The "may" / no-candidate paths both resolve cleanly as
        // a no-op + shuffle (CR 701.19a — declined / no legal candidate
        // is itself a legal outcome; shuffle still runs because a search
        // occurred — CR 701.20a).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor a legendary card to hand",
            () =>
            {
                var pick = owner.Zones.Library.GetCards()
                    .FirstOrDefault(c => c.HasSupertype(CardSupertype.Legendary));

                if (pick != null)
                {
                    owner.Zones.Library.RemoveCard(pick);
                    owner.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }
                // CR 701.20a — shuffle after the search resolves, even on
                // the decline / no-candidate path.
                LibraryShuffle.ShuffleLibrary(owner, "thalias-lancers");
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
