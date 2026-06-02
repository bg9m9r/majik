using System.Linq;
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
/// Tests for <see cref="BondersEnclaveFactory"/>.
///
/// Land. Oracle text (Scryfall-confirmed):
///   "{T}: Add {C}.
///    {3}, {T}: Draw a card. Activate only if you control a creature with
///    power 4 or greater."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Bonders'
/// Enclave enters untapped (no ETB-tapped clause). Identity + both abilities
/// are loaded from <c>bonders-enclave.json</c> via
/// <see cref="CardDefinitionFactory"/>; the power-4+ activation gate is exposed
/// as a public predicate (same posture as
/// <see cref="SeaGateWreckageFactory.HasNoCardsInHand"/>).
///
/// Covers:
/// - Identity: Land type, name, non-basic, non-legendary.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Bonders' Enclave".
/// - Two abilities: one <see cref="ManaAbility"/> ({T}: Add {C}) + one
///   <see cref="ActivatedAbility"/> ({3},{T}: Draw a card).
/// - Mana ability: {T} produces {C}; CanActivate false when tapped.
/// - Activated ability cost: {3} mana + tap.
/// - Activated ability resolve: draws one card for the controller.
/// - Power-4+ gate (CR 602.5):
///     · No creature → false.
///     · Creature with power 3 → false.
///     · Creature with power 4 → true.
///     · Creature with power 5 → true.
///     · Only an opponent controls the big creature → false.
/// </summary>
[Trait("Color", "C")]
public class BondersEnclaveFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: place Bonders' Enclave on Alice's battlefield (untapped).
    // -----------------------------------------------------------------------
    private Land PlaceOnBattlefield()
    {
        var enclave = BondersEnclaveFactory.Create(_alice);
        enclave.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enclave);
        return enclave;
    }

    // -----------------------------------------------------------------------
    // Helper: add a creature of the given power to a player's battlefield.
    // -----------------------------------------------------------------------
    private static Creature AddCreature(Player controller, int power)
    {
        var creature = new Creature("Test Beast", "", power, power)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        return creature;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedBondersEnclave()
    {
        var enclave = BondersEnclaveFactory.Create(_alice);
        enclave.Name.Should().Be("Bonders' Enclave");
        enclave.HasType(CardType.Land).Should().BeTrue();
        enclave.Owner.Should().BeSameAs(_alice);
        enclave.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary()
    {
        var enclave = BondersEnclaveFactory.Create(_alice);
        enclave.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Bonders' Enclave is nonbasic");
        enclave.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Bonders' Enclave is not legendary");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BondersEnclave()
    {
        var enclave = NamedCardFactory.Create("Bonders' Enclave", _alice);

        enclave.Should().BeOfType<Land>();
        enclave.Name.Should().Be("Bonders' Enclave");
    }

    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneActivated()
    {
        var enclave = BondersEnclaveFactory.Create(_alice);
        enclave.Abilities.Should().HaveCount(2,
            "one {T}: Add {C} mana ability + one {3},{T} activated ability");
        enclave.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        enclave.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_Activate_ProducesOneColorless()
    {
        var enclave = PlaceOnBattlefield();
        var mana = (IManaAbility)enclave.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Generic.Should().Be(1, "the mana ability produces {C}");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        enclave.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ManaAbility_CanActivate_FalseWhenTapped()
    {
        var enclave = PlaceOnBattlefield();
        enclave.Tap();
        var mana = (IManaAbility)enclave.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // Activated ability: {3}, {T} — cost gates
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_CostStack_Is_3Generic_Plus_TapSelf()
    {
        var enclave = BondersEnclaveFactory.Create(_alice);
        var draw = enclave.Abilities.OfType<ActivatedAbility>().Single();

        draw.Costs.Should().HaveCount(2, "costs are {3} mana + tap");

        var manaCost = draw.Costs.OfType<ManaCostCost>().Single();
        manaCost.Cost.Generic.Should().Be(3);
        manaCost.Cost.Black.Should().Be(0);

        var tap = draw.Costs.OfType<AdditionalCost>().Single();
        tap.CostType.Should().Be(AdditionalCostType.Tap);
    }

    // -----------------------------------------------------------------------
    // Activated ability: Draw a card (CR 120)
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_Resolve_DrawsOneCard_ForController()
    {
        var enclave = BondersEnclaveFactory.Create(_alice);
        enclave.SetZone(ZoneType.Battlefield);

        // Seed library with one card so the draw lands cleanly.
        var top = new Card("Mountain", "", new[] { CardType.Land });
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        _alice.Zones.Hand.Count.Should().Be(0);

        var draw = enclave.Abilities.OfType<ActivatedAbility>().Single();
        draw.Effects.Single().Execute();

        _alice.Zones.Hand.Count.Should().Be(1, "draw resolved → +1 card in hand");
        _alice.Zones.Library.Count.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Power-4+ activation gate (CR 602.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void Gate_FalseWhenNoCreature()
    {
        var enclave = PlaceOnBattlefield();
        BondersEnclaveFactory.ControlsCreatureWithPower4OrGreater(enclave)
            .Should().BeFalse("Alice controls no creatures");
    }

    [Fact]
    public void Gate_FalseWhenCreaturePower3()
    {
        var enclave = PlaceOnBattlefield();
        AddCreature(_alice, power: 3);
        BondersEnclaveFactory.ControlsCreatureWithPower4OrGreater(enclave)
            .Should().BeFalse("power 3 is below the 4-or-greater threshold");
    }

    [Fact]
    public void Gate_TrueWhenCreaturePower4()
    {
        var enclave = PlaceOnBattlefield();
        AddCreature(_alice, power: 4);
        BondersEnclaveFactory.ControlsCreatureWithPower4OrGreater(enclave)
            .Should().BeTrue("power exactly 4 satisfies '4 or greater'");
    }

    [Fact]
    public void Gate_TrueWhenCreaturePower5()
    {
        var enclave = PlaceOnBattlefield();
        AddCreature(_alice, power: 5);
        BondersEnclaveFactory.ControlsCreatureWithPower4OrGreater(enclave)
            .Should().BeTrue("power 5 satisfies '4 or greater'");
    }

    [Fact]
    public void Gate_FalseWhenOnlyOpponentControlsBigCreature()
    {
        var enclave = PlaceOnBattlefield();
        var bob = new Player("Bob", 20);
        AddCreature(bob, power: 6);
        BondersEnclaveFactory.ControlsCreatureWithPower4OrGreater(enclave)
            .Should().BeFalse("the 'you control' predicate checks the controller's battlefield, not the opponent's");
    }
}
