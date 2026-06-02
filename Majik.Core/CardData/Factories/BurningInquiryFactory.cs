using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Burning Inquiry (Zendikar, {R}).
///
/// Sorcery. Oracle text:
///   "Each player draws three cards, then discards three cards at random."
///
/// ## Why it gets its own factory
/// Burning Inquiry is the symmetric, every-player cousin of
/// <see cref="GoblinLoreFactory"/>: draw a fistful, then a forced random
/// discard — applied to EVERY player, like
/// <see cref="WheelOfFortuneFactory"/>'s "each player" iteration. The
/// "at random" discard is the distinguishing trait — the discard target is
/// chosen by the per-game RNG, not by any player, so it cannot ride the
/// agent-driven discard-pick lane, nor the declarative oracle-spell binder
/// (which has no "discard at random" primitive). This factory combines
/// Wheel of Fortune's all-players iteration with Goblin Lore's
/// <see cref="GameRandom"/>-driven discard.
///
/// ## Implemented (v1)
///
/// - <b>Sorcery</b> at <c>{R}</c> (MV 1), owner/controller wired.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>): every player
///   draws three, then every player discards three cards at random.
///
/// ## CR notes
///
/// - <b>"Each player draws three cards"</b> (CR 121.1): three top-of-library
///   draws per player, performed FIRST for ALL players. The "then" sequences
///   the two halves of the spell — every player completes their draws before
///   any discard occurs, and the freshly-drawn cards are themselves eligible
///   to be the random discards (CR 701.16d — the random discard happens after
///   the draws resolve). Empty library mid-draw flags the SBA loss
///   (CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> and
///   short-circuits that player's remaining draws — other players continue
///   drawing independently (same handling as
///   <see cref="WheelOfFortuneFactory"/>).
/// - <b>"...discards three cards at random"</b> (CR 701.16e): each discarder
///   chooses no cards; the engine picks uniformly at random from that
///   player's current hand. The pick is drawn from the per-game
///   <see cref="GameRandom"/> (looked up via <see cref="GameRandomRegistry"/>)
///   so it is seedable / replayable (CR 100.6). If a player's hand holds
///   fewer than three cards, they discard what is there (CR 701.16a —
///   "discard N" discards up to N when fewer exist).
/// - <b>APNAP order</b> (CR 101.4): "each player" effects resolve in
///   APNAP order. The caller supplies <c>allPlayers</c> in the order the
///   effect should iterate (typically turn order) — same posture as the
///   rest of the wheel family.
///
/// ## Deferred (v1 gaps)
///
/// - Production discard/draw-event wiring: the resolve body moves
///   Library → Hand and Hand → Graveyard via raw zone manipulation, the
///   same posture as <see cref="GoblinLoreFactory"/> /
///   <see cref="WheelOfFortuneFactory"/>. When run through SpellCastFlow
///   with a ZoneService the moves would route through CardMovedEvent →
///   TurnDriver → TurnState bookkeeping.
/// </summary>
[CardName("Burning Inquiry")]
public static class BurningInquiryFactory
{
    public const string CardName = "Burning Inquiry";
    public const string PrintedManaCost = "{R}";
    public const int DrawCount = 3;
    public const int DiscardCount = 3;

    /// <summary>
    /// Build a Burning Inquiry sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so callers can splice it into a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
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
    /// Build Burning Inquiry's resolve effect — every player draws three
    /// cards, then every player discards three cards at random. Single
    /// <see cref="IEffect"/> entry so callers can splice it into a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players in the game, in the order the
    /// effect should iterate (typically turn order / APNAP).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect("Burning Inquiry: each player draws three cards, then discards three cards at random.", () =>
            {
                // ----------------------------------------------------------
                // CR 121.1 — "Each player draws three cards." Three
                // top-of-library draws per player, performed FIRST for ALL
                // players (the "then" sequences the halves so the freshly
                // drawn cards are eligible to be discarded). Empty library
                // mid-draw flags the SBA loss (CR 704.5b) and short-circuits
                // that player's remaining draws — others keep drawing.
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

                // ----------------------------------------------------------
                // CR 701.16e — "...discards three cards at random." Each
                // discarder chooses nothing; the engine picks uniformly at
                // random from that player's current hand using the per-game
                // RNG (CR 100.6, seedable/replayable). Fewer than three cards
                // in a hand discards what is there (CR 701.16a). All discards
                // happen only after every player has finished drawing.
                // ----------------------------------------------------------
                foreach (var pl in allPlayers)
                {
                    var rng = GameRandomRegistry.Get(pl);
                    for (var i = 0; i < DiscardCount; i++)
                    {
                        var hand = pl.Zones.Hand.GetCards().ToList();
                        if (hand.Count == 0) break;
                        var pick = hand[rng.Next(hand.Count)];
                        pl.Zones.Hand.RemoveCard(pick);
                        pl.Zones.Graveyard.AddCard(pick);
                        pick.SetZone(ZoneType.Graveyard);
                    }
                }
            }),
        };
    }
}
