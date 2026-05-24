using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faithless Salvaging (Phyrexia: All Will Be One,
/// {1}{R}).
///
/// Sorcery. Oracle text:
///   "Discard a card, then draw a card.
///    Flashback—Discard a creature card."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{R}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) discards one
///   card from the caster's hand, then draws one card from the top of the
///   caster's library (CR 701.16 + CR 121.1). The "then" between discard
///   and draw means the discard happens BEFORE the draw — order matters
///   for "if you discarded a card this way" riders elsewhere in the
///   format (no such rider here, but the order is observable).
/// - Discard pick uses the deterministic v1 policy (first card in hand —
///   mirrors <see cref="DiscardACardCost"/>'s picker). Real agent-driven
///   "choose a card to discard" prompt deferred behind the same queue
///   as Faithless Looting / Liliana of the Veil / Connive / Psychic Frog.
/// - Empty hand: discard step is a no-op (CR 701.16a treats "discard a
///   card" as "discard up to 1" when fewer exist); the draw still fires.
/// - Empty library mid-draw flags the SBA loss flag via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 704.5b /
///   CR 120.3) — same handling as Faithless Looting / Psychic Frog.
/// - Flashback alt-cost is exposed via <see cref="BuildFlashbackCost"/>
///   alongside <see cref="BuildFlashbackAdditionalCosts"/>. Faithless
///   Salvaging's printed flashback cost is "Discard a creature card" —
///   a non-mana cost. The engine's
///   <see cref="FlashbackAlternativeCost"/> only carries the mana
///   portion (CR 118.9), so v1 splits the cost the same way
///   <see cref="CabalTherapyFactory"/> does: the alt cost is
///   <see cref="ManaCost.Zero"/> and the discard rider ships as a
///   separate <see cref="DiscardACreatureCardAdditionalCost"/> that
///   callers thread through <see cref="Majik.Core.Game.SpellCastFlow"/>'s
///   <c>additionalCosts</c> parameter when flashbacking. The post-resolve
///   exile (CR 702.34b) runs through the cost's <c>OnResolved</c> hook.
///
/// ## Deferred (v1 gaps)
/// - "Discard a card" pick prompt — currently first-in-hand. Real agent-
///   driven choice waits on the shared discard-prompt system.
/// - "Discard a creature card" pick prompt — same gap as the resolve-time
///   discard. The cost auto-picks the first creature card in hand.
/// - Flashback-with-non-mana-rider as a single cost: engine's
///   <see cref="IAlternativeCost"/> surface only carries the mana
///   portion, so the discard rider rides as a paired additional cost
///   (same pattern as Cabal Therapy's sacrifice rider).
/// </summary>
[CardName("Faithless Salvaging")]
public static class FaithlessSalvagingFactory
{
    public const string CardName = "Faithless Salvaging";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>
    /// Oracle text reference. Faithless Salvaging's printed flashback cost
    /// is "Discard a creature card" — non-mana, so
    /// <see cref="FlashbackOracleParser"/> would parse the mana portion as
    /// <see cref="ManaCost.Zero"/>. Kept here for documentation; the
    /// flashback cost is built directly by <see cref="BuildFlashbackCost"/>
    /// rather than through the parser (the parser doesn't model the
    /// discard-a-creature-card rider yet).
    /// </summary>
    public const string OracleText =
        "Discard a card, then draw a card.\nFlashback—Discard a creature card.";

    /// <summary>
    /// Build a Faithless Salvaging sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
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
    /// Build Faithless Salvaging's resolve effect — discard one card from
    /// the caster's hand, then draw one card from the top of the caster's
    /// library. Single <see cref="IEffect"/> entry so callers can splice
    /// it into a <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list. The same
    /// effect is reused for both the printed-cost cast and the flashback
    /// cast — flashback's post-resolve exile is performed by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>, not here.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Faithless Salvaging: discard a card, then draw a card.", () =>
            {
                // ----------------------------------------------------------
                // CR 701.16a — "Discard a card." Pick the first card in
                // hand (deterministic v1 policy; mirrors DiscardACardCost
                // and Psychic Frog's loot trigger). Real agent-driven
                // choice deferred. Empty hand is a clean no-op.
                // ----------------------------------------------------------
                var pick = caster.Zones.Hand.GetCards().FirstOrDefault();
                if (pick != null)
                {
                    caster.Zones.Hand.RemoveCard(pick);
                    caster.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }

                // ----------------------------------------------------------
                // CR 121.1 — "...then draw a card." Single top-of-library
                // draw. Empty library flags the SBA loss (CR 704.5b /
                // CR 120.3) via MarkTriedToDrawFromEmptyLibrary — same
                // handling as Faithless Looting / Wrenn's Resolve.
                // ----------------------------------------------------------
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    caster.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }),
        };
    }

    /// <summary>
    /// Build the flashback alternative cost. Faithless Salvaging's printed
    /// flashback cost is "Discard a creature card" — non-mana — so the
    /// returned cost carries <see cref="ManaCost.Zero"/>. The discard
    /// rider ships separately via
    /// <see cref="BuildFlashbackAdditionalCosts"/>; callers compose both
    /// when wiring the flashback cast through
    /// <see cref="Majik.Core.Game.SpellCastFlow"/>. Post-resolve exile
    /// (CR 702.34b) is handled by the cost's <c>OnResolved</c> hook (same
    /// as Faithless Looting / Reckless Charge / Cabal Therapy).
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost() =>
        new FlashbackAlternativeCost(ManaCost.Zero);

    /// <summary>
    /// Build the additional-cost rider that accompanies the flashback
    /// alt-cost — "Discard a creature card" as a non-mana cost
    /// (CR 601.2f / CR 702.34). Returned as a single-element list to
    /// match the shape <see cref="Majik.Core.Game.SpellCastFlow"/> threads
    /// through its <c>additionalCosts</c> parameter. v1 deterministically
    /// picks the first creature card in the caster's hand (mirrors
    /// <see cref="DiscardACreatureCardAdditionalCost"/>'s policy).
    /// </summary>
    public static IReadOnlyList<IAdditionalCost> BuildFlashbackAdditionalCosts() =>
        new IAdditionalCost[] { new DiscardACreatureCardAdditionalCost() };
}
