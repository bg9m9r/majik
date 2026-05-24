using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wheel of Fortune (Limited Edition Alpha / Revised,
/// {2}{R}).
///
/// Sorcery. Oracle text:
///   "Each player discards their hand, then draws seven cards."
///
/// ## Implementation
///
/// Resolves as one effect: every player in turn-order discards their entire
/// hand (CR 701.16 — cards move from hand to graveyard), and then every
/// player draws seven cards (CR 121.1 — top-of-library draws). The "then"
/// between the two halves is read as a sequencing barrier — all discards
/// resolve before any draws, mirroring how the existing
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.WheelTemplate"/>
/// handles its hand → library → draw ordering for shuffle-wheel variants.
///
/// Wheel of Fortune's printed text is "discards their hand, then draws"
/// — distinct from the shuffle-wheel template's "shuffles their hand and
/// graveyard into their library, then draws", so it does NOT bind through
/// <c>WheelTemplate</c>. This factory is the integration point.
///
/// ## v1 simplifications
/// - Player order is taken from <see cref="BuildResolveEffect"/>'s
///   <c>allPlayers</c> argument verbatim; turn-order resolution is the
///   caller's responsibility (matches the rest of the wheel family).
/// - "Draws seven cards" stops when a library runs out and flags the SBA
///   loss (CR 704.5b) on that player — same handling as Faithless Looting.
/// - "Discards their hand" empties the hand zone in iteration order; no
///   discard-choice prompt is required because the text moves the entire
///   hand to the graveyard (CR 701.16a — discard a specific number is
///   the only case that needs a chooser; this is "discards their hand"
///   wholesale).
/// </summary>
[CardName("Wheel of Fortune")]
public static class WheelOfFortuneFactory
{
    public const string CardName = "Wheel of Fortune";
    public const string PrintedManaCost = "{2}{R}";
    public const int DrawCount = 7;

    /// <summary>
    /// Build a Wheel of Fortune sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so callers can splice it into a
    /// <see cref="Majik.Core.Abilities.SpellDefinition"/> or a
    /// <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Wheel of Fortune's resolve effect — every player discards
    /// their hand, then every player draws seven cards. Single
    /// <see cref="IEffect"/> entry so callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players in the game, in the order the
    /// effect should iterate (typically turn order).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect("Wheel of Fortune: each player discards their hand, then draws seven cards.", () =>
            {
                // ----------------------------------------------------------
                // CR 701.16 — "Each player discards their hand."
                // Move every card from each player's hand to that player's
                // graveyard. Snapshot the hand list first so the iteration
                // is not disturbed by RemoveCard mutations.
                // ----------------------------------------------------------
                foreach (var pl in allPlayers)
                {
                    var hand = pl.Zones.Hand.GetCards().ToList();
                    foreach (var c in hand)
                    {
                        pl.Zones.Hand.RemoveCard(c);
                        pl.Zones.Graveyard.AddCard(c);
                        c.SetZone(ZoneType.Graveyard);
                    }
                }

                // ----------------------------------------------------------
                // CR 121.1 — "...then draws seven cards." All discards
                // resolve before any draws (the "then" sequences the two
                // halves). Empty library mid-draw flags the SBA loss
                // (CR 704.5b) for that player and short-circuits the
                // remaining draws for them — other players keep drawing.
                // ----------------------------------------------------------
                foreach (var pl in allPlayers)
                {
                    for (var i = 0; i < DrawCount; i++)
                    {
                        var top = pl.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            pl.MarkTriedToDrawFromEmptyLibrary();
                            break;
                        }
                        pl.Zones.Library.RemoveCard(top);
                        pl.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }
                }
            }),
        };
    }
}
