using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FountainportFactory"/>.
///
/// Land. Oracle text (Scryfall-confirmed):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice a token: Draw a card.
///    {3}, {T}, Pay 1 life: Create a 1/1 blue Fish creature token.
///    {4}, {T}: Create a Treasure token."
///
/// Colourless (color identity []) → sharded as [Trait("Color", "C")].
/// </summary>
[Trait("Color", "C")]
public class FountainportTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;

    public FountainportTests()
    {
        _zones = new ZoneService(_bus, _replacements);
    }

    // -----------------------------------------------------------------------
    // Card identity — plain Land, no supertype/subtype, no mana cost.
    // -----------------------------------------------------------------------

    [Fact]
    public void Fountainport_Identity_IsPlainLand()
    {
        var land = FountainportFactory.Create(_alice);

        land.Name.Should().Be("Fountainport");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void Fountainport_HasExactlyOneManaAbility_ColorlessC()
    {
        var land = FountainportFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "only one printed mana ability: {T}: Add {C}");
        manaAbilities[0].ManaGenerated.Generic.Should().Be(1);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Three activated abilities.
    // -----------------------------------------------------------------------

    [Fact]
    public void Fountainport_HasThreeActivatedAbilities()
    {
        var land = FountainportFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(3,
            "draw / create-Fish / create-Treasure");
    }

    // -----------------------------------------------------------------------
    // {2}, {T}, Sacrifice a token: Draw a card.
    // -----------------------------------------------------------------------

    [Fact]
    public void Fountainport_DrawAbility_CostStack_Is_2Generic_TapSelf_SacToken()
    {
        var land = FountainportFactory.Create(_alice);

        var draw = DrawAbility(land);
        draw.Costs.Should().HaveCount(3);

        var manaCost = draw.Costs.OfType<ManaCostCost>().Single();
        manaCost.Cost.Generic.Should().Be(2);

        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);

        draw.Costs.OfType<SacrificeFilteredCost>().Should().ContainSingle(
            "the activation cost includes 'Sacrifice a token'");
    }

    [Fact]
    public void Fountainport_DrawAbility_Resolve_DrawsOneCard_ForController()
    {
        var land = FountainportFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var top = new Card("Mountain", "", new[] { CardType.Land });
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        _alice.Zones.Hand.Count.Should().Be(0);

        DrawAbility(land).Resolve();

        _alice.Zones.Hand.Count.Should().Be(1, "draw resolved → +1 card in hand");
    }

    // -----------------------------------------------------------------------
    // {3}, {T}, Pay 1 life: Create a 1/1 blue Fish creature token.
    // -----------------------------------------------------------------------

    [Fact]
    public void Fountainport_FishAbility_CostStack_Is_3Generic_TapSelf_PayOneLife()
    {
        var land = FountainportFactory.Create(_alice);

        var fish = FishAbility(land);
        fish.Costs.Should().HaveCount(3);

        fish.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(3);

        fish.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);

        fish.Costs.OfType<PayLifeCost>().Should().ContainSingle(
            "the activation cost includes 'Pay 1 life'");
    }

    [Fact]
    public void Fountainport_FishAbility_Resolve_CreatesOneOneBlueFishToken()
    {
        var land = FountainportFactory.Create(_alice, _zones);
        land.SetZone(ZoneType.Battlefield);

        FishAbility(land).Resolve();

        var fish = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Fish");

        fish.BasePower.Should().Be(1);
        fish.BaseToughness.Should().Be(1);
        fish.HasSubtype(CardSubtype.Fish).Should().BeTrue();
        CardColors.GetColors(fish).Should().BeEquivalentTo(new[] { ManaColor.Blue },
            "CR 111.4 — the Fish token is blue");
    }

    // -----------------------------------------------------------------------
    // {4}, {T}: Create a Treasure token.
    // -----------------------------------------------------------------------

    [Fact]
    public void Fountainport_TreasureAbility_CostStack_Is_4Generic_TapSelf()
    {
        var land = FountainportFactory.Create(_alice);

        var treasure = TreasureAbility(land);
        treasure.Costs.Should().HaveCount(2);

        treasure.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(4);
        treasure.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public void Fountainport_TreasureAbility_Resolve_CreatesTreasureToken()
    {
        var land = FountainportFactory.Create(_alice, _zones);
        land.SetZone(ZoneType.Battlefield);

        TreasureAbility(land).Resolve();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Should().ContainSingle(a => a.IsToken && a.HasSubtype(CardSubtype.Treasure),
                "CR 111.10 — Treasure token created");
    }

    // -----------------------------------------------------------------------
    // Helpers — disambiguate the three activated abilities by their costs.
    // -----------------------------------------------------------------------

    private static ActivatedAbility DrawAbility(Land land) =>
        land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<SacrificeFilteredCost>().Any());

    private static ActivatedAbility FishAbility(Land land) =>
        land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<PayLifeCost>().Any());

    private static ActivatedAbility TreasureAbility(Land land) =>
        land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Cost.Generic == 4));
}
