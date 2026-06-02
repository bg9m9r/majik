using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CephalidColiseumFactory"/> — Cephalid Coliseum
/// (Torment, Land).
///
/// Oracle text:
///   "{T}: Add {U}. This land deals 1 damage to you.
///    Threshold — {U}, {T}, Sacrifice this land: Target player draws three
///    cards, then discards three cards. Activate only if there are seven or
///    more cards in your graveyard."
///
/// Covers:
/// - Identity (Land type, printed name, non-basic, owner/controller).
/// - Mana ability: exactly one {T}: Add {U} ManaAbility; activating deals
///   1 damage to you (life 20 → 19), taps the land (CR 120.3 — "deals 1
///   damage to you" reduces life; pain-land shape, no life-floor gate).
/// - Threshold activated ability: exactly one ActivatedAbility with costs
///   {U} (mana) + {T} (tap-self) + Sacrifice this land, and one 1..1
///   "target player" TargetRequest.
/// - Threshold gate (CR 702.84 — "Activate only if there are seven or more
///   cards in your graveyard"): CanActivateNow false with &lt;7 cards in the
///   controller's graveyard, true at exactly 7+.
/// - Resolution: target player draws three then discards three.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "U")]
public class CephalidColiseumFactoryTests
{
    private const string Name = "Cephalid Coliseum";

    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CephalidColiseum_IsLand_WithCorrectName()
    {
        var land = CephalidColiseumFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(Name);
    }

    [Fact]
    public void CephalidColiseum_IsNotBasic_AndNotLegendary()
    {
        var land = CephalidColiseumFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void CephalidColiseum_OwnerAndControllerAreSet()
    {
        var land = CephalidColiseumFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — {T}: Add {U}. This land deals 1 damage to you.
    // -----------------------------------------------------------------------

    [Fact]
    public void CephalidColiseum_HasExactlyOneManaAbility()
    {
        var land = CephalidColiseumFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the only mana ability is {T}: Add {U}");
    }

    [Fact]
    public void CephalidColiseum_ManaAbility_ProducesBlue()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Blue.Should().Be(1);
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
        mana.ManaGenerated.Generic.Should().Be(0);
    }

    [Fact]
    public void CephalidColiseum_ManaAbility_Activation_DealsOneDamageToYou()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.Activate();

        _alice.LifeTotal.Should().Be(19,
            "{T}: Add {U} deals 1 damage to you (CR 120.3)");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void CephalidColiseum_ManaAbility_CannotActivateWhenTapped()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.Activate();

        mana.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Threshold activated ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CephalidColiseum_HasExactlyOneActivatedAbility()
    {
        var land = CephalidColiseumFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the Threshold loot-3 ability is the only non-mana activated ability");
    }

    [Fact]
    public void CephalidColiseum_ThresholdAbility_HasManaTapAndSacrificeCosts()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "printed cost includes {U}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "printed cost includes {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "printed cost includes Sacrifice this land (CR 701.16)");
        ability.Costs.Should().HaveCount(3,
            "printed cost is {U}, {T}, Sacrifice this land");
    }

    [Fact]
    public void CephalidColiseum_ThresholdAbility_DeclaresOnePlayerTargetRequest()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.TargetRequests.Should().HaveCount(1, "the loot targets one player");
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Threshold gate — CR 702.84
    // -----------------------------------------------------------------------

    [Fact]
    public void CephalidColiseum_ThresholdAbility_CannotActivateBelowSevenGraveyardCards()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        // Six cards in Alice's graveyard — below threshold.
        for (var i = 0; i < 6; i++)
        {
            var c = new Land($"Filler {i}");
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
        }

        ability.CanActivateNow().Should().BeFalse(
            "Threshold (CR 702.84) requires seven or more cards in your graveyard");
    }

    [Fact]
    public void CephalidColiseum_ThresholdAbility_CanActivateAtSevenGraveyardCards()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        for (var i = 0; i < 7; i++)
        {
            var c = new Land($"Filler {i}");
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
        }

        ability.CanActivateNow().Should().BeTrue(
            "exactly seven cards in your graveyard meets Threshold");
    }

    // -----------------------------------------------------------------------
    // Threshold resolution — target player draws three, then discards three
    // -----------------------------------------------------------------------

    [Fact]
    public void CephalidColiseum_ThresholdAbility_TargetPlayerDrawsThreeThenDiscardsThree()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        // Bob (the chosen target) has 5 cards in library, 0 in hand.
        for (var i = 0; i < 5; i++)
        {
            var lib = new Land($"Bob Lib {i}");
            lib.SetOwner(_bob);
            _bob.Zones.Library.AddCard(lib);
            lib.SetZone(ZoneType.Library);
        }

        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var effect in ability.Effects) effect.Execute();

        // Drew 3 (library 5 → 2, hand 0 → 3), then discarded 3 (hand 3 → 0,
        // graveyard 0 → 3).
        _bob.Zones.Library.GetCards().Should().HaveCount(2,
            "target player drew three from the top of their library");
        _bob.Zones.Hand.GetCards().Should().HaveCount(0,
            "all three drawn cards were then discarded");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3,
            "three cards discarded into the graveyard");
    }

    [Fact]
    public void CephalidColiseum_ThresholdAbility_NoTarget_NoOp()
    {
        var land = CephalidColiseumFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        // No chosen targets set — the resolve body must guard and not throw.
        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("CR 608.2b — no legal target, the effect does nothing");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CephalidColiseum_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create(Name, _alice);

        card.Should().BeOfType<Land>("Cephalid Coliseum is a Land");
        card.Name.Should().Be(Name);
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the dispatcher attaches the {T}: Add {U} mana ability");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the dispatcher attaches the Threshold loot-3 activated ability");
    }
}
