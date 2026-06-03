using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spyglass Siren (The Lost Caverns of Ixalan, {U}).
/// Creature — Siren Pirate 1/1.
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, create a Map token. (It's an artifact with
///    "{1}, {T}, Sacrifice this token: Target creature you control explores.
///    Activate only as a sorcery.")"
///
/// ## Implemented (v1)
/// - 1/1 Siren Pirate, mana cost {U} (blue), owner / controller wired.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker.
/// - <b>ETB create a Map</b> (CR 603.6a + CR 111.10): an unconditional
///   self-ETB triggered ability via <see cref="Triggers.OnEnterBattlefieldSelf"/>
///   that mints one Map token for the controller via
///   <see cref="TokenFactory.CreateMap"/> (the Map carries its own
///   sorcery-speed "{1}, {T}, Sacrifice this token: Target creature you control
///   explores" ability — CR 701.40). Controller is resolved live at execute time
///   so blink / control-change mints the Map for the right player.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached but not
///   registered with a <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the ETB trigger.
/// </summary>
[CardName("Spyglass Siren")]
public static class SpyglassSirenFactory
{
    public const string CardName = "Spyglass Siren";
    public const string PrintedManaCost = "{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    public static Creature Create(Player owner) => Create(owner, triggers: null);

    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Siren, CardSubtype.Pirate });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 603.6a + CR 111.10 — "When this creature enters, create a Map token."
        var etbEffect = new Effect(
            $"{CardName}: create a Map token (when this creature enters)",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateMap(controller);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
