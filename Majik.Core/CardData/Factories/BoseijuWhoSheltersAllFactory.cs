using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
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
/// ## Deferred (v1 gaps)
/// - <b>"If that mana is spent on an instant or sorcery spell, that spell
///   can't be countered."</b> — the uncounterable rider requires tagging the
///   produced {C} with its provenance, detecting at cast time that one of
///   Boseiju's mana units paid a pip on an instant/sorcery spell, flagging
///   that spell object, and gating counter-spells in
///   <see cref="Majik.Core.Services.StackResolver"/>. This is the SAME
///   deferral the Cavern of Souls / Delighted Halfling "that spell can't be
///   countered" riders carry (see <see cref="CavernOfSoulsFactory"/>) — it
///   waits on per-slot mana provenance + a cast-time uncounterable flag.
///   Until then the rider is documented but not machine-enforced; the mana
///   itself is correct.
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
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("C"),
            canActivateCheck: () => !land.IsTapped && owner.LifeTotal > LifeCost,
            additionalCostPayer: p => p.LoseLife(LifeCost)));

        return land;
    }
}
