using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Big Score (Outlaws of Thunder Junction, {3}{R}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, discard a card.
///    Draw two cards and create two Treasure tokens. (They're artifacts
///    with "{T}, Sacrifice this token: Add one mana of any color.")"
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Set: Outlaws of Thunder Junction (otj), uncommon</item>
///   <item>Mana cost: {3}{R}; mana value 4</item>
///   <item>Type line: Instant</item>
///   <item>Colors: R; color identity: R</item>
/// </list>
///
/// ## Why it gets its own factory
/// Big Score is the loot-plus-ramp Unexpected Windfall variant — at instant
/// speed it pitches a card, draws two, and mints two Treasures, fuelling
/// red ramp / storm / reanimator lines. It combines the discard-as-cost +
/// draw shape of <see cref="CatharticReunionFactory"/> with the Treasure
/// minting of <see cref="StrikeItRichFactory"/> (both primitives already
/// shipped). No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{R}, red.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>): discard one
///   card, draw two cards, then create two Treasure tokens under the
///   caster's control via <see cref="TokenFactory.CreateTreasure"/> (each a
///   colourless artifact with five <see cref="ManaAbility"/> options —
///   W/U/B/R/G — per CR 111.10). The discard pick uses the same
///   deterministic-or-agent policy as <see cref="CatharticReunionFactory"/>:
///   the agent's <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/> chooses; null agent / null pick falls
///   back to the last card in hand.
/// - Empty library mid-draw: draws what's available, sets the
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> flag (CR 704.5b
///   SBA loss) and continues — same handling as Cathartic Reunion.
///
/// ## Rules citations
/// - CR 601.2f — "additional cost to cast" (the discard). v1 deviation
///   below.
/// - CR 121.1 — "Draw two cards."
/// - CR 111.10 — Treasure token (colourless artifact, any-colour sac mana).
///
/// ## Deviation from printed text (documented)
/// Printed text makes the discard an additional COST paid at announcement
/// (CR 601.2f) — the cast is illegal with an empty hand, and the discard
/// already happened if the spell is later countered. Mirroring
/// <see cref="CatharticReunionFactory"/>, v1 models the discard at RESOLVE
/// (first half of the resolve effect), so a countered Big Score is a full
/// no-op here. A future PR can promote the discard to a real
/// <see cref="Majik.Core.Costs.IAdditionalCost"/> once the engine has the
/// agent-driven cast-time discard prompt — same queue as Cathartic Reunion.
///
/// ## Deferred (v1 gaps)
/// - Real additional-cost shape (see above).
/// - Treasure tap-to-sac colour prompt: uses the five-option ManaAbility
///   model shared by all Treasure tokens; agent picks the colour at
///   mana-pick time.
/// </summary>
[CardName("Big Score")]
public static class BigScoreFactory
{
    public const string CardName = "Big Score";
    public const string PrintedManaCost = "{3}{R}";
    public const int DiscardCount = 1;
    public const int DrawCount = 2;
    public const int TreasureCount = 2;

    /// <summary>
    /// Build a Big Score instant owned by <paramref name="owner"/>. Card
    /// shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Big Score's resolve effect — discard one card, draw two cards,
    /// then create two Treasure tokens. See the factory XML docs for the
    /// documented deviation from the printed "additional cost" shape (the
    /// discard runs at resolve here, not at announcement).
    /// </summary>
    /// <param name="caster">The player discarding, drawing, and receiving
    /// the Treasures (CR 111.10 — they enter under the caster's control).</param>
    /// <param name="agent">Optional agent for discard target selection.
    /// When null, the deterministic v1 picker (last card in hand) is used —
    /// mirrors Cathartic Reunion's resolve.</param>
    /// <param name="zoneService">Optional zone service — routes the Treasure
    /// ETB through <see cref="ZoneService"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (enabling
    /// downstream triggers). Null → direct zone move, suitable for
    /// unit-test / shape-only paths.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IPlayerAgent? agent = null,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                "Big Score: discard a card, draw two cards, create two Treasure tokens.",
                () =>
                {
                    // ------------------------------------------------------
                    // CR 601.2f — "As an additional cost to cast this spell,
                    // discard a card." Modelled at resolve in v1 (see XML
                    // docs). Same agent-or-fallback policy as Cathartic
                    // Reunion / Faithless Looting. If the hand is empty the
                    // discard is a no-op here (the printed additional-cost
                    // gate is deferred).
                    // ------------------------------------------------------
                    for (var i = 0; i < DiscardCount; i++)
                    {
                        var hand = caster.Zones.Hand.GetCards().ToList();
                        if (hand.Count == 0) break;
                        ICard? pick;
                        if (agent != null)
                        {
                            pick = agent.ChooseFromHandAsync(caster, hand, BotIntent.Discard)
                                .GetAwaiter().GetResult();
                            // null = decline. The discard is mandatory; fall
                            // back to the deterministic pick so the effect
                            // stays observable (Cathartic Reunion parity).
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

                    // ------------------------------------------------------
                    // CR 121.1 — "Draw two cards." Empty library mid-draw
                    // flags the SBA loss (CR 704.5b) via
                    // MarkTriedToDrawFromEmptyLibrary and short-circuits the
                    // remaining draws — Cathartic Reunion parity.
                    // ------------------------------------------------------
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

                    // ------------------------------------------------------
                    // CR 111.10 — "create two Treasure tokens." Each is a
                    // colourless artifact with the five-option any-colour
                    // sac mana ability. TokenFactory.CreateTreasure handles
                    // the full spec + the battlefield ETB move.
                    // ------------------------------------------------------
                    for (var i = 0; i < TreasureCount; i++)
                    {
                        TokenFactory.CreateTreasure(caster, zoneService);
                    }
                }),
        };
    }
}
