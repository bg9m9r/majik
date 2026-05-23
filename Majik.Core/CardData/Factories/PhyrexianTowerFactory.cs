using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Tower (Urza's Saga / reprints).
///
/// Legendary Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {T}, Sacrifice a creature: Add {B}{B}."
///
/// ## Implementation (v1)
/// - First ability — <c>{T}: Add {C}</c> — wired as a vanilla
///   <see cref="ManaAbility"/>.
/// - Second ability — <c>{T}, Sacrifice a creature: Add {B}{B}</c> — wired
///   via the additional-cost <see cref="ManaAbility"/> constructor. The
///   <see cref="SacrificeAnotherCreatureCost"/> instance is exposed as
///   <see cref="PhyrexianTowerManaAbility.SacrificeChoice"/> so a caller
///   (test / bot) can pre-set <c>SacrificeChoice.Target</c> before
///   activation. <c>CanActivate</c> gates legality on:
///     1. the land is untapped, and
///     2. the controller has another creature available to sacrifice.
///
/// CR 605.1 — both abilities remain mana abilities (no stack), even with
/// the extra sacrifice cost; the sacrifice happens as part of activation
/// alongside the tap.
/// </summary>
public static class PhyrexianTowerFactory
{
    public const string CardName = "Phyrexian Tower";

    /// <summary>
    /// Construct Phyrexian Tower owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1: mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}, Sacrifice a creature: Add {B}{B}
        // Additional-cost ManaAbility — CR 605.1 still applies.
        // ----------------------------------------------------------------
        var sacrificeCost = new SacrificeAnotherCreatureCost(land);
        land.AddAbility(new PhyrexianTowerManaAbility(land, owner, sacrificeCost));

        return land;
    }
}

/// <summary>
/// Phyrexian Tower's <c>{T}, Sacrifice a creature: Add {B}{B}</c> mana
/// ability. Subclasses <see cref="ManaAbility"/> so the sacrifice cost is
/// reachable from outside for test / bot target-setting.
/// </summary>
public sealed class PhyrexianTowerManaAbility : ManaAbility
{
    /// <summary>
    /// The sacrifice cost paid as part of activating this ability. Set
    /// <see cref="SacrificeAnotherCreatureCost.Target"/> before
    /// <see cref="ManaAbility.Activate"/> to pick a specific creature;
    /// otherwise the cost falls back to its deterministic first-eligible
    /// pick (see <see cref="SacrificeAnotherCreatureCost"/>).
    /// </summary>
    public SacrificeAnotherCreatureCost SacrificeChoice { get; }

    internal PhyrexianTowerManaAbility(
        Land source,
        Player controller,
        SacrificeAnotherCreatureCost sacrificeCost)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse("BB"),
            canActivateCheck: () => !source.IsTapped && sacrificeCost.CanPay(controller),
            additionalCostPayer: p => sacrificeCost.Pay(p))
    {
        SacrificeChoice = sacrificeCost;
    }
}
