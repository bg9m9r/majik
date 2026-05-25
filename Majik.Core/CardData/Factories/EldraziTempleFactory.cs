using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// ## Deferred (v1 gaps)
/// - <b>"Spend this mana only to cast Eldrazi spells or activate
///   abilities of Eldrazi"</b>: the engine has no spend-restriction tag
///   surface on mana entries today. <see cref="ManaPool"/> stores bare
///   colour / generic counts without provenance, and
///   <see cref="Majik.Core.Costs.ManaPaymentResolver"/> has no predicate
///   hook to gate spending. Same deferral as Cavern of Souls' "spend
///   only on creature spell of the chosen type" rider (see
///   <see cref="CavernOfSoulsFactory"/> xmldoc). When a
///   <c>ManaProvenanceLedger</c> + spend-restriction predicate pair
///   lands, the {C}{C} ability swaps its third <see cref="ManaAbility"/>
///   ctor arg for the predicate-tagged variant; the Eldrazi-subtype
///   predicate is <c>spell => spell.Card.HasSubtype(CardSubtype.Eldrazi)</c>.
///   v1 produces the mana untagged — observationally equivalent to
///   "always Eldrazi" when the controller only spends it on Eldrazi
///   spells (which is the intended use case for this card in any deck
///   that runs it).
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
        // Second ManaAbility producing 2 generic. The spend-restriction
        // rider is deferred (see class xmldoc) — engine ships with the
        // raw 2-generic production untagged.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("CC")));

        return land;
    }
}
