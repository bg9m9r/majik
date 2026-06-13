using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boseiju, Who Shelters All (Champions of Kamigawa).
///
/// Legendary Land. Oracle text (verified against Scryfall):
///   "Boseiju, Who Shelters All enters tapped.
///    {T}, Pay 2 life: Add {C}. If that mana is spent on an instant or
///    sorcery spell, that spell can't be countered."
///
/// Same legendary-land family as Boseiju, Who Endures
/// (<see cref="BoseijuFactory"/>); this factory reuses the JSON-identity +
/// in-code mana-ability scaffolding pattern, with the pay-life mana shape
/// borrowed from the Horizon Canopy painless-dual cycle
/// (<see cref="Majik.Core.CardData.HorizonLandBinder.AttachPayLifeMana"/>).
///
/// ## Implemented (v1)
/// - <b>Identity</b> — Legendary Land, loaded from
///   <c>Majik.Core/CardData/Cards/boseiju-who-shelters-all.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>"{T}, Pay 2 life: Add {C}"</b> — a <see cref="ManaAbility"/> with an
///   additional non-mana activation cost (lose 2 life), built directly here
///   because the JSON <see cref="ManaAbilityDefinition"/> schema only models
///   an extra <i>mana</i> cost, not a life cost. Same additional-cost overload
///   the Horizon Canopy cycle's "Pay 1 life" lands use, scaled to 2 life and
///   producing {C} (colorless — rolls into the generic bucket via
///   <see cref="ManaCost.Parse"/>, see ManaCost.cs case 'C'). The activation
///   gate enforces CR 119.4 ("you can't pay life you don't have"): the
///   controller's life total must be strictly greater than 2.
///   CR 605.1a — mana abilities don't use the stack.
///
/// ## Implemented elsewhere
/// - <b>Enters-tapped (CR 614.1c)</b> — the unconditional
///   "Boseiju, Who Shelters All enters tapped." replacement is applied on the
///   production load path by
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle
///   text. This factory builds the land without the replacement (no
///   <see cref="Majik.Core.Effects.ReplacementBus"/> available on the
///   shape-only path), matching the Abraded Bluffs / Refuge / Temple cycle
///   posture — the binder owns the replacement so it isn't double-registered.
///
/// ## Implemented (v1 — uncounterable rider)
/// - <b>"If that mana is spent on an instant or sorcery spell, that spell
///   can't be countered."</b> (CR 701.5b / 106.4) — the {C} ability carries a
///   <see cref="Majik.Core.Abilities.ManaAbility.ProvenanceReaction"/>. The
///   produced {C} rides a per-slot
///   <see cref="Majik.Core.Mana.ManaProvenanceSlot"/> in the colorless
///   dimension; when <see cref="Majik.Core.Costs.ManaPaymentResolver"/>
///   consumes that unit to pay a cost, it fires the reaction with the object
///   the mana was spent on. If that object is an instant or sorcery card, the
///   reaction stamps <see cref="Cards.Card.PendingCastUncounterable"/>, which
///   <c>SpellCastFlow.StampSpellAndCardSentinels</c> reads onto
///   <see cref="Majik.Core.Spells.ISpell.CannotBeCountered"/> (then clears) the
///   moment the spell is constructed — strictly per-pip / per-spell, the same
///   slot-provenance seam as Arena of Glory's exert→haste rider. The Cavern of
///   Souls / Delighted Halfling "that spell can't be countered" riders can now
///   reuse this same mechanism.
/// </summary>
[CardName("Boseiju, Who Shelters All")]
public static class BoseijuWhoSheltersAllFactory
{
    public const string CardName = "Boseiju, Who Shelters All";

    /// <summary>Life paid as part of the mana ability's activation cost.</summary>
    public const int LifeCost = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("boseiju-who-shelters-all");

    /// <summary>
    /// Construct Boseiju, Who Shelters All with its "{T}, Pay 2 life: Add {C}"
    /// mana ability wired. Enters-tapped is applied by the binder layer on the
    /// production load path (see class xmldoc).
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity (Legendary Land) comes from the JSON definition.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}, Pay 2 life: Add {C}.
        // CR 605.1a — mana ability, doesn't use the stack. The extra cost
        // (lose 2 life) is part of activation, paid after tapping. CR 119.4 —
        // the controller must have more than 2 life to pay it.
        // {C} parses as +1 generic (ManaCost.Parse, case 'C').
        // ----------------------------------------------------------------
        var mana = new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("C"),
            canActivateCheck: () => !land.IsTapped && owner.LifeTotal > LifeCost,
            additionalCostPayer: p => p.LoseLife(LifeCost));

        // ----------------------------------------------------------------
        // "If that mana is spent on an instant or sorcery spell, that spell
        // can't be countered." (CR 701.5b / 106.4).
        //
        // The {C} is slot-tagged with this ability as its provenance source
        // (ManaProvenanceSlot, deferral #1). When the payment resolver consumes
        // one of those colorless units to pay a cost, it fires this reaction
        // with the object the mana was spent on (the cast ICard, or null for a
        // non-spell context). If that object is an instant or sorcery card, we
        // stamp a pay-time uncounterable flag on the card. SpellCastFlow reads
        // that flag onto Spell.CannotBeCountered (and clears it) right after it
        // constructs the spell — payment happens at CR 601.2h, the spell is
        // built immediately after, so the stamp is live at that point.
        //
        // Strictly per-pip / per-spell (mirrors Arena of Glory's exert→haste
        // rider): the flag attaches to the exact spell this mana paid for, not
        // "the first spell after Boseiju tapped". A creature spell (or a
        // non-spell ability cost) paid with this mana is NOT flagged — the
        // printed rider is instant/sorcery-only.
        // ----------------------------------------------------------------
        mana.ProvenanceReaction = MarkUncounterableIfInstantOrSorcery;

        land.AddAbility(mana);

        return land;
    }

    /// <summary>
    /// CR 701.5b — Boseiju's provenance reaction. When one of Boseiju's {C}
    /// units is spent on an instant or sorcery <i>spell</i>, stamp the
    /// pay-time uncounterable flag on the underlying card so the cast flow
    /// marks the resulting spell <see cref="Majik.Core.Spells.ISpell.CannotBeCountered"/>.
    /// No-op for a creature/other spell or a non-spell (ability-cost) context
    /// (<paramref name="spentOn"/> is null or not an instant/sorcery card).
    /// </summary>
    private static void MarkUncounterableIfInstantOrSorcery(ICard? spentOn)
    {
        if (spentOn is not Card card) return;
        if (!card.HasType(CardType.Instant) && !card.HasType(CardType.Sorcery)) return;
        card.MarkPendingCastUncounterable();
    }
}
