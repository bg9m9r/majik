using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Llanowar Tribe (Mercadian Masques, {G}{G}{G}).
///
/// Creature — Elf Druid 3/3. Oracle text:
///   "{T}: Add {G}{G}{G}."
///
/// ## Implemented (v1)
/// - 3/3 Creature — Elf Druid, mana cost {G}{G}{G}, owner / controller
///   wired.
/// - <b>Single mana ability (CR 605.1)</b>: {T}: Add {G}{G}{G}. Implemented
///   as a <see cref="ManaAbility"/> producing three green pips in one
///   activation, with a <c>canActivateCheck = !IsTapped</c> gate. The
///   batched-output shape mirrors Mana Crypt's {T}: Add {C}{C}.
///
/// ## Notes
/// - Summoning sickness (CR 302.1 / 605.3a) is the engine's job — not
///   encoded here.
/// - Substitute slot for Joraga Treespeaker (level-up not yet supported by
///   the engine); shape and types intentionally mirror Llanowar Elves /
///   Elvish Mystic so the dispatch / mana-pool wiring is consistent.
/// </summary>
[CardName("Llanowar Tribe")]
public static class LlanowarTribeFactory
{
    public const string CardName = "Llanowar Tribe";
    public const string PrintedManaCost = "{G}{G}{G}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Llanowar Tribe owned and controlled by
    /// <paramref name="owner"/>. The single {T}: Add {G}{G}{G} mana ability
    /// is attached structurally.
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

        // CR 605.1 — {T}: Add {G}{G}{G}. Mana ability (no stack). Tap when
        // activated; gate on !IsTapped to prevent duplicate activations.
        // Batched three-pip output mirrors Mana Crypt's {T}: Add {C}{C}.
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{G}{G}{G}"),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
