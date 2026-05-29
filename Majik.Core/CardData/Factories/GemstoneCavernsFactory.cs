using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gemstone Caverns (Coldsnap).
///
/// Legendary Land. Oracle text (verified against Scryfall 2026-05-29):
///   "If this card is in your opening hand and you're not the starting
///    player, you may begin the game with Gemstone Caverns on the
///    battlefield with a luck counter on it. If you do, exile a card from
///    your hand.
///    {T}: Add {C}. If Gemstone Caverns has a luck counter on it, instead
///    add one mana of any color."
///
/// ## Why a hand-coded factory (not a JSON definition)
///
/// The data-driven <see cref="Majik.Core.CardData.Definitions.ManaAbilityDefinition"/>
/// schema only carries a fixed <c>produces</c> colour — it has no field for a
/// counter-conditional "instead add one mana of any color" replacement
/// (CR 605.1) nor for the opening-hand luck-counter start clause. A JSON-only
/// definition would silently drop both riders. So this card follows the
/// proven hand-coded land analogues — Gemstone Mine (counter-gated mana,
/// <see cref="GemstoneMineFactory"/>), Cavern of Souls ({C} + five-colour
/// any-colour, <see cref="CavernOfSoulsFactory"/>), and the Verge cycle
/// (<see cref="ThornspireVergeFactory"/>, <c>canActivateCheck</c>-gated
/// colour abilities).
///
/// ## Implemented (v1)
/// - Legendary nonbasic Land with correct identity / owner / controller.
/// - <b>{T}: Add {C}</b> — a single <see cref="ManaAbility"/> producing {C},
///   gated to activate ONLY while Gemstone Caverns has no luck counter on it.
/// - <b>"If Gemstone Caverns has a luck counter on it, instead add one mana
///   of any color"</b> — modelled as five <see cref="ManaAbility"/> instances
///   (one per WUBRG), each gated to activate ONLY while a luck counter is
///   present (CR 605.1; same five-colour "any color" pattern as Cavern of
///   Souls / Gemstone Mine / City of Brass). The {C} and the any-colour set
///   are mutually exclusive via the luck-counter predicate — exactly one set
///   is live at a time, faithfully modelling the printed "instead" replacement
///   without a single modal-colour ability (which the engine does not have).
///
/// ## Deferred (v1 gaps)
/// - <b>Opening-hand luck-counter start</b>: "If this card is in your opening
///   hand and you're not the starting player, you may begin the game with
///   Gemstone Caverns on the battlefield with a luck counter on it. If you do,
///   exile a card from your hand." This is a CR 103.5 opening-hand action —
///   the same family as the Leyline cycle's
///   <see cref="Majik.Core.Game.OpeningHandLeylineAlternativeCost"/> subscriber,
///   which already consumes <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
///   Gemstone Caverns is the documented future subscriber for that event, but
///   it needs three things the Leyline subscriber does not model: (1) the
///   "you're not the starting player" gate, (2) putting the land in with a
///   luck counter (vs. plain put-onto-battlefield), and (3) the
///   "exile a card from your hand" additional cost. Building that shared
///   start-of-game-action surface is a distinct engine slice. Until it lands,
///   this factory ships the gameplay-relevant mana ability fully and flags the
///   start clause with the marker keyword
///   <see cref="OpeningHandStartKeyword"/> so the future subscriber can
///   discover Gemstone Caverns the same keyword-driven way it discovers
///   Leylines. No half-built subscriber wiring is shipped here.
/// - <b>Single modal-colour mana ability</b>: "add one mana of any color" is
///   five separate <see cref="ManaAbility"/> instances — same posture as
///   Cavern of Souls / Gemstone Mine / City of Brass.
/// </summary>
[CardName("Gemstone Caverns")]
public static class GemstoneCavernsFactory
{
    public const string CardName = "Gemstone Caverns";

    /// <summary>Marker keyword flagging Gemstone Caverns as an opening-hand
    /// luck-counter start candidate (CR 103.5). The deferred start-of-game
    /// subscriber discovers tagged cards the same way
    /// <see cref="Majik.Core.Game.OpeningHandLeylineAlternativeCost"/>
    /// discovers Leylines. See class xmldoc for the deferral.</summary>
    public const string OpeningHandStartKeyword = "GemstoneCavernsOpeningHandStart";

    /// <summary>
    /// Construct Gemstone Caverns owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Legendary nonbasic land, no printed subtype.
        var land = new Land(CardName, supertypes: new[] { CardSupertype.Legendary }, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Opening-hand luck-counter start clause (CR 103.5) — deferred.
        // Flagged with a marker keyword for the future shared start-of-game
        // subscriber (sibling of OpeningHandLeylineAlternativeCost). See
        // class xmldoc for why the subscriber itself is not built here.
        // ----------------------------------------------------------------
        land.AddAbility(new KeywordAbility(OpeningHandStartKeyword, land, owner));

        // ----------------------------------------------------------------
        // {T}: Add {C}.
        // CR 605.1 — mana ability; does not use the stack. Active ONLY while
        // there is no luck counter on Gemstone Caverns; with a luck counter
        // present the printed "instead add one mana of any color" replacement
        // takes over (the WUBRG set below). {C} rolls into the generic
        // bucket per ManaCost.Parse — same posture as Cavern of Souls.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("C"),
            canActivateCheck: () => !land.IsTapped
                                    && land.Zone == ZoneType.Battlefield
                                    && !HasLuckCounter(land)));

        // ----------------------------------------------------------------
        // "If Gemstone Caverns has a luck counter on it, instead add one mana
        //  of any color."
        // CR 605.1 — mana ability. Five ManaAbility instances (one per
        // WUBRG); each active ONLY while a luck counter is present. The
        // source-picker chooses whichever colour a cost needs at payment
        // time (same any-colour pattern as Cavern of Souls / Gemstone Mine).
        // Mutually exclusive with the {C} ability via the luck-counter gate,
        // faithfully modelling the printed "instead" replacement.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !land.IsTapped
                                        && land.Zone == ZoneType.Battlefield
                                        && HasLuckCounter(land)));
        }

        return land;
    }

    private static bool HasLuckCounter(Land land) =>
        land.Counters.Count(CounterType.Luck) >= 1;
}
