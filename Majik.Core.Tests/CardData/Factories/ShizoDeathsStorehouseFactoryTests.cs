using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ShizoDeathsStorehouseFactory"/> (Champions of
/// Kamigawa, Legendary Land).
///
/// Oracle text (Scryfall-confirmed):
///   "{T}: Add {B}.
///    {B}, {T}: Target legendary creature gains fear until end of turn.
///    (It can't be blocked except by artifact creatures and/or black
///    creatures.)"
///
/// Covers:
/// - Card identity (Legendary Land, owner/controller).
/// - {T}: Add {B} — vanilla black mana ability from the embedded JSON.
/// - Grant ability cost shape: {B} + {T} + a single 1..1 target.
/// - Resolution: the chosen creature gains fear until end of turn
///   (CR 613.1c), expiring at cleanup (CR 514.2).
/// - CR 608.2b guards: no target / no effects service → no-op, no throw.
/// - NamedCardFactory dispatcher resolves "Shizo, Death's Storehouse".
/// </summary>
[Trait("Color", "B")]
public class ShizoDeathsStorehouseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Shizo_IsLand()
    {
        var land = ShizoDeathsStorehouseFactory.Create(_alice);
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Legendary Land");
    }

    [Fact]
    public void Shizo_NameIsCorrect()
    {
        var land = ShizoDeathsStorehouseFactory.Create(_alice);
        land.Name.Should().Be("Shizo, Death's Storehouse");
    }

    [Fact]
    public void Shizo_IsLegendary()
    {
        var land = ShizoDeathsStorehouseFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Shizo, Death's Storehouse is a Legendary Land");
    }

    [Fact]
    public void Shizo_OwnerAndControllerAreSet()
    {
        var land = ShizoDeathsStorehouseFactory.Create(_alice);
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Shizo_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Shizo, Death's Storehouse", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Shizo, Death's Storehouse");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        // {T}: Add {B} is the only mana ability.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void Shizo_HasBlackTapAbility()
    {
        var land = ShizoDeathsStorehouseFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();
        mana.ManaGenerated.Black.Should().Be(1, "the {T}: Add {B} mana ability");
        mana.ManaGenerated.TotalValue.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // {B}, {T}: Target legendary creature gains fear until end of turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void GrantAbility_HasCorrectCostShape_ManaAndTapAndOneTarget()
    {
        var land = ShizoDeathsStorehouseFactory.Create(_alice);

        var grant = ShizoDeathsStorehouseFactory.GetGrantAbility(land);

        var mana = grant.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Black.Should().Be(1, "the {B} pip");
        grant.Costs.OfType<AdditionalCost>().Should().HaveCount(1, "{T} is part of the cost");
        grant.TargetRequests.Should().ContainSingle();
        grant.TargetRequests[0].MinTargets.Should().Be(1);
        grant.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void GrantAbility_OnResolution_GivesFearUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = ShizoDeathsStorehouseFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var target = new Creature("Legend", "", 3, 3)
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);

        effects.Compute(target).Keywords.Should().NotContain("Fear", "no grant yet");

        var grant = ShizoDeathsStorehouseFactory.GetGrantAbility(land);
        grant.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        grant.Resolve();

        effects.Compute(target).Keywords.Should().Contain("Fear",
            "the ability grants fear until end of turn (CR 702.36 / 613.1c)");

        // CR 514.2 — the grant expires during cleanup.
        effects.ExpireEndOfTurn();
        effects.Compute(target).Keywords.Should().NotContain("Fear",
            "fear expired at cleanup");
    }

    [Fact]
    public void GrantAbility_NoTargetOrEffectsService_NoOp_DoesNotThrow()
    {
        var land = ShizoDeathsStorehouseFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var grant = ShizoDeathsStorehouseFactory.GetGrantAbility(land);
        // No target primed + no effects service — resolving must not throw.
        var resolve = () => grant.Resolve();
        resolve.Should().NotThrow();
    }
}
