using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reforge the Soul (Avacyn Restored, {3}{R}{R}).
///
/// Sorcery. Oracle text:
///   "Each player discards their hand, then draws seven cards.
///    Miracle {1}{R} (You may cast this card for its miracle cost when
///    you draw it if it's the first card you drew this turn.)"
///
/// ## Implemented (v1)
///
/// - <b>Sorcery</b> at <c>{3}{R}{R}</c> (MV 5), owner/controller wired.
/// - <b>Wheel effect (CR 701.16 + CR 121.1)</b>: every player discards
///   their entire hand to their graveyard, then every player draws seven
///   cards. The "then" between the two halves is a sequencing barrier —
///   all discards resolve before any draws begin (same body as
///   <see cref="WheelOfFortuneFactory.BuildResolveEffect"/>).
/// - <b>Miracle {1}{R} (CR 702.94)</b> — wired as a real alternative cost.
///   The factory stamps the printed miracle cost via
///   <see cref="Card.SetMiracleCost"/>; the draw hook in
///   <see cref="Majik.Core.Game.TurnDriver"/> opens the one-shot window when
///   Reforge is the first card its controller drew this turn (CR 702.94b),
///   and the card may then be cast from hand for {1}{R} via
///   <see cref="Majik.Core.Costs.MiracleAlternativeCost"/> (surfaced to the
///   bot by <see cref="Majik.Core.Players.Agents.MiracleAltCostProbe"/>).
///   The <see cref="KeywordAbility"/>("Miracle") marker is retained for
///   keyword scanners — same posture as
///   <see cref="BonfireOfTheDamnedFactory"/>.
///
/// ## CR notes
///
/// - <b>"Each player discards their hand"</b> (CR 701.16a): the entire
///   hand zone moves to the graveyard — no choice of which cards to keep.
///   Hand is snapshotted before removal so mutation during iteration is safe.
/// - <b>"...then draws seven cards"</b> (CR 121.1): top-of-library draws.
///   If a player's library has fewer than 7 cards, they draw until the
///   library is empty; the next draw attempt sets the
///   <see cref="Player.TriedToDrawFromEmptyLibrary"/> flag (CR 704.5b SBA).
///   Other players continue drawing independently.
/// </summary>
[CardName("Reforge the Soul")]
public static class ReforgeTheSoulFactory
{
    public const string CardName = "Reforge the Soul";
    public const string PrintedManaCost = "{3}{R}{R}";
    public const string MiracleCostText = "{1}{R}";
    public const int DrawCount = 7;

    /// <summary>
    /// Build a Reforge the Soul sorcery owned by <paramref name="owner"/>.
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

        // CR 702.94 — Miracle. Keyword marker + the printed miracle cost the
        // draw hook reads to open the first-card-drawn-this-turn window
        // (see MiracleAlternativeCost).
        card.AddAbility(new KeywordAbility("Miracle", card, owner));
        card.SetMiracleCost(Majik.Core.ValueObjects.ManaCost.Parse(MiracleCostText));

        return card;
    }

    /// <summary>
    /// Build Reforge the Soul's resolve effect — every player discards
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
            new Effect("Reforge the Soul: each player discards their hand, then draws seven cards.", () =>
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
