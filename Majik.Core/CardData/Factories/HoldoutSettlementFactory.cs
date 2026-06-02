using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Holdout Settlement (Amonkhet, common land).
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}, Tap an untapped creature you control: Add one mana of any color."
///
/// <para>
/// The Land shell (identity / owner / controller) plus the colorless
/// <c>{T}: Add {C}</c> mana ability are declared declaratively in
/// <c>Majik.Core/CardData/Cards/holdout-settlement.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/> — the same posture
/// as <see cref="ManaConfluenceFactory"/> / <see cref="CityOfBrassFactory"/>.
/// The data-only <see cref="ManaAbilityDefinition"/> schema carries only a
/// <c>Produces</c> string, so it can express the plain <c>{C}</c> mode but
/// NOT the five-colour any-colour fan-out nor the "Tap an untapped creature
/// you control" additional activation cost. The JSON therefore declares
/// only the <c>{C}</c> ability; this factory attaches the five coloured
/// abilities on top.
/// </para>
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype) + <b>{T}: Add {C}</b> via JSON
///   (<c>ManaCost.Parse("C")</c> tracks colorless as generic mana — same
///   path as every other <c>produces: "C"</c> land).
/// - <b>{T}, Tap an untapped creature you control: Add one mana of any
///   color.</b> — modelled as five <see cref="ManaAbility"/> instances, one
///   per WUBRG (the same any-colour fan-out as Springleaf Drum / Mana
///   Confluence / Aether Hub). Each is the additional-cost overload:
///     - <c>{T}</c> on the Settlement itself is the implicit self-tap baked
///       into <see cref="ManaAbility"/>.
///     - The "tap an untapped creature you control" component is a
///       <see cref="TapAnotherUntappedCreatureCost"/> consulted by
///       <c>canActivateCheck</c> (CR 119.4 — can't pay a cost you can't
///       afford) and executed by <c>additionalCostPayer</c> (CR 118.12 /
///       605.1 — the second tap is paid concurrently with the land's own
///       self-tap; mana abilities don't use the stack). Summoning sickness
///       on the would-be-tapped creature is honoured (CR 302.6) by the cost.
///
/// The "any color" picker is expressed structurally: one coloured ability
/// slot per WUBRG, so the activator picks the colour by picking the
/// matching slot — no separate colour prompt needed.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for which creature to tap</b> — the cost falls back to
///   the first eligible (untapped, no summoning sickness) creature on the
///   controller's battlefield via <see cref="TapAnotherUntappedCreatureCost"/>'s
///   deterministic pick. Agents/tests can pre-set
///   <see cref="TapAnotherUntappedCreatureCost.Target"/> to override — the
///   same gap as Springleaf Drum and the rest of the additional-cost family.
/// </summary>
[CardName("Holdout Settlement")]
public static class HoldoutSettlementFactory
{
    public const string CardName = "Holdout Settlement";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("holdout-settlement");

    /// <summary>Construct Holdout Settlement owned and controlled by
    /// <paramref name="owner"/> with the colorless {C} ability (from JSON)
    /// plus the five "Tap a creature: any color" abilities attached.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}, Tap an untapped creature you control: Add one mana of any
        //   color. Five ManaAbility instances (one per WUBRG) — same
        //   any-colour fan-out as Springleaf Drum. Each carries the
        //   tap-another-creature additional cost.
        foreach (var pip in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(BuildAnyColorAbility(land, owner, pip));
        }

        return land;
    }

    /// <summary>
    /// Build one colour's <see cref="ManaAbility"/> slot. Exposed for tests
    /// that need to inspect or activate a specific colour.
    /// </summary>
    public static HoldoutSettlementManaAbility BuildAnyColorAbility(
        Permanent source, Player controller, string colorPip)
    {
        var tapCost = new TapAnotherUntappedCreatureCost(source);
        return new HoldoutSettlementManaAbility(source, controller, colorPip, tapCost);
    }
}

/// <summary>
/// Holdout Settlement's per-colour mana ability. Subclasses
/// <see cref="ManaAbility"/> so the embedded
/// <see cref="TapAnotherUntappedCreatureCost"/> is reachable from outside
/// (agents / tests) for target-setting — same shape as
/// <see cref="SpringleafDrumManaAbility"/>.
/// </summary>
public sealed class HoldoutSettlementManaAbility : ManaAbility
{
    /// <summary>
    /// Colour pip this ability produces (one of W / U / B / R / G).
    /// </summary>
    public string ColorPip { get; }

    /// <summary>
    /// The creature-tap cost paid as part of activating this ability. Set
    /// <see cref="TapAnotherUntappedCreatureCost.Target"/> before
    /// <see cref="ManaAbility.Activate"/> to pick a specific creature;
    /// otherwise the cost falls back to its deterministic first-eligible
    /// pick.
    /// </summary>
    public TapAnotherUntappedCreatureCost TapChoice { get; }

    internal HoldoutSettlementManaAbility(
        Permanent source,
        Player controller,
        string colorPip,
        TapAnotherUntappedCreatureCost tapCost)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse(colorPip),
            canActivateCheck: () => source is Permanent p
                && !p.IsTapped
                && tapCost.CanPay(controller),
            additionalCostPayer: p => tapCost.Pay(p))
    {
        ColorPip = colorPip;
        TapChoice = tapCost;
    }
}
