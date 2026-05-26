using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Avacyn's Pilgrim (Innistrad, {G}).
///
/// Creature — Human Monk 1/1. Oracle text:
///   "{T}: Add {W}."
///
/// One-mana off-colour fixer in the Birds of Paradise / Noble Hierarch
/// family — a green-cost creature that taps for white. Backbone of
/// G/W "Selesnya" curves (Voice of Resurgence, Knight of the Reliquary).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Monk at printed cost {G}, owner/controller wired.
/// - <b>Mana ability (CR 605.1)</b>: <c>{T}: Add {W}.</c> Wired via
///   <see cref="ManaAbility"/> with <c>canActivateCheck = !IsTapped</c>
///   so the tap-cost gate is enforced. CR 605.1 — mana abilities don't
///   use the stack and resolve atomically at activation time.
///
/// ## Deferred (v1 gaps)
/// - <b>Summoning sickness gate</b>: Avacyn's Pilgrim's {T} mana ability
///   is gated by <see cref="Majik.Core.Rules.ActionValidator"/>'s
///   tap-cost check against creatures with summoning sickness (CR 302.1).
///   The factory itself doesn't bypass this; enforcement happens upstream
///   at activation validation time — same posture as Llanowar Elves /
///   Birds of Paradise.
/// </summary>
[CardName("Avacyn's Pilgrim")]
public static class AvacynsPilgrimFactory
{
    public const string CardName = "Avacyn's Pilgrim";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Avacyn's Pilgrim owned and controlled by
    /// <paramref name="owner"/>. The {T}: Add {W} mana ability is always
    /// wired.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Monk });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 605.1 — mana ability. {T}: Add {W}. Doesn't use the stack;
        // tap-cost legality gated by !IsTapped (same shape as Llanowar
        // Elves / Birds of Paradise / each colour-fixer of Noble
        // Hierarch).
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{W}"),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
