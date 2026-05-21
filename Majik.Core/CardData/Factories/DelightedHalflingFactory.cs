using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Delighted Halfling (The Lord of the Rings: Tales of
/// Middle-earth).
///
/// Legendary Creature — Halfling Citizen 1/2.
/// Oracle text:
///   "{T}: Add one mana of any color. Spend this mana only to cast a legendary
///    spell. That spell can't be countered."
///
/// ## Implemented (v1)
/// - Legendary Creature — Halfling Citizen 1/2.
/// - {T}: Add one mana of any colour — implemented as five <see cref="ManaAbility"/>
///   instances (one per WUBRG), mirroring the Treasure token pattern in
///   <see cref="Majik.Core.Tokens.TokenFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Usage restriction</b>: "Spend this mana only to cast a legendary spell."
///   Enforcement requires per-mana-pool entry tagging and a spend-restriction check
///   in the cast-payment flow. Not yet retrofitted.
/// - <b>Can't-be-countered rider</b>: "That spell can't be countered." Requires
///   flagging the spell object at cast time and gating counter-spells in
///   <see cref="Majik.Core.Services.StackResolver"/>. Deferred.
/// </summary>
public static class DelightedHalflingFactory
{
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var halfling = new Creature(
            "Delighted Halfling",
            manaCost: "{G}",
            power: 1, toughness: 2,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Halfling, CardSubtype.Citizen });

        halfling.SetOwner(owner);
        halfling.SetController(owner);

        // {T}: Add one mana of any color (CR 605).
        // Implemented as 5 ManaAbility instances so the mana picker can satisfy
        // any single colour pip using this creature — mirrors Treasure token
        // pattern (TokenFactory.CreateTreasure).
        // Usage restriction (legendary-only) deferred — see class xmldoc.
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            halfling.AddAbility(new ManaAbility(
                halfling, owner, ManaCost.Parse(color)));
        }

        return halfling;
    }
}
