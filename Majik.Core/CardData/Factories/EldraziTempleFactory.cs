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
/// ## Spend-restriction (v1 data, payment-gate deferred)
/// - <b>"Spend this mana only to cast Eldrazi spells or activate
///   abilities of Eldrazi"</b>: the {C}{C} <see cref="ManaAbility"/>
///   stamps a <see cref="Majik.Core.Mana.SpendRestriction"/> with the
///   predicate <c>spell => spell.Card.HasSubtype(CardSubtype.Eldrazi)</c>.
///   The {T}: Add {C} ability is <b>unrestricted</b> (matches the
///   printed oracle — only the second mana ability carries the rider).
///   The "or activated abilities of Eldrazi" half of the restriction is
///   not modelled here (mana paid into ability costs goes through a
///   separate path that doesn't surface an <c>ISpell</c>); the predicate
///   is conservative — spell-side only.
///
///   <b>Payment-gate enforcement</b> (filtering tagged pool entries
///   when paying a non-Eldrazi cost) is deferred until
///   <see cref="ManaPool"/> grows per-slot tags — today the pool stores
///   bucketed colour/generic counts only. Same posture as Cavern of
///   Souls' rider (see <see cref="CavernOfSoulsFactory"/> xmldoc); both
///   unlock together when the resolver consumes the tag.
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
        // stamping the Eldrazi-subtype predicate on the generated mana.
        // Payment-gate side is still deferred (see class xmldoc) — the
        // rider is observational metadata on the ability until ManaPool
        // grows per-slot tag awareness.
        // ----------------------------------------------------------------
        var eldraziRestriction = new SpendRestriction(
            "Eldrazi spell or ability",
            spell => spell.Card.HasSubtype(CardSubtype.Eldrazi));

        land.AddAbility(new ManaAbility(
            land, owner, ManaCost.Parse("CC"),
            canActivateCheck: null,
            spendRestriction: eldraziRestriction));

        return land;
    }
}
