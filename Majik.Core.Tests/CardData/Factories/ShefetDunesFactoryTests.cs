using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShefetDunesFactory"/>.
///
/// Shefet Dunes — Land — Desert (Hour of Devastation).
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {T}, Pay 1 life: Add {W}.
///    {2}{W}{W}, {T}, Sacrifice a Desert: Creatures you control get +1/+1
///    until end of turn. Activate only as a sorcery."
///
/// Same Desert sac-land chassis as Barbarian Ring (sac-self-as-cost via an
/// effect-closure) + the painland pay-life mana shape (Cephalid Coliseum)
/// + the non-targeted +1/+1-until-EOT anthem (Restless Prairie).
///
/// Covers:
/// - Identity (Land, Desert subtype, non-Basic, non-Legendary, name,
///   owner/controller).
/// - Two mana abilities: {T}: Add {C} (colorless) and {T}, Pay 1 life: Add
///   {W} (white, life floor gate).
/// - Pay-life mana ability: activation costs 1 life and taps; gated when life
///   too low (CR 119.4).
/// - The {2}{W}{W} anthem ability is sorcery-speed (CR 117.1a) and pumps all
///   creatures you control +1/+1 until EOT (CR 514.2 cleanup expiry).
/// - The anthem ability sacrifices a Desert (the land itself) as a cost.
/// </summary>
[Trait("Color", "W")]
public class ShefetDunesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ShefetDunes_IsLand_Desert_WithCorrectName()
    {
        var land = ShefetDunesFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("Shefet Dunes is a Desert (CR 205.3i)");
        land.Name.Should().Be("Shefet Dunes");
    }

    [Fact]
    public void ShefetDunes_OwnerAndControllerAreSet()
    {
        var land = ShefetDunesFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ShefetDunes_IsNotBasic_AndNotLegendary()
    {
        var land = ShefetDunesFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ShefetDunes_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Shefet Dunes", _alice);

        card.Should().BeOfType<Land>();
        card!.Name.Should().Be("Shefet Dunes");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add {C} and {T}, Pay 1 life: Add {W}
    // -----------------------------------------------------------------------

    [Fact]
    public void ShefetDunes_HasTwoManaAbilities_ColorlessAndWhite()
    {
        var land = ShefetDunesFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2,
            "{T}: Add {C} and {T}, Pay 1 life: Add {W}");
        mana.Should().Contain(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.White == 0,
            "one mana ability produces {C} (colorless lands as generic per ManaCost.Parse)");
        mana.Should().Contain(m => m.ManaGenerated.White == 1,
            "one mana ability produces {W}");
    }

    [Fact]
    public void ShefetDunes_ColorlessManaAbility_TapsWithoutLifeLoss()
    {
        var land = ShefetDunesFactory.Create(_alice);
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.White == 0);

        colorless.Activate();

        _alice.LifeTotal.Should().Be(20, "{T}: Add {C} costs no life");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void ShefetDunes_WhiteManaAbility_Activation_CostsOneLife()
    {
        var land = ShefetDunesFactory.Create(_alice);
        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.Activate();

        _alice.LifeTotal.Should().Be(19,
            "{T}, Pay 1 life: Add {W} costs 1 life (CR 118.4)");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void ShefetDunes_WhiteManaAbility_CannotActivateWhenTapped()
    {
        var land = ShefetDunesFactory.Create(_alice);
        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.Activate();

        white.CanActivate().Should().BeFalse("a tapped land can't pay the {T} cost");
    }

    [Fact]
    public void ShefetDunes_WhiteManaAbility_CannotActivateAtOneLife()
    {
        // CR 119.4 — a player can't pay a life cost they can't afford.
        var lowLife = new Player("Lowlife", 1);
        var land = ShefetDunesFactory.Create(lowLife);
        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "you must be able to pay 1 life (life total must exceed the cost)");
    }

    // -----------------------------------------------------------------------
    // Anthem ability — {2}{W}{W}, {T}, Sacrifice a Desert: +1/+1 until EOT.
    // Activate only as a sorcery.
    // -----------------------------------------------------------------------

    [Fact]
    public void ShefetDunes_HasExactlyOneActivatedAbility_AndItIsSorcerySpeed()
    {
        var land = ShefetDunesFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "the anthem is the only non-mana activated ability");
        activated[0].IsSorcerySpeed.Should().BeTrue(
            "the anthem is \"Activate only as a sorcery\" (CR 117.1a)");
    }

    [Fact]
    public void ShefetDunes_Anthem_PumpsAllCreaturesYouControl_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = ShefetDunesFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var elf = new Creature("Llanowar Elves", "{G}", 1, 1);
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(elf);
        elf.SetZone(ZoneType.Battlefield);

        var anthem = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in anthem.Effects) e.Execute();

        effects.Compute(bear).Power.Should().Be(3, "base 2 + 1 from +1/+1");
        effects.Compute(bear).Toughness.Should().Be(3, "base 2 + 1 from +1/+1");
        effects.Compute(elf).Power.Should().Be(2, "base 1 + 1 from +1/+1");
        effects.Compute(elf).Toughness.Should().Be(2, "base 1 + 1 from +1/+1");

        // CR 514.2 cleanup — the pump expires at end of turn.
        effects.ExpireEndOfTurn();
        effects.Compute(bear).Power.Should().Be(2, "the +1/+1 pump expired at end of turn");
        effects.Compute(elf).Power.Should().Be(1);
    }

    [Fact]
    public void ShefetDunes_Anthem_SacrificesTheLand()
    {
        var effects = new ContinuousEffectsService();
        var land = ShefetDunesFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var anthem = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in anthem.Effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Graveyard,
            "Shefet Dunes sacrifices a Desert (itself) as part of the cost");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
    }

    [Fact]
    public void ShefetDunes_Anthem_NoCreatures_DoesNotThrow()
    {
        var effects = new ContinuousEffectsService();
        var land = ShefetDunesFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var anthem = land.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in anthem.Effects) e.Execute(); };

        act.Should().NotThrow("with no creatures the anthem is a clean no-op");
    }
}
