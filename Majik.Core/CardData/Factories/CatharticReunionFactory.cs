using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cathartic Reunion (Kaladesh, {1}{R}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, discard two cards.
///    Draw three cards."
///
/// ## Why it gets its own factory
/// Cathartic Reunion is the canonical Modern Rakdos / Dredge / Hollow-One
/// looter — three-for-three rate at sorcery speed for {1}{R}, the cleanest
/// Faithless-Looting-adjacent looter available pre-Modern-Horizons. With
/// Hollow One (this same PR), Bedlam Reveler, and Bridge from Below in the
/// engine, Cathartic Reunion is a first-class binner. The factory plugs
/// into the same shape-only oracle binder lane Faithless Looting / Wrenn's
/// Resolve use (no flashback rider, just resolve-side discard + draw).
///
/// ## Implemented (v1)
///
/// - Sorcery shape, mana cost {1}{R}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) discards two
///   cards then draws three cards. Discard pick uses the same
///   deterministic-or-agent policy as <see cref="FaithlessLootingFactory"/>:
///   the agent's <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/> chooses; null agent / null pick
///   falls back to the last card in hand (mirrors Connive / Faithless
///   Looting fallback).
/// - Empty library: draws what's available, sets the
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> flag (CR 704.5b
///   SBA loss), and continues — same handling as Faithless Looting /
///   Wrenn's Resolve.
///
/// ## Deviation from printed text (documented)
///
/// Printed text says "As an additional cost to cast this spell, discard
/// two cards" (CR 601.2f), meaning the discard happens at announcement
/// (before the spell resolves) and the cast is illegal if the caster
/// can't discard two cards. v1 models the discard at RESOLVE instead —
/// the discard runs as the first half of the resolve effect, then the
/// draw. The functional differences from the printed shape:
///
/// 1. <b>Counter interactions</b>: if Cathartic Reunion is countered, no
///    discard happened in v1 (printed: the discard already happened at
///    announcement, so the countered spell still cost 2 cards). v1
///    treats countering as a full no-op.
/// 2. <b>Hollow-One timing</b>: the printed-as-additional-cost discard
///    increments TurnState.DiscardsByPlayer BEFORE Cathartic Reunion is
///    on the stack. v1 ordering: TurnState updates as the resolve body
///    moves Hand → Graveyard, which is AFTER Cathartic Reunion has
///    resolved. The cards-discarded counter is therefore available to
///    spells cast AFTER Cathartic Reunion resolves but NOT to a Hollow
///    One being cast on the same priority pass before Cathartic Reunion
///    resolves. Most Hollow One lines cast Cathartic Reunion → let it
///    resolve → then cast Hollow One, so the practical impact is small.
///
/// A future PR can promote the discard to a real
/// <see cref="Majik.Core.Costs.IAdditionalCost"/> ("discard N cards")
/// once the engine has the agent-driven "choose N cards to discard"
/// prompt — same queue as Liliana of the Veil + Faithless Looting's
/// deferred discard pick.
///
/// ## Deferred (v1 gaps)
///
/// - Real additional-cost shape (see above).
/// - Agent-driven discard pick prompt (currently last-2-in-hand /
///   heuristic-bot's highest-MV picker via ChooseFromHandAsync).
/// </summary>
[CardName("Cathartic Reunion")]
public static class CatharticReunionFactory
{
    public const string CardName = "Cathartic Reunion";
    public const string PrintedManaCost = "{1}{R}";
    public const int DiscardCount = 2;
    public const int DrawCount = 3;

    /// <summary>
    /// Build a Cathartic Reunion sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
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
    /// Build Cathartic Reunion's resolve effect — discard two cards, then
    /// draw three. See the factory XML docs for the documented deviation
    /// from the printed "additional cost" shape (the discard runs at
    /// resolve here, not at announcement).
    /// </summary>
    /// <param name="caster">The player discarding + drawing.</param>
    /// <param name="agent">Optional agent for discard target selection.
    /// When null, the deterministic v1 picker (last card in hand) is
    /// used. Mirrors Faithless Looting's resolve.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Cathartic Reunion: discard two cards, then draw three cards.", () =>
            {
                // ----------------------------------------------------------
                // CR 701.16 — "Discard two cards." Same agent-or-fallback
                // policy as Faithless Looting. Each move is raw zone
                // manipulation (Hand → Graveyard); the production wiring
                // path (when run via SpellCastFlow with a ZoneService)
                // would route through CardMovedEvent → TurnDriver →
                // TurnState.RecordCardDiscarded.
                //
                // If the hand has fewer than two cards, discard what is
                // available — CR 701.16a treats "discard N cards" as
                // discard up to N when fewer exist.
                // ----------------------------------------------------------
                for (var i = 0; i < DiscardCount; i++)
                {
                    var hand = caster.Zones.Hand.GetCards().ToList();
                    if (hand.Count == 0) break;
                    ICard? pick;
                    if (agent != null)
                    {
                        pick = agent.ChooseFromHandAsync(caster, hand, BotIntent.Discard)
                            .GetAwaiter().GetResult();
                        // null = decline. "Discard a card" is mandatory
                        // (not "may"); fall back to the deterministic
                        // pick so the rules-effect remains observable.
                        // Same posture as ScryDecision's fallback.
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
                // CR 121.1 — "Draw three cards." Three simple top-of-
                // library draws. Empty library mid-draw flags the player
                // for the SBA loss (CR 704.5b) via
                // MarkTriedToDrawFromEmptyLibrary and short-circuits the
                // remaining draws — same handling as Faithless Looting /
                // Wrenn's Resolve.
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
