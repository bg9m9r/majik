using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seekers' Squire (Ixalan, {1}{B}).
/// Creature — Human Scout 1/2.
///
/// Oracle text (verified against Scryfall):
///   "When this creature enters, it explores. (Reveal the top card of your
///    library. Put that card into your hand if it's a land. Otherwise, put a
///    +1/+1 counter on this creature, then put the card back or put it into
///    your graveyard.)"
///
/// ## Implemented (v1)
/// - 1/2 Human Scout, mana cost {1}{B}, owner / controller wired.
/// - <b>ETB explore</b> (CR 603.6a + CR 701.40): an unconditional self-ETB
///   triggered ability via <see cref="Triggers.OnEnterBattlefieldSelf"/>. On
///   resolution the controller explores (CR 701.40) — the exploring permanent
///   is Seekers' Squire itself, so the +1/+1 counter (non-land branch) lands
///   on this creature. Resolution runs the shared
///   <see cref="ExploreAction.ExploreAsync"/> pipeline, consulting the
///   registered <see cref="IPlayerAgent"/> for the keep-on-top / graveyard
///   choice (CR 701.40c). The controller closure re-resolves at execute time
///   so blink / control-change scenarios explore for the correct player.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached but not
///   registered with a <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the ETB trigger
///   so the relevant <c>CardMovedEvent</c> stacks it (CR 603.3).
/// </summary>
[CardName("Seekers' Squire")]
public static class SeekersSquireFactory
{
    public const string CardName = "Seekers' Squire";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 2;

    public static Creature Create(Player owner) => Create(owner, triggers: null);

    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        ExploreEtb.Attach(card, owner, triggers);

        return card;
    }
}
