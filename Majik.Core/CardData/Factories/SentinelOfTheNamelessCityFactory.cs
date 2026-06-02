using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sentinel of the Nameless City
/// (The Lost Caverns of Ixalan, {2}{G}).
/// Creature — Merfolk Warrior Scout 3/4.
///
/// Oracle text (verified against Scryfall):
///   "Vigilance
///    Whenever this creature enters or attacks, create a Map token. (It's an
///    artifact with "{1}, {T}, Sacrifice this token: Target creature you
///    control explores. Activate only as a sorcery.")"
///
/// ## Implemented (v1)
/// - 3/4 Merfolk Warrior Scout, mana cost {2}{G}, owner / controller wired.
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/> marker.
/// - <b>"Whenever this creature enters or attacks, create a Map token"</b>
///   (CR 603.6a / CR 508.1f + CR 111.10): modelled as TWO self-scoped
///   triggered abilities — an ETB trigger
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>) and an attack trigger
///   (<see cref="Triggers.OnAttackSelf"/>) — each minting one Map token for the
///   controller via <see cref="TokenFactory.CreateMap"/> (the Map carries its
///   own sorcery-speed targeted-explore ability, CR 701.40). Splitting the
///   "enters or attacks" clause into two triggers matches the AvatarRoku /
///   the rest of the "enters or attacks" family. Controller is resolved live
///   at execute time.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only; triggers attached but not
///   registered with a <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers both triggers.
/// </summary>
[CardName("Sentinel of the Nameless City")]
public static class SentinelOfTheNamelessCityFactory
{
    public const string CardName = "Sentinel of the Nameless City";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 3;
    public const int Toughness = 4;

    public static Creature Create(Player owner) => Create(owner, triggers: null);

    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Warrior, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 111.10 — the shared "create a Map token" effect body (resolves the
        // controller live so blink / control-change mints for the right player).
        IEffect MakeMapEffect(string when) => new Effect(
            $"{CardName}: create a Map token ({when})",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateMap(controller);
            });

        // CR 603.6a — "Whenever this creature enters … create a Map token."
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { MakeMapEffect("enters") },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // CR 508.1f — "Whenever this creature … attacks, create a Map token."
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { MakeMapEffect("attacks") },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
