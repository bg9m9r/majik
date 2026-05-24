using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faithless Looting (Innistrad / Modern Horizons,
/// {R}).
///
/// Sorcery. Oracle text:
///   "Draw two cards, then discard two cards.
///    Flashback {2}{R}."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) draws two cards
///   from the top of the controller's library, then discards two cards
///   from hand (CR 121.1 + CR 701.16). Net effect is +0 hand size when the
///   library has at least two cards available; the controller swaps two
///   chosen cards in hand for two fresh top-deck draws.
/// - Discard pick uses the deterministic v1 policy (most-recent cards in
///   hand — i.e. the just-drawn cards if no other replacement). Mirrors
///   <see cref="Majik.Core.Keywords.ConniveAction"/>'s discard policy until
///   an agent prompt for "choose N cards to discard" ships.
/// - Empty library: draws what's available, then discards from the new
///   hand (CR 704.5b SBA loss flag set by the underlying draw on the empty
///   library — same handling as Wrenn's Resolve).
/// - Flashback alt-cost ({2}{R}) is exposed via
///   <see cref="BuildFlashbackCost"/> (parsed by
///   <see cref="FlashbackOracleParser"/> from the printed oracle text so
///   the data-driven binder path and this named-factory path agree on cost
///   shape). Callers wire the returned <see cref="FlashbackAlternativeCost"/>
///   into <see cref="Majik.Core.Game.SpellCastFlow"/> when casting from
///   graveyard; resolution side-effects (exile after resolve, CR 702.34b)
///   are handled by the cost's <c>OnResolved</c> hook.
///
/// ## Deferred (v1 gaps)
/// - "Discard two cards" pick prompt — currently last-2-in-hand. Real
///   agent-driven choice waits on the same discard-prompt system that
///   other v1 discard sites (Connive, Liliana of the Veil, Yawgmoth) are
///   queued behind.
/// - Madness / "exile from graveyard" alternate resolution riders are
///   out of scope; only the printed flashback alt cost is wired.
/// </summary>
[CardName("Faithless Looting")]
public static class FaithlessLootingFactory
{
    public const string CardName = "Faithless Looting";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Oracle text used by <see cref="BuildFlashbackCost"/> to derive the
    /// flashback cost via <see cref="FlashbackOracleParser"/>. Kept on the
    /// factory so the production load path (Scryfall row → oracle text →
    /// parser) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "Draw two cards, then discard two cards.\nFlashback {2}{R}";

    /// <summary>
    /// Build a Faithless Looting sorcery owned by <paramref name="owner"/>.
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
    /// Build Faithless Looting's resolve effect — draw two cards, then
    /// discard two cards. Single <see cref="IEffect"/> entry so callers
    /// can splice it into a <c>SpellDefinition.EffectFactory</c> result or
    /// a <see cref="Majik.Core.Spells.Spell"/>'s effect list. The same
    /// effect is reused for both the printed-cost cast and the flashback
    /// cast — flashback's post-resolve exile is performed by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>, not here.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Faithless Looting: draw two cards, then discard two cards.", () =>
            {
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
                // CR 701.16 — "Discard two cards." Pick the last two cards
                // in hand (deterministic v1 policy; mirrors ConniveAction).
                // The most-recent positions in the hand list are typically
                // the just-drawn cards when the controller had a small or
                // empty starting hand. Real agent-driven choice deferred.
                //
                // If the hand has fewer than two cards (e.g. drew on an
                // empty library mid-resolve), discard what is available —
                // CR 701.16a treats "discard N cards" as discard up to N
                // when fewer exist.
                // ----------------------------------------------------------
                for (var i = 0; i < 2; i++)
                {
                    var pick = caster.Zones.Hand.GetCards().LastOrDefault();
                    if (pick == null) break;
                    caster.Zones.Hand.RemoveCard(pick);
                    caster.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }
            }),
        };
    }

    /// <summary>
    /// Build the flashback alternative cost ({2}{R}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here)
    /// keeps the named-factory path and the data-driven oracle binder path
    /// agreeing on shape — any change to the parser's interpretation of
    /// "Flashback {2}{R}" flows through to this factory automatically.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Faithless Looting's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
