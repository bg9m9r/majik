using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Direct unit tests for the dynamic-mana + additional-cost-payer
/// <see cref="ManaAbility"/> ctor (deferral #2). Composes a
/// <c>Func&lt;ManaCost&gt;</c> mana generator with an
/// <c>Action&lt;Player&gt;</c> additional-cost payer so "{N},{T}: Add … for
/// each …" cards (Cabal Coffers) declare the {N} cost cleanly instead of
/// inlining it inside the generator lambda.
/// </summary>
public class ManaAbilityDynamicCostTests
{
    private static Land Source(Player owner)
    {
        var land = new Land("Source") { Owner = owner, Controller = owner };
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void Activate_RunsDynamicGenerator_AndPaysAdditionalCost_AndTaps()
    {
        var alice = new Player("Alice", 20);
        var land = Source(alice);
        alice.AddManaToPool(ManaCost.Parse("2"));
        var dynamicAmount = 3;

        var ability = new ManaAbility(
            source: land,
            controller: alice,
            manaGenerator: () => ManaCost.Parse(new string('B', dynamicAmount)),
            canActivateCheck: () => !land.IsTapped && alice.ManaPool.CanPay(ManaCost.Parse("2")),
            additionalCostPayer: p => p.PayMana(ManaCost.Parse("2")));

        ability.CanActivate().Should().BeTrue();
        var produced = ability.Activate();

        produced.Black.Should().Be(3, "dynamic generator produced {B}{B}{B}");
        alice.ManaPool.Generic.Should().Be(0, "the {2} additional cost was paid");
        land.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    [Fact]
    public void CanActivate_FalseWhenAdditionalCostUnaffordable()
    {
        var alice = new Player("Alice", 20); // empty pool
        var land = Source(alice);

        var ability = new ManaAbility(
            source: land,
            controller: alice,
            manaGenerator: () => ManaCost.Parse("B"),
            canActivateCheck: () => !land.IsTapped && alice.ManaPool.CanPay(ManaCost.Parse("2")),
            additionalCostPayer: p => p.PayMana(ManaCost.Parse("2")));

        ability.CanActivate().Should().BeFalse("the {2} additional cost is unaffordable");
        land.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void DynamicGenerator_EvaluatedAtActivation_NotAtConstruction()
    {
        // The amount is read at Activate() time, so changing it after
        // construction is reflected (the Cabal Coffers Swamp-count semantics).
        var alice = new Player("Alice", 20);
        var land = Source(alice);
        alice.AddManaToPool(ManaCost.Parse("2"));
        var amount = 1;

        var ability = new ManaAbility(
            source: land,
            controller: alice,
            manaGenerator: () => ManaCost.Parse(new string('B', amount)),
            canActivateCheck: () => !land.IsTapped && alice.ManaPool.CanPay(ManaCost.Parse("2")),
            additionalCostPayer: p => p.PayMana(ManaCost.Parse("2")));

        amount = 5; // changes the count before activation

        ability.Activate().Black.Should().Be(5, "the generator is lazy — evaluated at Activate()");
    }
}
