using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Llanowar Elves (Alpha + many reprints, {G}).
///
/// Creature — Elf Druid 1/1. Oracle text:
///   "{T}: Add {G}."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Druid, mana cost {G}, owner / controller wired.
/// - <b>Single mana ability (CR 605.1)</b>: {T}: Add {G}. Implemented as a
///   <see cref="ManaAbility"/> with a <c>canActivateCheck = !IsTapped</c>
///   gate — mirrors the per-colour mana abilities on
///   <see cref="NobleHierarchFactory"/>. <see cref="ManaAbility.Activate"/>
///   taps the creature via the <see cref="Permanent.Tap"/> path.
///
/// ## Notes
/// - Llanowar Elves does NOT have summoning sickness baked in here — that's
///   the engine's job (CR 302.1). The mana ability is structurally always
///   available; the engine applies summoning sickness at activation time
///   per CR 302.1 / 605.3a (mana abilities are NOT exempt — they're
///   activated abilities for summoning-sickness purposes).
/// - Functional reprints (Llanowar Elf, Elvish Mystic, Fyndhorn Elves,
///   Gilded Goose's predecessors, etc.) are NOT wired here — each requires
///   its own [CardName] dispatch entry.
/// </summary>
[CardName("Llanowar Elves")]
public static class LlanowarElvesFactory
{
    public const string CardName = "Llanowar Elves";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Llanowar Elves owned and controlled by
    /// <paramref name="owner"/>. The single {T}: Add {G} mana ability is
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

        // CR 605.1 — {T}: Add {G}. Mana ability (no stack). Tap the elf
        // when activated; gate on !IsTapped so duplicate activations are
        // prevented. Mirrors Noble Hierarch's per-colour mana abilities.
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
