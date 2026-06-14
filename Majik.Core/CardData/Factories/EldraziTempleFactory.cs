using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldrazi Temple (Rise of the Eldrazi).
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    {T}: Add {C}{C}. Spend this mana only to cast Eldrazi spells or
///    activate abilities of Eldrazi."
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes / supertypes — non-basic).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/>. {C} folds
///   into the generic bucket per <see cref="ManaCost.Parse"/>
///   (see ManaCost.cs:170).
/// - <b>{T}: Add {C}{C}</b> — second <see cref="ManaAbility"/> producing
///   two generic mana. Same factory-time shape as the {C} ability;
///   distinguishable from {T}: Add {C} by its <c>ManaGenerated.Generic
///   == 2</c>.
///
/// ## Spend-restriction (v1 — payment-gate ENFORCED)
/// - <b>"Spend this mana only to cast Eldrazi spells or activate
///   abilities of Eldrazi"</b>: the {C}{C} <see cref="ManaAbility"/>
///   stamps a <see cref="Majik.Core.Mana.SpendRestriction"/> with the
///   predicate <c>spell => spell.Card.HasSubtype(CardSubtype.Eldrazi)</c>.
///   The {T}: Add {C} ability is <b>unrestricted</b> (matches the
///   printed oracle — only the second mana ability carries the rider).
///   The "or activate abilities of Eldrazi" half is now modelled too: the
///   restriction carries an ability-spend predicate
///   (<c>ctx => ctx.SourceHasSubtype(Eldrazi)</c>) consulted on the
///   ability-cost payment path (<see cref="Majik.Core.Costs.ManaCostCost"/> /
///   <see cref="Majik.Core.Costs.CostPayment"/> via the
///   <see cref="Majik.Core.Mana.ManaSpendContext"/>), so the {C}{C} pays an
///   Eldrazi source's activated ability but NOT a non-Eldrazi source's.
///
///   <b>Payment-gate enforcement</b> is now live for this COLORLESS ({C}{C})
///   mana too: the per-slot provenance ledger tracks a {C} unit in its own
///   <see cref="Majik.Core.ValueObjects.ManaColor.Colorless"/> dimension (CR
///   107.4c), and <see cref="Majik.Core.Costs.ManaPaymentResolver"/> withholds
///   a restricted colorless unit from the bucketed spend when the cast spell
///   doesn't satisfy the rider — so the {C}{C} can only pay an Eldrazi spell.
///   (The "or activated abilities of Eldrazi" half remains spell-only.)
/// </summary>
[CardName("Eldrazi Temple")]
public static class EldraziTempleFactory
{
    public const string CardName = "Eldrazi Temple";

    /// <summary>
    /// Construct an Eldrazi Temple owned and controlled by
    /// <paramref name="owner"/>. Wires both <see cref="ManaAbility"/>
    /// instances (the {T}: Add {C} and the {T}: Add {C}{C}).
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} folds into
        // the generic bucket per ManaCost.Parse (see ManaCost.cs:170).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}: Add {C}{C}. Spend this mana only to cast Eldrazi spells
        //   or activate abilities of Eldrazi.
        // Second ManaAbility producing 2 generic, with a SpendRestriction
        // stamping the Eldrazi-subtype predicate on the generated mana. The
        // spend-restriction gate is live for COLORLESS ({C}{C}) mana via the
        // ManaColor.Colorless provenance slot dimension (CR 107.4c): the
        // resolver withholds this restricted {C}{C} from a non-Eldrazi spend
        // (see class xmldoc + SpendRestrictionProvenanceGateTests).
        // ----------------------------------------------------------------
        // CR 106.4 — "Spend this mana only to cast Eldrazi spells or activate
        // abilities of Eldrazi." Both halves now enforced: the spell predicate
        // (Eldrazi-subtype spell) rides ManaPaymentResolver; the ability
        // predicate (the activated ability's source is an Eldrazi) rides the
        // ability-cost spend context.
        var eldraziRestriction = new SpendRestriction(
            "Eldrazi spell or ability",
            spell => spell.Card.HasSubtype(CardSubtype.Eldrazi),
            ctx => ctx.SourceHasSubtype(CardSubtype.Eldrazi));

        land.AddAbility(new ManaAbility(
            land, owner, ManaCost.Parse("CC"),
            canActivateCheck: null,
            spendRestriction: eldraziRestriction));

        return land;
    }
}
