using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cabal Coffers (Torment / reprints).
///
/// Land. Oracle text:
///   "{2}, {T}: Add {B} for each Swamp you control."
///
/// Scryfall-confirmed type line: Land (no basic supertype, no subtypes).
/// Cabal Coffers is NOT a Swamp itself — it does not count toward its own
/// ability (CR 305.6 — the Swamp subtype is a printed land subtype; Cabal
/// Coffers has none).
///
/// ## Implemented (v1)
/// - Land identity (non-basic, no supertypes, no subtypes).
/// - <b>NO basic mana ability</b>: Cabal Coffers has NO "{T}: Add {C}" or
///   similar tap-alone ability. The only ability is the {2},{T} ability
///   described below. Tests assert the absence of a basic-mana ability.
/// - <b>{2}, {T}: Add {B} for each Swamp you control.</b>
///   Modelled as a <see cref="ManaAbility"/> using the dynamic
///   <c>Func&lt;ManaCost&gt; manaGenerator</c> overload. The {2} payment is
///   inlined at the start of the generator lambda — this is correct because
///   CR 605.1 treats cost payment and mana production as a single atomic
///   step for mana abilities (no stack). The <c>canActivateCheck</c> gates
///   on both untapped state and affordability of {2}, ensuring the lambda
///   is only entered when the full cost is payable. Execution order inside
///   <see cref="ManaAbility.Activate"/>:
///   <list type="number">
///     <item><c>CanActivate()</c> — verifies untapped + can afford {2}.</item>
///     <item><c>_manaGenerator()</c> — pays {2} inline, counts Swamps,
///       returns N × {B}.</item>
///     <item>Taps the land (standard {T} cost).</item>
///   </list>
///   The mana payment happens before the tap, which is an ordering
///   artefact of the <see cref="ManaAbility"/> Activate() implementation;
///   both are part of the same activation cost per CR 602.2a, so the
///   observable game state after activation is identical.
///
/// ## Swamp count (CR 305.6)
/// Delegates to <see cref="DefileFactory.CountSwamps"/>: counts permanents
/// the controller controls that have the <see cref="CardSubtype.Swamp"/>
/// subtype. Snow-covered Swamps count; Cabal Coffers itself does not (no
/// Swamp subtype).
///
/// ## Zero-Swamp activation (CR 605.1c)
/// Activating a mana ability is legal even when the net mana produced is
/// zero. With 0 Swamps the generator returns <see cref="ManaCost.Zero"/>
/// and the pool gains nothing. The {2} was still paid and the land is still
/// tapped. This is intentional and correct.
///
/// ## Deferred (v1 gaps)
/// - <b>N × {B} as concatenated string</b>: <see cref="ManaCost"/> has no
///   native "N black pips" constructor; <see cref="BuildBlackMana"/> builds
///   a string <c>"{B}{B}…"</c> and parses it. Functionally correct; a
///   future <c>ManaCost.BlackMana(n)</c> factory method would be tidier.
/// - <b>Bot policy</b>: the MonoBlack Midrange bot will discover this via
///   the existing <see cref="Majik.Bot.Decks.MonoBlackMidrangeDeck"/> deck
///   list; EV scoring for the {2},{T} activation is inherited from the
///   generic "add mana" bot policy (ManaAbility activation).
/// </summary>
[CardName("Cabal Coffers")]
public static class CabalCoffersFactory
{
    public const string CardName = "Cabal Coffers";

    private static readonly ManaCost TapAdditionalCost = ManaCost.Parse("2");

    /// <summary>
    /// Construct a Cabal Coffers owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}: Add {B} for each Swamp you control.
        //
        // CR 605.1 — mana ability (produces mana, no target, doesn't use
        // the stack). The {2} is an additional mana cost paid as part of
        // the activation alongside the {T}.
        //
        // Implementation approach:
        //   We use the Func<ManaCost> overload so the Swamp count is
        //   evaluated at activation time rather than factory-build time.
        //   The {2} payment is inlined inside the generator because the
        //   existing ManaAbility API has no Func<ManaCost> + additionalCostPayer
        //   constructor. The canActivateCheck guards both conditions so the
        //   lambda is only reached when the full cost ({2} + {T}) is payable.
        //
        //   canActivateCheck:
        //     1. Land is not already tapped (standard {T} gate).
        //     2. Controller's mana pool can pay {2}
        //        (owner.ManaPool.CanPay — does NOT consume mana; read-only).
        //
        //   manaGenerator lambda (runs inside ManaAbility.Activate before tap):
        //     1. owner.PayMana({2}) — drains 2 generic mana from pool.
        //     2. Count Swamps the owner controls (DefileFactory.CountSwamps).
        //     3. Return N × {B} (ManaCost.Zero when N == 0).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () =>
            {
                // Pay the {2} portion of the activation cost.
                owner.PayMana(TapAdditionalCost);
                // Count Swamps the controller currently controls.
                var n = DefileFactory.CountSwamps(owner);
                return BuildBlackMana(n);
            },
            canActivateCheck: () =>
                !land.IsTapped
                && owner.ManaPool.CanPay(TapAdditionalCost)));

        return land;
    }

    /// <summary>
    /// Count how many Swamps <paramref name="controller"/> currently
    /// controls. Delegates to <see cref="DefileFactory.CountSwamps"/>
    /// (CR 305.6 — Swamp subtype on a land the controller controls).
    /// Exposed as a public helper for tests and bot policies.
    /// Returns 0 for null input.
    /// </summary>
    public static int CountSwamps(Player controller) =>
        DefileFactory.CountSwamps(controller);

    /// <summary>
    /// Build a <see cref="ManaCost"/> representing <paramref name="n"/>
    /// black mana pips. Returns <see cref="ManaCost.Zero"/> when
    /// <paramref name="n"/> is ≤ 0.
    /// </summary>
    internal static ManaCost BuildBlackMana(int n)
    {
        if (n <= 0) return ManaCost.Zero;
        return ManaCost.Parse(string.Concat(Enumerable.Repeat("{B}", n)));
    }
}
