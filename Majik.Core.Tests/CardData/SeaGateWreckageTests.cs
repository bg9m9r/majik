using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SeaGateWreckageFactory"/>.
///
/// Land. Oracle text:
///   "Sea Gate Wreckage enters tapped.
///    {T}: Add {C}.
///    {2}, {T}: Draw a card. Activate only if you have no cards in hand."
/// </summary>
public class SeaGateWreckageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;

    public SeaGateWreckageTests()
    {
        _zones = new ZoneService(_bus, _replacements);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateWreckage_IsLand_WithNoBasicSupertype()
    {
        var land = SeaGateWreckageFactory.Create(_alice);

        land.Name.Should().Be("Sea Gate Wreckage");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Fact]
    public void SeaGateWreckage_OwnerAndControllerSet()
    {
        var land = SeaGateWreckageFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SeaGateWreckage()
    {
        var land = NamedCardFactory.Create("Sea Gate Wreckage", _alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Sea Gate Wreckage");
    }

    // -----------------------------------------------------------------------
    // ETB tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateWreckage_EntersTapped()
    {
        var land = SeaGateWreckageFactory.Create(_alice, _replacements);
        _zones.MoveCard(land, ZoneType.Library, ZoneType.Battlefield, _alice);

        land.IsTapped.Should().BeTrue("CR 614.1c — Sea Gate Wreckage enters tapped");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateWreckage_HasExactlyOneManaAbility_ColorlessC()
    {
        var land = SeaGateWreckageFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "only one printed mana ability: {T}: Add {C}");
        manaAbilities[0].ManaGenerated.Generic.Should().Be(1);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Draw activated ability — {2}, {T}: Draw a card. Empty-hand gate.
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateWreckage_HasExactlyOneActivatedAbility()
    {
        var land = SeaGateWreckageFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "one activated ability: {2}, {T}: Draw a card");
    }

    [Fact]
    public void SeaGateWreckage_DrawAbility_CostStack_Is_2Generic_Plus_TapSelf()
    {
        var land = SeaGateWreckageFactory.Create(_alice);
        var draw = land.Abilities.OfType<ActivatedAbility>().Single();

        draw.Costs.Should().HaveCount(2);

        var manaCost = draw.Costs.OfType<ManaCostCost>().Single();
        manaCost.Cost.Generic.Should().Be(2);
        manaCost.Cost.Black.Should().Be(0);

        var tap = draw.Costs.OfType<AdditionalCost>().Single();
        tap.CostType.Should().Be(AdditionalCostType.Tap);
    }

    [Fact]
    public void SeaGateWreckage_HasNoCardsInHand_PureHelper()
    {
        var land = SeaGateWreckageFactory.Create(_alice);

        // Empty hand → true.
        SeaGateWreckageFactory.HasNoCardsInHand(land).Should().BeTrue();

        // Add a card to Alice's hand.
        var bear = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);

        SeaGateWreckageFactory.HasNoCardsInHand(land).Should().BeFalse();
    }

    [Fact]
    public void SeaGateWreckage_DrawAbility_Resolve_DrawsOneCard_ForController()
    {
        var land = SeaGateWreckageFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        // Seed library with one card so the draw lands cleanly.
        var top = new Card("Mountain", "", new[] { CardType.Land });
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        _alice.Zones.Hand.Count.Should().Be(0);

        var draw = land.Abilities.OfType<ActivatedAbility>().Single();
        draw.Resolve();

        _alice.Zones.Hand.Count.Should().Be(1, "draw resolved → +1 card in hand");
        _alice.Zones.Library.Count.Should().Be(0);
    }
}
