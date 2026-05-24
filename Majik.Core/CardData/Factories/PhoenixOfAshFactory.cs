using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phoenix of Ash (Throne of Eldraine, {2}{R}{R}).
///
/// Creature — Phoenix 3/2. Oracle text:
///   "Phoenix of Ash can attack as though it didn't have summoning sickness
///    as long as it has haste.
///    Haste.
///    Escape—{3}{R}{R}, Exile four other cards from your graveyard.
///    (You may cast this card from your graveyard for its escape cost.)"
///
/// ## Implemented (v1)
/// - 3/2 Creature — Phoenix, mana cost {2}{R}{R}.
/// - <see cref="KeywordAbility"/> marker for Haste (CR 702.10). The printed
///   "can attack as though it didn't have summoning sickness as long as it
///   has haste" rider collapses observationally to the Haste keyword in v1
///   — CR 702.10b already lets a creature with haste attack the turn it
///   came under its controller's control, so the additional clause only
///   matters when Haste is granted-then-lost mid-turn (deferred until a
///   keyword-loss surface exists). Same simplification posture as
///   <see cref="ArclightPhoenixFactory"/>'s single-keyword Haste path.
///
/// ## Deferred (v1 gaps)
/// - <b>Escape (CR 702.143)</b>: cast-from-graveyard alt cost with the
///   "exile four other cards from your graveyard" rider. Engine has
///   <see cref="Costs.CastFromExileAlternativeCost"/> for cast-from-exile
///   only; no graveyard variant + multi-card-exile additional-cost
///   primitive yet. Same gap as <see cref="UroTitanFactory"/> /
///   <see cref="PhlageFactory"/>.
/// - <b>"Can attack as though it didn't have summoning sickness as long as
///   it has haste"</b>: structurally collapses to the Haste grant in v1.
///   Distinct behaviour only manifests if Haste is removed mid-turn after
///   the controller has owned Phoenix of Ash for less than a full turn
///   — no keyword-removal surface yet, same gap as Goblin Chieftain's
///   Haste-loss interactions.
/// </summary>
[CardName("Phoenix of Ash")]
public static class PhoenixOfAshFactory
{
    public const string CardName = "Phoenix of Ash";
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>
    /// Construct Phoenix of Ash owned and controlled by
    /// <paramref name="owner"/>. Ships the 3/2 Phoenix shape and a Haste
    /// <see cref="KeywordAbility"/> marker (CR 702.10).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Phoenix });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        return card;
    }
}
