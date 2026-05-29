using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Overgrown Battlement (Rise of the Eldrazi,
/// {1}{G}).
///
/// Creature — Wall 0/4. Oracle text:
///   "Defender
///    {T}: Add {G} for each creature you control with defender."
///
/// The premier "Wall ramp" engine — pairs with Axebane Guardian / Assault
/// Formation decks, exploding mana off a board of Defender creatures.
///
/// ## Implemented (v1)
/// - 0/4 Creature — Wall at printed cost {1}{G}, owner/controller wired.
/// - <b>Defender keyword (CR 702.3)</b>: wired as a
///   <see cref="KeywordAbility"/> marker so
///   <see cref="CombatAbilities.HasDefender"/> surfaces it (combat
///   block-legality treats the card as a blocker only). The Battlement is
///   therefore itself a "creature you control with defender" and counts
///   toward its own mana ability.
/// - <b>Defender-tribal mana ability (CR 605.1 / 107.1b)</b>:
///   <c>{T}: Add {G} for each creature you control with defender.</c>
///   Wired via the <see cref="ManaAbility"/> <c>Func&lt;ManaCost&gt;</c>
///   generator overload (Elvish Archdruid / Tron-land / Nykthos shape).
///   The generator counts creatures on the controller's battlefield that
///   have defender — surfaced through
///   <see cref="CombatAbilities.HasDefender"/> reading the
///   <see cref="KeywordAbility"/> marker — and returns a
///   <see cref="ManaCost"/> of N green pips. With just the Battlement
///   alone it produces {G}; with two more defenders it produces {G}{G}{G}.
///
/// ## X-count semantics
/// - Counted at activation (CR 605.1 — mana abilities don't use the stack;
///   the generator runs atomically). Same snapshot posture as Elvish
///   Archdruid's {T} ability — read once, freeze for the activation.
/// - INCLUDES the Battlement itself (oracle reads "each creature you
///   control with defender" with no "other" qualifier; the Battlement has
///   defender).
/// - Counts creatures on the controller's battlefield only (CR 109.5 —
///   "you control" = controller, not opponents) that carry defender; the
///   controller's non-defender creatures are excluded.
///
/// ## Deferred (v1 gaps)
/// - <b>Summoning-sickness gate</b>: the {T} mana ability is gated by
///   <see cref="Majik.Core.Rules.ActionValidator"/>'s tap-cost check
///   against creatures with summoning sickness (CR 302.1). Enforcement
///   happens upstream at activation-validation time — same posture as
///   Elvish Archdruid / Llanowar Elves.
/// - <b>Granted-defender creatures</b>: defenders granted by a continuous
///   effect (rather than printed) are recognised only insofar as the
///   granting effect attaches a "Defender" <see cref="KeywordAbility"/>
///   marker; a future layer system can plug granted keywords into
///   <see cref="CombatAbilities.HasDefender"/> without changing this
///   factory (same posture as the rest of the combat-keyword lookups).
/// </summary>
[CardName("Overgrown Battlement")]
public static class OvergrownBattlementFactory
{
    public const string CardName = "Overgrown Battlement";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 0;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Overgrown Battlement. The Defender keyword marker and the
    /// {T} defender-tribal mana ability are always wired; the mana
    /// generator reads the controller's defender count at each activation.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Wall });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block-legality AND so
        // the Battlement counts itself toward its own mana ability.
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // ----------------------------------------------------------------
        // {T}: Add {G} for each creature you control with defender
        // (CR 605.1 — mana ability, no stack; CR 107.1b — X resolves at
        // the moment the effect determines it).
        //
        // X-count semantics:
        //   - Counted at activation (mana abilities resolve atomically).
        //   - INCLUDES the Battlement itself (no "other" qualifier).
        //   - Controller's battlefield only (CR 109.5), creatures with
        //     defender only (CombatAbilities.HasDefender marker).
        //
        // Wired via the Func<ManaCost> generator overload so the count is
        // re-read at each activation (Elvish Archdruid shape).
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerator: () =>
            {
                var controller = card.Controller ?? owner;
                int defenderCount = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Count(CombatAbilities.HasDefender);

                if (defenderCount <= 0) return ManaCost.Zero;

                // Build "{G}{G}...{G}" with defenderCount green pips.
                return ManaCost.Parse(string.Concat(Enumerable.Repeat("{G}", defenderCount)));
            },
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
