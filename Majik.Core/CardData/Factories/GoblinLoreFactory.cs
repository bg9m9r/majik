using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Lore (Tempest, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Draw four cards, then discard three cards at random."
///
/// ## Why it gets its own factory
/// Goblin Lore is a Burning-Inquiry / Reforge-the-Soul-family looter:
/// draw a fistful, then a forced random discard. The "at random" half is
/// the distinguishing trait — the discard target is chosen by the per-game
/// RNG, not by the controller, so it cannot ride the agent-driven
/// discard-pick lane Faithless Looting / Cathartic Reunion use, nor the
/// declarative oracle-spell binder (which has no "discard at random"
/// primitive). It mirrors the pure-C# resolve-effect shape of
/// <see cref="CatharticReunionFactory"/> (single-player draw + discard),
/// swapping the agent pick for a <see cref="GameRandom"/> pick.
///
/// ## Implemented (v1)
///
/// - <b>Sorcery</b> at <c>{1}{R}</c> (MV 2), owner/controller wired.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>): draw four,
///   then discard three cards at random.
///
/// ## CR notes
///
/// - <b>"Draw four cards"</b> (CR 121.1): four top-of-library draws,
///   performed FIRST. The "then" sequences the two halves — the four
///   drawn cards join the hand and are themselves eligible to be the
///   random discards (CR 701.16d the random discard happens after the
///   draw resolves). Empty library mid-draw flags the SBA loss
///   (CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   and short-circuits the remaining draws — same handling as
///   <see cref="CatharticReunionFactory"/>.
/// - <b>"...discard three cards at random"</b> (CR 701.16e): the discarder
///   chooses no cards; the engine picks uniformly at random from the
///   current hand. Pick is drawn from the per-game
///   <see cref="GameRandom"/> (looked up via
///   <see cref="GameRandomRegistry"/>) so it is seedable / replayable
///   (CR 100.6). If the hand holds fewer than three cards, discard what
///   is there (CR 701.16a — "discard N" discards up to N when fewer
///   exist).
///
/// ## Deferred (v1 gaps)
///
/// - Production discard-event wiring: the resolve body moves Hand →
///   Graveyard via raw zone manipulation, the same posture as
///   <see cref="CatharticReunionFactory"/> / <see cref="FaithlessLootingFactory"/>.
///   When run through SpellCastFlow with a ZoneService the moves would
///   route through CardMovedEvent → TurnDriver → TurnState.RecordCardDiscarded.
/// </summary>
[CardName("Goblin Lore")]
public static class GoblinLoreFactory
{
    public const string CardName = "Goblin Lore";
    public const string PrintedManaCost = "{1}{R}";
    public const int DrawCount = 4;
    public const int DiscardCount = 3;

    /// <summary>
    /// Build a Goblin Lore sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Spells.Spell"/>'s effect list.
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
    /// Build Goblin Lore's resolve effect — draw four cards, then discard
    /// three cards at random. Single <see cref="IEffect"/> entry so callers
    /// can splice it into a <see cref="Majik.Core.Spells.Spell"/>'s effect
    /// list.
    /// </summary>
    /// <param name="caster">The player drawing + discarding.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect("Goblin Lore: draw four cards, then discard three cards at random.", () =>
            {
                // ----------------------------------------------------------
                // CR 121.1 — "Draw four cards." Four top-of-library draws,
                // performed first (the "then" sequences the halves so the
                // freshly-drawn cards are eligible to be discarded). Empty
                // library mid-draw flags the SBA loss (CR 704.5b) and
                // short-circuits — same handling as Cathartic Reunion.
                // ----------------------------------------------------------
                for (var i = 0; i < DrawCount; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }

                // ----------------------------------------------------------
                // CR 701.16e — "...discard three cards at random." The
                // discarder chooses nothing; the engine picks uniformly at
                // random from the current hand using the per-game RNG
                // (CR 100.6, seedable/replayable). Fewer than three cards in
                // hand discards what is there (CR 701.16a).
                // ----------------------------------------------------------
                var rng = GameRandomRegistry.Get(caster);
                for (var i = 0; i < DiscardCount; i++)
                {
                    var hand = caster.Zones.Hand.GetCards().ToList();
                    if (hand.Count == 0) break;
                    var pick = hand[rng.Next(hand.Count)];
                    caster.Zones.Hand.RemoveCard(pick);
                    caster.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }
            }),
        };
    }
}
