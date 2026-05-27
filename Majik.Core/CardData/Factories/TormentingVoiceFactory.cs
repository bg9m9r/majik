using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tormenting Voice (Khans of Tarkir, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Discard a card, then draw two cards."
///
/// ## Why it gets its own factory
/// Tormenting Voice is the printed-cost looter sibling of Faithless
/// Looting / Cathartic Reunion: same {1}{R} mana value as Cathartic
/// Reunion but trades one discard for one draw (net +1 hand size). The
/// engine ships several "discard then draw" enablers — Faithless Looting
/// (two-for-two with flashback), Cathartic Reunion (two-for-three), and
/// now Tormenting Voice (one-for-two) — covering the full draw-cost
/// curve for Rakdos / Modern Madness shells. The shape is also the
/// simplest "then" sequence in the engine (CR 121.4 — single instruction
/// sequence; if the discard can't be paid the draw still happens) and
/// is a useful baseline test for the discard-driven Hollow One cost
/// reduction shipped in this same PR.
///
/// ## Implemented (v1)
///
/// - Sorcery shape, mana cost {1}{R}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) discards one
///   card, then draws two cards. Discard pick uses the same agent-or-
///   fallback policy as <see cref="FaithlessLootingFactory"/> and
///   <see cref="CatharticReunionFactory"/>: the agent's
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/> chooses; null agent / null pick
///   falls back to the last card in hand.
/// - "Then" sequencing (CR 121.4): the discard and the draw are a
///   single instruction sequence. If the hand is empty at resolve, the
///   discard is a no-op and the draw still happens — matches printed
///   rulings ("Discard a card, then draw two cards" continues drawing
///   even when the hand is empty).
/// - Empty library: draws what's available, sets the
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> flag (CR 704.5b
///   SBA loss), and continues — same handling as Faithless Looting /
///   Wrenn's Resolve / Cathartic Reunion.
///
/// ## Hollow-One interaction
///
/// The Hand → Graveyard discard move funnels through
/// <see cref="Majik.Core.Events.CardMovedEvent"/> which TurnDriver
/// observes and forwards to
/// <see cref="Majik.Core.Game.TurnState.RecordCardDiscarded"/>. A
/// Hollow One cast LATER the same turn sees the discard via its
/// cost-reduction reducer (each discard → {2} less). This is the
/// minimum reproducer for the Hollow-One cost-reduction wire shipped
/// in this PR.
///
/// ## Deferred (v1 gaps)
///
/// - Agent-driven discard pick prompt (currently last-card-in-hand /
///   heuristic-bot's highest-MV picker via ChooseFromHandAsync).
/// - No flashback / madness riders — Tormenting Voice has none printed.
/// </summary>
[CardName("Tormenting Voice")]
public static class TormentingVoiceFactory
{
    public const string CardName = "Tormenting Voice";
    public const string PrintedManaCost = "{1}{R}";
    public const int DiscardCount = 1;
    public const int DrawCount = 2;

    /// <summary>
    /// Build a Tormenting Voice sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can
    /// splice it into a <see cref="Majik.Core.Game.SpellDefinition"/>
    /// or pass it directly to a <see cref="Majik.Core.Spells.Spell"/>.
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
    /// Build Tormenting Voice's resolve effect — discard one card, then
    /// draw two. Mirrors Cathartic Reunion's resolve shape; the only
    /// difference is the discard / draw counts (1 / 2 vs 2 / 3).
    /// </summary>
    /// <param name="caster">The player discarding + drawing.</param>
    /// <param name="agent">Optional agent for discard target selection.
    /// When null, the deterministic v1 picker (last card in hand) is
    /// used.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Tormenting Voice: discard a card, then draw two cards.", () =>
            {
                // ----------------------------------------------------------
                // CR 701.16 — "Discard a card." Same agent-or-fallback
                // policy as Faithless Looting / Cathartic Reunion. The
                // Hand → Graveyard move (in production via a wired
                // ZoneService) funnels through CardMovedEvent which
                // TurnDriver forwards to TurnState.RecordCardDiscarded
                // (Hollow One reducer input). Empty hand → no-op (the
                // printed "then" still permits the draw to happen per
                // CR 121.4).
                // ----------------------------------------------------------
                var hand = caster.Zones.Hand.GetCards().ToList();
                if (hand.Count > 0)
                {
                    ICard? pick;
                    if (agent != null)
                    {
                        pick = agent.ChooseFromHandAsync(caster, hand, BotIntent.Discard)
                            .GetAwaiter().GetResult();
                        if (pick == null || pick.Zone != ZoneType.Hand)
                            pick = hand[^1];
                    }
                    else
                    {
                        pick = hand[^1];
                    }
                    caster.Zones.Hand.RemoveCard(pick);
                    caster.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }

                // ----------------------------------------------------------
                // CR 121.1 — "Draw two cards." Empty library mid-draw
                // flags the player for the SBA loss (CR 704.5b) and
                // short-circuits the remaining draws.
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
            }),
        };
    }
}
