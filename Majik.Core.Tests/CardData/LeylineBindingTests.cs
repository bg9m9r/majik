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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LeylineBindingFactory"/> — Enchantment {5}{W}.
///
///   "Flash
///    Domain — This spell costs {1} less to cast for each basic land type
///    among lands you control.
///    When this enchantment enters, exile target nonland permanent an
///    opponent controls until this enchantment leaves the battlefield."
///
/// Leyline Binding is the "Oblivion Ring" exile-until-leaves template
/// (CR 701.21) on a Flash (CR 702.8) body with a Domain (CR 702.16 /
/// CR 117.7) cost reducer — the same backbone as
/// <see cref="CastOutFactory"/>.
///
/// Covers:
/// - Card identity (Enchantment, {5}{W}; NOT an Aura).
/// - NamedCardFactory dispatch.
/// - Flash keyword marker present (CR 702.8).
/// - Domain cost reduction (CR 702.16 / CR 117.7): 0/3/5 basic types,
///   floor preserving the single coloured W pip (CR 117.7c).
/// - ETB exile + LTB return O-Ring pair:
///     * ETB exiles a target nonland permanent an opponent controls.
///     * ETB rejects lands (CR 608.2b) + controller-side permanents.
///     * LTB returns the exiled card under its owner's control (CR 110.2).
/// </summary>
public class LeylineBindingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void AddBasic(Player owner, CardSubtype subtype, string name)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype })
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(land);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_IsEnchantment_WithFiveGenericOneWhite()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        lb.Name.Should().Be("Leyline Binding");
        lb.HasType(CardType.Enchantment).Should().BeTrue();
        lb.IsAura.Should().BeFalse("the current Leyline Binding is a plain Enchantment, not an Aura");
        lb.ManaCost.Should().Be("{5}{W}");
        lb.Owner.Should().BeSameAs(_alice);
        lb.Controller.Should().BeSameAs(_alice);
        lb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LeylineBinding()
    {
        var lb = NamedCardFactory.Create("Leyline Binding", _alice);

        lb.Should().BeOfType<Enchantment>();
        lb.Name.Should().Be("Leyline Binding");
        lb.ManaCost.Should().Be("{5}{W}");
    }

    [Fact]
    public void LeylineBinding_HasFlashKeyword()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        lb.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "Leyline Binding has Flash (CR 702.8)");
    }

    // -----------------------------------------------------------------------
    // Domain cost reduction (CR 702.16 / CR 117.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_NoBasicTypes_PaysFullFiveGenericOneWhite()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        effective.Generic.Should().Be(5, "no basic land types → no Domain reduction");
        effective.White.Should().Be(1, "the single coloured W pip is untouched (CR 117.7c)");
    }

    [Fact]
    public void LeylineBinding_ThreeBasicTypes_ReducesGenericByThree()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        effective.Generic.Should().Be(2, "{5} generic − {3} for three basic land types");
        effective.White.Should().Be(1, "coloured pips never reduce (CR 117.7c)");
    }

    [Fact]
    public void LeylineBinding_AllFiveBasicTypes_CollapsesToSingleWhite()
    {
        // The canonical "Leyline Binding turn-2 for {W}" case.
        var lb = LeylineBindingFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Swamp, "Swamp");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");
        AddBasic(_alice, CardSubtype.Forest, "Forest");

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        effective.Generic.Should().Be(0,
            "{5} generic − {5} for all five basic land types floors at zero");
        effective.White.Should().Be(1,
            "CR 117.7c — Domain only reduces generic mana; the W pip remains");
    }

    [Fact]
    public void LeylineBinding_DomainReducer_IsExactlyOnePerBasicType()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        var reducer = lb.Abilities.OfType<CostReductionAbility>().Single();
        reducer.TotalReducer.Should().NotBeNull("Domain uses the whole-reducer shape");

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        reducer.TotalReducer!(_alice).Should().Be(3,
            "Domain returns 1 × number of distinct basic land types (CR 702.16)");
    }

    // -----------------------------------------------------------------------
    // O-Ring exile-until-leaves (CR 701.21 / 603.6 / 610.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_Etb_ExilesOpponentPermanent()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        etb.Resolve();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted nonland permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void LeylineBinding_Etb_RejectsLandTarget()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });
        etb.Resolve();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "lands are skipped by the printed 'nonland' filter (CR 608.2b)");
    }

    [Fact]
    public void LeylineBinding_Etb_RejectsControllerOwnPermanent()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        etb.Resolve();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents ('an opponent controls', CR 109.5)");
    }

    [Fact]
    public void LeylineBinding_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        etb.Resolve();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        ltb.Resolve();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void LeylineBinding_Ltb_NoOpWhenNothingExiled()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var ltb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        ltb.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
