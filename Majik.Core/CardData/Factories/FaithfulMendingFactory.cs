using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faithful Mending (Innistrad: Midnight Hunt, {W}{U}).
///
/// Instant. Oracle text:
///   "You gain 2 life, draw two cards, then discard two cards.
///    Flashback {1}{W}{U}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}{U}, mana value 2; colors White + Blue
///   (Azorius).
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) in sequence:
///     1. Caster gains 2 life (CR 119.3 — <see cref="Fx.GainLife"/>).
///     2. Draw two cards from top of controller's library (CR 121.1).
///     3. Discard two cards from hand (CR 701.16).
///   Net hand size is +0 when the library has at least two cards; the
///   controller trades two hand cards for two fresh draws and 2 bonus life.
/// - Draw order mirrors <see cref="FaithlessLootingFactory.BuildResolveEffect"/>
///   but with "gain life first" prepended and draw-THEN-discard order
///   (opposite of Faithless Looting which draws then discards).
/// - Discard pick uses the deterministic v1 policy (last card in hand per
///   pass). Agent path: consult
///   <see cref="IPlayerAgent.ChooseFromHandAsync(Player, IReadOnlyList{ICard}, BotIntent)"/>
///   with <see cref="BotIntent.Discard"/>. Mirrors FaithlessLootingFactory.
/// - Flashback alt-cost ({1}{W}{U}) is exposed via
///   <see cref="BuildFlashbackCost"/> (parsed by
///   <see cref="FlashbackOracleParser"/> from the printed oracle text so
///   the data-driven binder path and this named-factory path agree on cost
///   shape). Post-resolve exile (CR 702.33b) handled by the cost's
///   <c>OnResolved</c> hook.
///
/// ## Relevant rules
/// - CR 702.33 — Flashback keyword (cast from graveyard for flashback cost,
///   then exile).
/// - CR 702.33b — "If a card with flashback is in a graveyard, its owner
///   may cast it using its flashback cost. After it resolves, it's exiled."
/// - CR 119.3 — Life gain.
/// - CR 121.1 — Draw.
/// - CR 701.16 — Discard.
///
/// ## Deferred (v1 gaps)
/// - "Discard two cards" pick prompt — currently last-2-in-hand. Real
///   agent-driven choice waits on the same discard-prompt system queued
///   behind Faithless Looting / Connive / Liliana of the Veil.
/// </summary>
[CardName("Faithful Mending")]
public static class FaithfulMendingFactory
{
    public const string CardName = "Faithful Mending";
    public const string PrintedManaCost = "{W}{U}";

    /// <summary>
    /// Oracle text used by <see cref="BuildFlashbackCost"/> to derive the
    /// flashback cost via <see cref="FlashbackOracleParser"/>. Kept on the
    /// factory so the production load path (Scryfall row → oracle text →
    /// parser) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "You gain 2 life, draw two cards, then discard two cards.\nFlashback {1}{W}{U}";

    /// <summary>Life gain amount on resolution (CR 119.3).</summary>
    public const int LifeGainAmount = 2;

    /// <summary>CardDef DSL — Instant shape only. Resolve body lives in
    /// <see cref="BuildResolveEffect"/>; flashback cost in
    /// <see cref="BuildFlashbackCost"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    /// <summary>
    /// Construct Faithful Mending as an <see cref="Instant"/> owned by
    /// <paramref name="owner"/>. Shape only (no resolve closure wired on
    /// this path — mirrors FaithlessLootingFactory).
    /// </summary>
    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Faithful Mending's resolve effect — gain 2 life, draw two
    /// cards, then discard two cards. Single <see cref="IEffect"/> entry so
    /// callers can splice it into a <c>SpellDefinition.EffectFactory</c>
    /// result or a <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// The same effect is reused for both the printed-cost cast and the
    /// flashback cast — flashback's post-resolve exile is performed by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>, not here.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Faithful Mending: gain 2 life, draw two cards, then discard two cards.", () =>
            {
                // ----------------------------------------------------------
                // CR 119.3 — "You gain 2 life." Routed through Fx.GainLife
                // so the LifeGainIntent replacement bus is honoured (CR 119.6).
                // ----------------------------------------------------------
                Fx.GainLife(caster, LifeGainAmount);

                // ----------------------------------------------------------
                // CR 121.1 — "Draw two cards." Two simple top-of-library
                // draws. Empty library mid-draw flags the player for the
                // SBA loss (CR 704.5b) and short-circuits the remaining
                // draws. The "then" between draw and discard means the two
                // halves resolve as a single instruction sequence — we
                // never partial-out the discard if the draw underflowed.
                // ----------------------------------------------------------
                for (var i = 0; i < 2; i++)
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
                // CR 701.16 — "Discard two cards." Agent path: consult
                // ChooseFromHandAsync(BotIntent.Discard) for each pick;
                // the heuristic bot's override pitches the highest-MV card
                // each pass. Default path (no agent): last-card-in-hand
                // (deterministic v1 policy mirroring FaithlessLootingFactory).
                //
                // If the hand has fewer than two cards (e.g. drew on an
                // empty library mid-resolve), discard what is available —
                // CR 701.16a treats "discard N cards" as discard up to N
                // when fewer exist.
                // ----------------------------------------------------------
                for (var i = 0; i < 2; i++)
                {
                    var hand = caster.Zones.Hand.GetCards().ToList();
                    if (hand.Count == 0) break;
                    ICard? pick;
                    if (agent != null)
                    {
                        pick = agent.ChooseFromHandAsync(caster, hand, BotIntent.Discard)
                            .GetAwaiter().GetResult();
                        // null = decline. "Discard a card" is mandatory
                        // (not "may"); fall back to the deterministic pick
                        // so the rules-effect remains observable. Same
                        // posture as FaithlessLootingFactory's fallback.
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
            }),
        };
    }

    /// <summary>
    /// Build the flashback alternative cost ({1}{W}{U}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here)
    /// keeps the named-factory path and the data-driven oracle binder path
    /// agreeing on shape — any change to the parser's interpretation of
    /// "Flashback {1}{W}{U}" flows through to this factory automatically.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Faithful Mending's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
