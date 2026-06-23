using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unknown Shores (Theros + reprints). Oracle text
/// (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// <para>
/// The Land shell (identity / owner / controller) and the vanilla
/// <c>{T}: Add {C}</c> mana ability are declared declaratively in
/// <c>Majik.Core/CardData/Cards/unknown-shores.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="SurvivorsEncampmentFactory"/> / <see cref="CityOfBrassFactory"/>.
/// The five any-colour abilities are attached on top in C# because the
/// data-only <see cref="ManaAbilityDefinition"/> schema carries only a
/// <c>Produces</c> string — it can express neither the five-colour any-colour
/// fan-out nor the <c>{1}</c> generic additional activation cost. The JSON
/// therefore declares only the {C} ability; this factory adds the rest.
/// </para>
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — non-basic, no subtype, empty mana cost (JSON).
/// - <b>{T}: Add {C}</b> — vanilla mana ability (CR 605.1, no stack)
///   declared in JSON. {C} folds into the generic bucket per
///   <c>ManaCost.Parse</c> (same posture as Survivors' Encampment / Rogue's
///   Passage).
/// - <b>{1}, {T}: Add one mana of any color</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG), the same any-colour
///   fan-out as <see cref="ManaConfluenceFactory"/> / City of Brass, but with
///   a <c>{1}</c> generic mana additional cost (CR 605.1; CR 601.2f — paying a
///   mana cost is part of paying the activation cost) instead of the
///   pain/life rider. Each ability is built via the
///   fixed-mana + <c>canActivateCheck</c> + <c>additionalCostPayer</c> overload
///   of <see cref="ManaAbility"/> — exactly the Cabal Coffers / Pentad Prism
///   shape (<see cref="Majik.Core.Tests.Abilities.ManaAbilityDynamicCostTests"/>):
///     - <c>canActivateCheck</c>: land untapped AND the controller's mana pool
///       can pay <c>{1}</c> (CR 601.2f — an unpayable cost makes activation
///       illegal). The {1} is generic, so any one mana in the pool satisfies it.
///     - <c>additionalCostPayer</c>: <c>controller.PayMana({1})</c> after the
///       {T} tap pays the activation. The mana picker chooses whichever colour
///       is needed when paying spell costs by picking the matching ability slot.
///
/// ## Prod note — this factory is TEST-ONLY
/// - Lands are never routed through their <c>[CardName]</c> factory in prod —
///   they build through the binder chain. This factory exists for the contract
///   / dispatcher tests and the focused behaviour test; per-card prod behaviour
///   for any-colour modes is bound by the oracle binders.
/// </summary>
[CardName("Unknown Shores")]
public static class UnknownShoresFactory
{
    public const string CardName = "Unknown Shores";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("unknown-shores");

    /// <summary>The {1} generic additional cost on the any-colour mode.</summary>
    private static readonly ManaCost GenericOne = ManaCost.Parse("1");

    /// <summary>Construct Unknown Shores owned and controlled by
    /// <paramref name="owner"/> with the {C} mana ability (from JSON) and all
    /// five any-colour ({1}, {T}) mana abilities attached.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Land + {T}: Add {C}, materialized from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {1}, {T}: Add one mana of any color.
        //   Five ManaAbility instances (one per WUBRG). Each carries:
        //     - canActivateCheck: land untapped AND the {1} is affordable
        //       (CR 601.2f — an unpayable cost makes activation illegal).
        //     - additionalCostPayer: pay {1} generic (CR 605.1 — part of the
        //       activation cost) after the {T} tap.
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var mana = ManaCost.Parse(color);
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: mana,
                canActivateCheck: () =>
                {
                    if (land.IsTapped) return false;
                    var controller = land.Controller ?? owner;
                    return controller.ManaPool.CanPay(GenericOne);
                },
                additionalCostPayer: controller => controller.PayMana(GenericOne)));
        }

        return land;
    }
}
