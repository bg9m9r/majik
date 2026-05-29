using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thrill of Possibility (Throne of Eldraine, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "As an additional cost to cast this spell, discard a card.
///    Draw two cards."
///
/// ## Why it gets its own factory
/// Thrill of Possibility is the instant-speed sibling of Cathartic Reunion —
/// the same additional-discard-cost + draw looter pattern, reduced to discard
/// one / draw two and castable at instant speed ({1}{R}). It is a staple
/// red-deck card-filtering / spellbook-enabler in Modern. It reuses the exact
/// resolve-side discard-then-draw shape of <see cref="CatharticReunionFactory"/>.
///
/// The base card shape (name / Instant type / {1}{R} cost) is materialised
/// from the embedded JSON definition (<c>thrill-of-possibility.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="PlayWithFireFactory"/>); the resolve effect is built on demand
/// via <see cref="BuildResolveEffect"/> because the discard-pick + draw body
/// is not expressible in the data-only JSON <c>AbilityDefinition</c> schema.
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {1}{R}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) discards one card
///   then draws two. Discard pick uses the same deterministic-or-agent policy
///   as <see cref="CatharticReunionFactory"/>: the agent's
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/> chooses; null agent / null pick falls
///   back to the last card in hand.
/// - Empty library: draws what's available, sets the
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> flag (CR 704.5b SBA
///   loss), and continues — same handling as Cathartic Reunion.
///
/// ## Deviation from printed text (documented)
///
/// Printed text says "As an additional cost to cast this spell, discard a
/// card" (CR 601.2f), meaning the discard happens at announcement (before the
/// spell resolves) and the cast is illegal if the caster can't discard a
/// card. v1 models the discard at RESOLVE instead — the discard runs as the
/// first half of the resolve effect, then the draw. This mirrors the
/// documented deviation in <see cref="CatharticReunionFactory"/>:
///
/// 1. <b>Counter interactions</b>: if Thrill of Possibility is countered, no
///    discard happened in v1 (printed: the discard already happened at
///    announcement, so the countered spell still cost a card). v1 treats
///    countering as a full no-op.
/// 2. <b>Discard-counter timing</b>: the printed-as-additional-cost discard
///    increments the turn's discard counters BEFORE Thrill of Possibility is
///    on the stack; v1 ordering updates as the resolve body moves Hand →
///    Graveyard, AFTER the spell has resolved.
///
/// A future PR can promote the discard to a real
/// <see cref="Majik.Core.Costs.IAdditionalCost"/> ("discard a card") once the
/// engine has the agent-driven "choose a card to discard" cast-time prompt —
/// same queue as Cathartic Reunion / Faithless Looting's deferred discard pick.
///
/// ## Deferred (v1 gaps)
///
/// - Real additional-cost shape (see above).
/// - Agent-driven discard pick prompt (currently last-in-hand /
///   heuristic-bot's picker via ChooseFromHandAsync).
/// </summary>
[CardName("Thrill of Possibility")]
public static class ThrillOfPossibilityFactory
{
    public const string CardName = "Thrill of Possibility";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "thrill-of-possibility";

    public const string PrintedManaCost = "{1}{R}";
    public const int DiscardCount = 1;
    public const int DrawCount = 2;

    /// <summary>
    /// Build the Thrill of Possibility instant shape from the embedded JSON
    /// definition. Card shape only — the resolve effect is built on demand
    /// via <see cref="BuildResolveEffect"/> (same split as
    /// <see cref="CatharticReunionFactory"/>).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Thrill of Possibility's resolve effect — discard one card, then
    /// draw two. See the factory XML docs for the documented deviation from
    /// the printed "additional cost" shape (the discard runs at resolve here,
    /// not at announcement).
    /// </summary>
    /// <param name="caster">The player discarding + drawing.</param>
    /// <param name="agent">Optional agent for discard target selection. When
    /// null, the deterministic v1 picker (last card in hand) is used. Mirrors
    /// Cathartic Reunion's resolve.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Thrill of Possibility: discard a card, then draw two cards.", () =>
            {
                // ----------------------------------------------------------
                // CR 701.16 — "Discard a card." Same agent-or-fallback policy
                // as Cathartic Reunion. Raw zone manipulation (Hand →
                // Graveyard); the production wiring path (when run via
                // SpellCastFlow with a ZoneService) would route through
                // CardMovedEvent → TurnDriver → TurnState.RecordCardDiscarded.
                //
                // If the hand is empty, there is nothing to discard — discard
                // is a no-op (the additional-cost gate is the printed shape;
                // v1's resolve-side discard discards what's available).
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
                        // null = decline. "Discard a card" is mandatory (not
                        // "may"); fall back to the deterministic pick so the
                        // rules-effect remains observable. Same posture as
                        // Cathartic Reunion / ScryDecision's fallback.
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
                // CR 121.1 — "Draw two cards." Two simple top-of-library
                // draws. Empty library mid-draw flags the player for the SBA
                // loss (CR 704.5b) via MarkTriedToDrawFromEmptyLibrary and
                // short-circuits the remaining draws — same handling as
                // Cathartic Reunion.
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
