using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mirrodin's Core (Fifth Dawn).
///
/// Land. Oracle text (Scryfall, verified 2026-06-02):
///   "{T}: Add {C}.
///    {T}: Put a charge counter on this land.
///    {T}, Remove a charge counter from this land: Add one mana of any
///    color."
///
/// The charge-storing five-colour fixing land: tap for {C} every turn, or
/// "store" untapped turns as charge counters that later cash out as one
/// off-colour pip each. Same charge-counter / "remove a charge counter: any
/// colour" suite as Vivid Crag (<see cref="VividCragFactory"/>) and Tendo Ice
/// Bridge, but Mirrodin's Core enters untapped with NO counters (no ETB
/// trigger) and its base ability produces colourless {C} rather than a fixed
/// colour, and it can ADD counters via its own {T} activated ability.
///
/// ## Implementation
///
/// Card identity (Land, no supertype / subtype) is loaded from
/// <c>Majik.Core/CardData/Cards/mirrodins-core.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven card cycle
/// (same posture as <see cref="VividCragFactory"/>).
///
/// ## {T}: Add {C} (CR 605.1)
///
/// One plain <see cref="ManaAbility"/> producing {C} (colourless) with the
/// standard tap-as-cost overload (no counter cost). Gated on untapped + on the
/// battlefield. This consumes no charge counter.
///
/// ## {T}: Put a charge counter on this land (CR 122.1 / CR 602)
///
/// One non-mana <see cref="ActivatedAbility"/> whose only cost is
/// <see cref="AdditionalCost.Tap"/>. The effect places one
/// <see cref="CounterType.Charge"/> counter — same shape as
/// <see cref="RatchetBombFactory"/>'s "{T}: Put a charge counter". This is NOT
/// a mana ability (it produces no mana, so CR 605.1a excludes it); it uses the
/// stack.
///
/// ## {T}, Remove a charge counter from this land: Add one mana of any color
/// (CR 605.1)
///
/// Five <see cref="ManaAbility"/> instances (one per WUBRG) — the same modal
/// colour shape as Vivid Crag / Sphere of the Suns / Pentad Prism. The
/// activator picks a colour by picking the matching ability slot, so no
/// separate colour prompt is needed (CR 605.1 — mana abilities don't use the
/// stack). The cost is {T} PLUS "remove a charge counter", so the standard
/// tap-as-cost overload is used (<c>tapsAsCost</c> defaults to true). Each
/// slot is gated on:
///   (1) the land is still on the battlefield, AND
///   (2) the land is untapped (the printed {T} cost), AND
///   (3) the land has at least one charge counter to remove
///       (CR 605.3a — the cost must be payable).
/// The <c>additionalCostPayer</c> removes one charge counter inline
/// (CR 121.5 / CR 602.1 — paid up front in the same atomic step as mana
/// production). Because every ability here costs {T}, only ONE of them can be
/// activated per untap step.
///
/// ## Enters untapped, no counters
///
/// Unlike Vivid Crag, Mirrodin's Core has no ETB clause — it enters untapped
/// with zero charge counters, so there is no ETB trigger / replacement to
/// model.
/// </summary>
[CardName("Mirrodin's Core")]
public static class MirrodinsCoreFactory
{
    public const string CardName = "Mirrodin's Core";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mirrodins-core");

    /// <summary>
    /// Construct Mirrodin's Core. Enters untapped with no charge counters.
    /// Attaches the base {C} mana ability, the non-mana "{T}: Put a charge
    /// counter" activated ability, and the five WUBRG any-colour mana
    /// abilities. Suitable for shape / <see cref="NamedCardFactory"/> dispatch
    /// tests.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}. (CR 605.1 — mana ability, no stack.)
        //
        // The base colourless producer. Standard tap-as-cost overload; NO
        // charge counter is consumed. Gated on untapped + on the battlefield.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("C"),
            canActivateCheck: () => land.Zone == ZoneType.Battlefield
                                    && !land.IsTapped));

        // ----------------------------------------------------------------
        // {T}: Put a charge counter on this land. (CR 602 — ordinary
        // activated ability; cost = tap.) Adds one charge counter
        // (CR 122.1). Not a mana ability (produces no mana), so it uses the
        // stack — same shape as Ratchet Bomb's charge-accrual ability.
        // ----------------------------------------------------------------
        var chargeEffect = new Effect(
            $"{CardName}: put a charge counter ({{T}})",
            () =>
            {
                if (land.Zone != ZoneType.Battlefield) return;
                land.Counters.Add(CounterType.Charge, 1);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { chargeEffect }));

        // ----------------------------------------------------------------
        // {T}, Remove a charge counter from this land: Add one mana of any
        // color. (CR 605.1 — mana ability; CR 605.3b — doesn't use the
        // stack.)
        //
        // Five ManaAbility instances (one per WUBRG) — same modal-colour
        // shape as Vivid Crag / Sphere of the Suns. The activation cost is
        // {T} PLUS "remove a charge counter", so the standard tap-as-cost
        // overload is used (tapsAsCost defaults to true). Each is gated on:
        //   (1) the land is still on the battlefield, AND
        //   (2) the land is untapped (so {T} is payable), AND
        //   (3) the land has at least one charge counter to remove
        //       (CR 605.3a — the cost must be payable).
        // The additionalCostPayer removes one charge counter inline.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => land.Zone == ZoneType.Battlefield
                                        && !land.IsTapped
                                        && land.Counters.Count(CounterType.Charge) > 0,
                additionalCostPayer: _ => RemoveOneChargeCounter(land)));
        }

        return land;
    }

    /// <summary>
    /// CR 121.5 / CR 602.1 — pay part of the activation cost by removing one
    /// charge counter from the land. Defensive against an empty pool (the
    /// canActivateCheck gate makes that unreachable in practice).
    /// </summary>
    private static void RemoveOneChargeCounter(Land land)
    {
        if (land.Counters.Count(CounterType.Charge) <= 0) return;
        land.Counters.Remove(CounterType.Charge, 1);
    }
}
