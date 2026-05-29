using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dimir Signet (Ravnica "signet" mana-rock cycle).
///
/// Oracle text (verified against Scryfall):
/// <code>
/// Artifact {2}.
/// {1}, {T}: Add {U}{B}.
/// </code>
///
/// ## Why a programmatic factory (not a JSON definition)
/// The signet's mana ability carries a <b>{1} additional mana cost</b>
/// paid into the same activation as the {T} tap. The JSON
/// <c>ManaAbilityDefinition</c> schema only models <c>produces</c> (the
/// plain <c>{T}: Add &lt;X&gt;</c> shape) — it has no additional-cost
/// surface — so a JSON-backed thin wrapper cannot express the signet.
/// This factory therefore builds the card in code, mirroring
/// <see cref="FilterLandCycleFactory"/>'s proven additional-cost
/// <see cref="ManaAbility"/> pattern (the only other "pay {1}, {T}: add
/// coloured mana" family in the pool). The {2} artifact identity matches
/// <see cref="TalismanCycleFactory"/>.
///
/// ## Implemented
/// - Artifact identity ({2}, owner / controller wiring) + printed name.
/// - <b>{1}, {T}: Add {U}{B}</b> — a single <see cref="ManaAbility"/>
///   built via the additional-cost overload:
///     - <c>manaGenerated</c> = <c>{U}{B}</c> (one blue + one black pip,
///       produced <i>together</i> — not a modal "or", unlike the filter
///       lands). CR 605.1 — mana ability, never on the stack.
///     - <c>canActivateCheck</c> = <c>!IsTapped &amp;&amp;
///       controller.ManaPool.CanPay({1})</c> — gates on both the {T}
///       (untap) half and the {1}-affordability half so activation never
///       taps the signet only to no-op on the payment.
///     - <c>additionalCostPayer</c> = <c>controller.PayMana({1})</c> —
///       the {1} deducted from the mana pool, paid atomically with the
///       tap (the engine taps in <see cref="ManaAbility.Activate"/>).
///
/// Net mana: pay {1}, add {U}{B} — a +1 mana gain plus colour fixing, the
/// signature signet curve.
///
/// ## Deferred (v1 gaps — same posture as the filter lands)
/// - Activation requires {1} already in the mana pool; the engine does
///   not auto-tap other sources to feed the signet cost (no look-ahead
///   mana planner). Identical to every other additional-mana-cost
///   activated/mana ability (filter lands, Mind Stone's draw cost, …).
/// </summary>
[CardName("Dimir Signet")]
public static class DimirSignetFactory
{
    /// <summary>Printed mana cost shared by the whole signet cycle.</summary>
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Dimir Signet owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var signet = new Artifact("Dimir Signet", PrintedManaCost);
        signet.SetOwner(owner);
        signet.SetController(owner);

        // {1}, {T}: Add {U}{B}. Built via the additional-cost overload of
        // ManaAbility (same shape as FilterLandCycleFactory): the {T} tap is
        // paid by the default tap-as-cost path; {U}{B} is the produced mana;
        // additionalCostPayer deducts {1} from the controller's pool. The
        // canActivateCheck gates on both the untap state and {1}
        // affordability (CR 605.1 — the {1} is part of activation, paid
        // atomically with the tap; without the affordability check
        // activation would tap then no-op on payment).
        var output = ManaCost.Parse("UB");
        var oneGeneric = ManaCost.Parse("1");

        signet.AddAbility(new ManaAbility(
            source: signet,
            controller: owner,
            manaGenerated: output,
            canActivateCheck: () => !signet.IsTapped && owner.ManaPool.CanPay(oneGeneric),
            additionalCostPayer: p => p.PayMana(oneGeneric)));

        return signet;
    }
}
