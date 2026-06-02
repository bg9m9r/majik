using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CaveOfTemptationFactory"/>.
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color.
///    {4}, {T}, Sacrifice this land: Put two +1/+1 counters on target
///    creature. Activate only as a sorcery."
///
/// Covers:
/// - Land identity (name, Land type, owner/controller) + NamedCardFactory dispatch.
/// - {T}: Add {C} (from JSON) — produces one colorless/generic, no extra cost.
/// - Five {1}, {T}: Add one mana of any color abilities (one per WUBRG):
///   gated on untapped + {1} affordability, pays {1} and taps on activation.
/// - {4}, {T}, Sacrifice this land: put two +1/+1 counters on target creature,
///   sorcery-speed; self-sacrifices on resolution.
/// </summary>
public class CaveOfTemptationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature BearFor(Player owner)
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(owner);
        bear.SetController(owner);
        owner.Zones.Battlefield.AddCard(bear);
        bear.ClearSummoningSickness();
        return bear;
    }

    private Land OnBattlefield()
    {
        var land = CaveOfTemptationFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTemptation_Identity()
    {
        var land = CaveOfTemptationFactory.Create(_alice);

        land.Name.Should().Be("Cave of Temptation");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CaveOfTemptation_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Cave of Temptation", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Cave of Temptation");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} (from JSON)
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTemptation_HasColorlessManaAbility_ProducesC()
    {
        var land = CaveOfTemptationFactory.Create(_alice);

        // The plain {C} ability is the one mana ability with no additional
        // mana cost (the any-colour ones each pay {1}).
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(a => a is not CaveOfTemptationManaAbility);

        colorless.CanActivate().Should().BeTrue("the land is untapped and {C} needs no other cost");
        var mana = colorless.Activate();

        mana.Generic.Should().Be(1, "{T}: Add {C}");
        mana.White.Should().Be(0);
        mana.Blue.Should().Be(0);
        mana.Black.Should().Be(0);
        mana.Red.Should().Be(0);
        mana.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("{T} is the activation cost of the {C} ability");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Add one mana of any color — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTemptation_HasFiveAnyColorManaAbilities()
    {
        var land = CaveOfTemptationFactory.Create(_alice);

        land.Abilities.OfType<CaveOfTemptationManaAbility>().Should().HaveCount(5);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void CaveOfTemptation_HasOneAbilityPerColor(string colorPip)
    {
        var land = CaveOfTemptationFactory.Create(_alice);

        land.Abilities.OfType<CaveOfTemptationManaAbility>()
            .Should().ContainSingle(a => a.ColorPip == colorPip);
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Add one mana of any color — activation
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTemptation_AnyColor_PaysOneGeneric_TapsLand_ProducesColor()
    {
        var land = OnBattlefield();
        // Feed {1} into the pool so the additional cost is payable.
        _alice.AddManaToPool(ManaCost.Parse("1"));

        var blue = land.Abilities.OfType<CaveOfTemptationManaAbility>()
            .Single(a => a.ColorPip == "U");

        blue.CanActivate().Should().BeTrue("the land is untapped and {1} is in the pool");
        var mana = blue.Activate();

        mana.Blue.Should().Be(1, "{1}, {T}: Add one mana of any color — here U");
        mana.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue("self-tap is part of the activation cost");
        _alice.ManaPool.Generic.Should().Be(0, "the {1} additional cost was spent");
    }

    [Fact]
    public void CaveOfTemptation_AnyColor_CannotActivate_WhenNoMana()
    {
        var land = OnBattlefield();
        // Empty pool — cannot pay {1}.

        var any = land.Abilities.OfType<CaveOfTemptationManaAbility>().First();
        any.CanActivate().Should().BeFalse("the {1} additional cost cannot be paid from an empty pool");
    }

    [Fact]
    public void CaveOfTemptation_AnyColor_CannotActivate_WhenLandTapped()
    {
        var land = OnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("1"));
        land.Tap();

        var any = land.Abilities.OfType<CaveOfTemptationManaAbility>().First();
        any.CanActivate().Should().BeFalse("the land itself must be untapped to pay {T}");
    }

    // -----------------------------------------------------------------------
    // {4}, {T}, Sacrifice this land: Put two +1/+1 counters on target creature.
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTemptation_HasSorceryCounterAbility()
    {
        var land = CaveOfTemptationFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);

        ability.IsSorcerySpeed.Should().BeTrue("'Activate only as a sorcery' (CR 117.1a / 307.5)");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void CaveOfTemptation_SacAbility_PutsTwoCounters_AndSacrificesSelf()
    {
        var land = OnBattlefield();
        var target = BearFor(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        ability.Resolve();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should()
            .Be(2, "Put two +1/+1 counters on target creature");
        land.Zone.Should().Be(ZoneType.Graveyard, "Sacrifice this land");
        _alice.Zones.Graveyard.ContainsCard(land).Should().BeTrue();
    }

    [Fact]
    public void CaveOfTemptation_SacAbility_CanTargetAnyCreature_NotJustOpponents()
    {
        var land = OnBattlefield();
        var bobsCreature = BearFor(_bob);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });

        ability.Resolve();

        bobsCreature.Counters.Count(CounterType.PlusOnePlusOne).Should()
            .Be(2, "'target creature' has no controller restriction");
    }
}
