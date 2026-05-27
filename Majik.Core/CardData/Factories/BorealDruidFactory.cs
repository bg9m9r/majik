using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boreal Druid (Coldsnap, {G}).
///
/// Creature — Elf Druid 1/1. Oracle text:
///   "{T}: Add {C}."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Druid, mana cost {G}, owner / controller wired.
/// - <b>Single mana ability (CR 605.1)</b>: {T}: Add {C}. Implemented as a
///   <see cref="ManaAbility"/> with a <c>canActivateCheck = !IsTapped</c>
///   gate — mirrors the single colourless mana ability on
///   <see cref="PlagueMyrFactory"/>.
///
/// ## Notes
/// - {C} (CR 107.4c) is bucketed as +1 generic in
///   <see cref="ValueObjects.ManaCost.Parse"/> today (same convention used
///   by Inkmoth Nexus' {T}: Add {C} and Plague Myr's {T}: Add {C}). Pays
///   generic costs identically; snow-aware accounting deferred.
/// - Summoning sickness (CR 302.1 / 605.3a) is the engine's job — this
///   factory does not encode it.
/// </summary>
[CardName("Boreal Druid")]
public static class BorealDruidFactory
{
    public const string CardName = "Boreal Druid";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Boreal Druid owned and controlled by
    /// <paramref name="owner"/>. The single {T}: Add {C} mana ability is
    /// attached structurally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 605.1 — {T}: Add {C}. Mana ability (no stack). Tap the druid
        // when activated; gate on !IsTapped to prevent duplicate
        // activations. {C} is bucketed as +1 generic in ManaCost.Parse
        // today (same convention used by Plague Myr / Inkmoth Nexus).
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{C}"),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
