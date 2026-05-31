using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Drift of Phantasms (Future Sight, {3}{U}).
///
/// Creature — Illusion 1/5. Oracle text:
///   "Defender.
///    Transmute {1}{U}{U} ({1}{U}{U}, Discard this card: Search your
///    library for a card with the same mana value as this card, reveal
///    it, put it into your hand, then shuffle.)"
///
/// CR 702.49 — Transmute is an activated ability that functions only
/// while the card with Transmute is in a player's hand. "[Cost], Discard
/// this card: Search your library for a card with the same mana value
/// as the discarded card, reveal it, put it into your hand, then
/// shuffle." Activate only as a sorcery (CR 702.49b).
///
/// Drift of Phantasms has MV 4 (printed {3}{U}); Transmute discards Drift
/// and tutors a 4-MV card to hand. The classic Modern application is
/// Glittering Wish / Restore Balance / Living End — all 4-MV singletons
/// fetched by Drift to enable cascade-shell shenanigans, but in v1 the
/// tutor is shape-correct and works against the printed-MV match.
///
/// ## Implemented (v1)
/// - <b>Creature — Illusion {3}{U} 1/5</b>.
/// - <b>Defender</b> (CR 702.3) wired via the <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces the can't-attack restriction.
/// - <b>Transmute {1}{U}{U}</b> (CR 702.49) — an
///   <see cref="ActivatedAbility"/> attached to the card shape with cost
///   stack <c>[ManaCostCost({1}{U}{U}), DiscardSelfCost(Drift)]</c>. The
///   <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.49a) is the
///   activated-from-hand surface — same shape as Cycling. The resolve
///   body searches the controller's library for a card whose
///   <see cref="ValueObjects.ManaCost.TotalValue"/> equals Drift's printed
///   MV (4), agent-prompts via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> with a
///   deterministic first-match fallback (CR 701.19a — same posture as
///   Mystical Tutor / Wishclaw / Stoneforge Mystic), moves the pick
///   Library → Hand, then shuffles the library
///   (<see cref="LibraryShuffle.ShuffleLibrary"/>, CR 701.20a).
/// - <b>Sorcery-speed gate</b> (CR 702.49b) — wired via
///   <see cref="ActivatedAbility"/>'s <c>sorcerySpeed: true</c> flag so
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects activations
///   outside the controller's main phase / with a non-empty stack.
///
/// ## Marker keyword
/// - A <see cref="KeywordAbility"/>("Transmute") marker is also attached
///   so oracle audits + future bot-decision layers can detect the
///   keyword without scanning the activated-ability cost stack (mirrors
///   <see cref="Majik.Core.Keywords.CyclingFactory"/>'s "Cycling" marker
///   convention).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b> — the picked card moves Library → Hand without
///   publishing a reveal event, same gap as every other tutor factory
///   (Mystical Tutor / Stoneforge Mystic / Wishclaw Talisman).
/// - <b>MV-snapshot policy</b> — v1 reads Drift's printed MV from
///   <c>card.ManaCostValue.TotalValue</c>. The CR-accurate posture is the
///   discarded card's MV at the moment the cost is paid (Transmute "card
///   with the same mana value as the discarded card"); for a printed-cost
///   card with no cost-modifying effects in the hand zone these are
///   identical. Polish for when the engine surfaces hand-side cost
///   modifications.
/// - <b>Stack-uncounterable rider</b> — Transmute's activated ability is
///   not a spell (CR 702.49d), so counterspells targeting "spells" can't
///   counter it. The engine's stack already distinguishes spells from
///   activated abilities so this is structural, not a separate flag.
/// </summary>
[CardName("Drift of Phantasms")]
public static class DriftOfPhantasmsFactory
{
    public const string CardName = "Drift of Phantasms";
    public const string PrintedManaCost = "{3}{U}";
    public const int Power = 1;
    public const int Toughness = 5;
    public const string TransmuteCost = "{1}{U}{U}";

    /// <summary>
    /// Construct Drift of Phantasms. Defender keyword marker + Transmute
    /// activated ability + Transmute keyword marker are wired
    /// unconditionally; the Transmute activation is gated to the
    /// controller's hand by <see cref="DiscardSelfCost"/> and to
    /// sorcery-speed timing by <see cref="ActivatedAbility.IsSorcerySpeed"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Illusion });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.3 — Defender. KeywordAbility marker so
        // CombatAbilities.HasDefender surfaces the can't-attack rider for
        // BlockLegality / CombatValidator consumers.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // ----------------------------------------------------------------
        // CR 702.49 — Transmute {1}{U}{U}.
        //   "[Cost], Discard this card: Search your library for a card
        //    with the same mana value as the discarded card, reveal it,
        //    put it into your hand, then shuffle."
        // Activated-from-hand via DiscardSelfCost (CR 702.49a hand-zone
        // gate, same shape as Cycling). Sorcery-speed via the
        // ActivatedAbility flag (CR 702.49b).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Transmute", card, owner));

        var transmuteEffect = new Effect(
            $"{CardName}: transmute — tutor MV {card.ManaCostValue.TotalValue} card to hand",
            async ctx =>
            {
                var targetMv = card.ManaCostValue.TotalValue;

                bool Pred(ICard c)
                {
                    // ICard exposes the printed mana-cost string; parse for
                    // a defensive MV read (production Card instances cache
                    // this as ManaCostValue but the contract here is only
                    // ICard).
                    if (string.IsNullOrEmpty(c.ManaCost)) return targetMv == 0;
                    try
                    {
                        return ManaCost.Parse(c.ManaCost).TotalValue == targetMv;
                    }
                    catch
                    {
                        return false;
                    }
                }

                var candidates = owner.Zones.Library.GetCards().Where(Pred).ToList();

                // CR 701.19a — prompt agent even on zero candidates so
                // the human searcher sees the failed search (see
                // LibrarySearch xmldoc).
                var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
                    ctx, owner, candidates, $"card with mana value {targetMv}").ConfigureAwait(false);

                if (pick != null)
                {
                    owner.Zones.Library.RemoveCard(pick);
                    owner.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }
                // CR 701.20a — shuffle whether or not a card was found.
                LibraryShuffle.ShuffleLibrary(owner, "drift-of-phantasms");
            });

        var transmute = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(TransmuteCost),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[] { transmuteEffect },
            sorcerySpeed: true);

        card.AddAbility(transmute);

        return card;
    }
}
