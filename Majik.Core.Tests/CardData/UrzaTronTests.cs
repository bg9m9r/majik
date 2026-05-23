using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for the Urza Tron land cycle — Urza's Mine, Urza's Tower,
/// Urza's Power-Plant (Antiquities).
///
/// Each card has the same printed mana ability:
///   "{T}: Add {C}. If you control an Urza's Mine, an Urza's
///    Power-Plant, and an Urza's Tower, add {2} instead."
///
/// CR 605 — mana ability, no stack, no targets. The amount of mana is
/// decided at activation time against the controller's live
/// battlefield (controller-only — opposing Tron pieces don't count).
///
/// Coverage:
/// - Identity for each card (Land type, no supertypes, correct
///   Urza's + Mine/Tower/PowerPlant subtype pair).
/// - NamedCardFactory dispatch for each printed name.
/// - Tap alone → adds {C} (1 generic).
/// - Tap with all three Tron pieces controlled → adds {2} (2 generic).
/// - Tap with only two pieces (third missing) → still {C}.
/// - Tap when opponent controls one of the three → still {C}
///   (controller-only check).
/// </summary>
public class UrzaTronTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void UrzasMine_Identity()
    {
        var land = UrzasMineFactory.Create(_alice);

        land.Name.Should().Be("Urza's Mine");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Supertypes.Should().BeEmpty("Urza's Mine has no supertype (not basic, not legendary)");
        land.HasSubtype(CardSubtype.Urzas).Should().BeTrue();
        land.HasSubtype(CardSubtype.Mine).Should().BeTrue();
        land.HasSubtype(CardSubtype.Tower).Should().BeFalse();
        land.HasSubtype(CardSubtype.PowerPlant).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UrzasTower_Identity()
    {
        var land = UrzasTowerFactory.Create(_alice);

        land.Name.Should().Be("Urza's Tower");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Supertypes.Should().BeEmpty();
        land.HasSubtype(CardSubtype.Urzas).Should().BeTrue();
        land.HasSubtype(CardSubtype.Tower).Should().BeTrue();
        land.HasSubtype(CardSubtype.Mine).Should().BeFalse();
        land.HasSubtype(CardSubtype.PowerPlant).Should().BeFalse();
    }

    [Fact]
    public void UrzasPowerPlant_Identity()
    {
        var land = UrzasPowerPlantFactory.Create(_alice);

        land.Name.Should().Be("Urza's Power-Plant");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Supertypes.Should().BeEmpty();
        land.HasSubtype(CardSubtype.Urzas).Should().BeTrue();
        land.HasSubtype(CardSubtype.PowerPlant).Should().BeTrue();
        land.HasSubtype(CardSubtype.Mine).Should().BeFalse();
        land.HasSubtype(CardSubtype.Tower).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void UrzasMine_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Urza's Mine", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Urza's Mine");
        card.HasSubtype(CardSubtype.Urzas).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mine).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void UrzasTower_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Urza's Tower", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Urza's Tower");
        card.HasSubtype(CardSubtype.Urzas).Should().BeTrue();
        card.HasSubtype(CardSubtype.Tower).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void UrzasPowerPlant_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Urza's Power-Plant", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Urza's Power-Plant");
        card.HasSubtype(CardSubtype.Urzas).Should().BeTrue();
        card.HasSubtype(CardSubtype.PowerPlant).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability — conditional output
    // -----------------------------------------------------------------------

    [Fact]
    public void TronLand_TapAlone_AddsOneColorless()
    {
        // Only an Urza's Mine on the battlefield — no Tower, no Power-Plant.
        var mine = UrzasMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mine);
        mine.SetZone(ZoneType.Battlefield);

        var ability = mine.Abilities.OfType<ManaAbility>().Single();
        var produced = ability.Activate();

        // {C} buckets as +1 generic (CR 107.4c — engine has no dedicated
        // colourless slot today; see ManaCost.Parse).
        produced.Generic.Should().Be(1);
        produced.TotalValue.Should().Be(1);
    }

    [Fact]
    public void TronLand_TapWithAllThreeControlled_AddsTwo()
    {
        // Assemble Tron on Alice's side.
        var mine = UrzasMineFactory.Create(_alice);
        var tower = UrzasTowerFactory.Create(_alice);
        var plant = UrzasPowerPlantFactory.Create(_alice);
        foreach (var land in new Permanent[] { mine, tower, plant })
        {
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        // Tap the Mine.
        var ability = mine.Abilities.OfType<ManaAbility>().Single();
        var produced = ability.Activate();

        produced.Generic.Should().Be(2, "Tron is assembled — {2} replaces {C}");
        produced.TotalValue.Should().Be(2);

        // Sanity: Tower and Power-Plant produce {2} too while Tron stands.
        var fromTower = tower.Abilities.OfType<ManaAbility>().Single().Activate();
        var fromPlant = plant.Abilities.OfType<ManaAbility>().Single().Activate();
        fromTower.Generic.Should().Be(2);
        fromPlant.Generic.Should().Be(2);
    }

    [Fact]
    public void TronLand_TapWithOnlyTwoControlled_AddsColorless()
    {
        // Two of three — third is missing.
        var mine = UrzasMineFactory.Create(_alice);
        var tower = UrzasTowerFactory.Create(_alice);
        foreach (var land in new Permanent[] { mine, tower })
        {
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        var produced = mine.Abilities.OfType<ManaAbility>().Single().Activate();

        produced.Generic.Should().Be(1, "Power-Plant absent — fall back to {C}");
    }

    [Fact]
    public void TronLand_OpponentControlsOneOfTheThree_StillAddsColorless()
    {
        // Alice has Mine + Tower. Bob has Power-Plant.
        // The conditional is "you control all three" (controller-only):
        // Bob's Power-Plant doesn't enable Alice's Tron.
        var mine = UrzasMineFactory.Create(_alice);
        var tower = UrzasTowerFactory.Create(_alice);
        var oppPlant = UrzasPowerPlantFactory.Create(_bob);

        _alice.Zones.Battlefield.AddCard(mine);
        mine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tower);
        tower.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(oppPlant);
        oppPlant.SetZone(ZoneType.Battlefield);

        var produced = mine.Abilities.OfType<ManaAbility>().Single().Activate();

        produced.Generic.Should().Be(1, "controller-only check — opponent's Power-Plant doesn't count");
    }
}
