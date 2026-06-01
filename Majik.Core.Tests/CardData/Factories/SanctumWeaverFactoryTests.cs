using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Sanctum Weaver (Modern Horizons 2).
///
/// Covers:
///   * Card shape: Creature, 0/2, {1}{G}, Dryad subtype, green colour.
///   * v1: NOT modeled as Enchantment (plain Creature, no CardType.Enchantment).
///   * Dispatch: NamedCardFactory.Create returns a Creature.
///   * Five mana ability slots (WUBRG).
///   * Activation with N enchantments → N pips of chosen colour; creature taps.
///   * Activation with 0 enchantments → zero mana produced; still legal.
///   * CountEnchantments counts only CardType.Enchantment permanents.
///   * CanActivate: false when creature is already tapped.
///   * Summoning sickness blocks activation (bare {T} cost).
///   * BuildColorMana helper.
/// </summary>
public class SanctumWeaverFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Place Sanctum Weaver on Alice's battlefield, untapped, with
    /// summoning sickness cleared so it can activate immediately.
    /// </summary>
    private Creature PlaceOnBattlefield()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);
        weaver.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(weaver);
        weaver.ClearSummoningSickness();
        return weaver;
    }

    /// <summary>
    /// Add a plain <see cref="Enchantment"/> to Alice's battlefield.
    /// </summary>
    private Enchantment AddEnchantment(string name = "Test Enchantment")
    {
        var enc = new Enchantment(name, "{G}");
        enc.SetOwner(_alice);
        enc.SetController(_alice);
        enc.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enc);
        return enc;
    }

    // -----------------------------------------------------------------------
    // Card shape / identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsCreature_Named_SanctumWeaver()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.Name.Should().Be("Sanctum Weaver");
        weaver.Should().BeOfType<Creature>();
    }

    [Fact]
    public void Create_HasCorrectManaCost_TwoManaValue()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        // {1}{G} — mana value 2 (1 generic + 1 green pip).
        weaver.ManaCost.Should().Be("{1}{G}");
        var cost = ManaCost.Parse("{1}{G}");
        cost.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Create_HasCorrectPowerAndToughness()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.BasePower.Should().Be(0);
        weaver.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void Create_HasDryadSubtype()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.HasSubtype(CardSubtype.Dryad).Should().BeTrue(
            because: "Sanctum Weaver is a Dryad");
    }

    [Fact]
    public void Create_HasCreatureCardType()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void Create_IsEnchantmentCreature_HasBothCardTypes()
    {
        // Deferral #10: Enchantment Creatures now carry BOTH the Creature AND
        // Enchantment card types (CR 205.2a) via PermanentBuilders.
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.HasType(CardType.Creature).Should().BeTrue();
        weaver.HasType(CardType.Enchantment).Should().BeTrue(
            because: "Sanctum Weaver is an Enchantment Creature (CR 205.2a)");
    }

    [Fact]
    public void EnchantmentCreature_CountsTowardBothCreaturesAndEnchantments()
    {
        // Deferral #10 regression: an Enchantment Creature counts toward BOTH
        // "creatures you control" AND "enchantments you control".
        var weaver = SanctumWeaverFactory.Create(_alice);
        weaver.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(weaver);

        // Enchantment side (CR 109.2 via HasType(CardType.Enchantment)).
        SanctumWeaverFactory.CountEnchantments(_alice).Should().Be(1,
            because: "Sanctum Weaver is itself an Enchantment Creature");

        // Creature side (it is, and remains, a Creature instance).
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().ContainSingle()
            .Which.Should().BeSameAs(weaver,
                because: "an Enchantment Creature still counts as a creature you control");
    }

    [Fact]
    public void Create_OwnerAndController_AreSet()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.Owner.Should().BeSameAs(_alice);
        weaver.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsCreature()
    {
        var card = NamedCardFactory.Create("Sanctum Weaver", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sanctum Weaver");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasFiveManaAbilities_OnePerColor()
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.Abilities.OfType<SanctumWeaverManaAbility>().Should().HaveCount(5,
            because: "one slot per WUBRG colour");
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void Create_HasManaAbility_ForEachColor(string pip)
    {
        var weaver = SanctumWeaverFactory.Create(_alice);

        weaver.Abilities.OfType<SanctumWeaverManaAbility>()
            .Should().Contain(a => a.ColorPip == pip,
                because: $"Sanctum Weaver produces {pip} among its WUBRG options");
    }

    // -----------------------------------------------------------------------
    // CountEnchantments
    // -----------------------------------------------------------------------

    [Fact]
    public void CountEnchantments_NullController_ReturnsZero()
    {
        SanctumWeaverFactory.CountEnchantments(null).Should().Be(0);
    }

    [Fact]
    public void CountEnchantments_EmptyBattlefield_ReturnsZero()
    {
        SanctumWeaverFactory.CountEnchantments(_alice).Should().Be(0);
    }

    [Fact]
    public void CountEnchantments_CountsPlainEnchantments()
    {
        AddEnchantment("Enchantment 1");
        AddEnchantment("Enchantment 2");

        SanctumWeaverFactory.CountEnchantments(_alice).Should().Be(2);
    }

    [Fact]
    public void CountEnchantments_DoesNotCountCreatures()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        SanctumWeaverFactory.CountEnchantments(_alice).Should().Be(0,
            because: "Creatures do not have CardType.Enchantment");
    }

    [Fact]
    public void CountEnchantments_DoesNotCountLands()
    {
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        SanctumWeaverFactory.CountEnchantments(_alice).Should().Be(0);
    }

    [Fact]
    public void CountEnchantments_CountsWeaverItself()
    {
        // Deferral #10 (CR 205.2a): Sanctum Weaver is an Enchantment Creature —
        // it carries CardType.Enchantment and counts itself.
        PlaceOnBattlefield(); // Weaver on battlefield

        SanctumWeaverFactory.CountEnchantments(_alice).Should().Be(1,
            because: "an Enchantment Creature counts toward 'enchantments you control'");
    }

    [Fact]
    public void CountEnchantments_MixedBattlefield_OnlyCountsEnchantments()
    {
        AddEnchantment("Aura 1");
        AddEnchantment("Aura 2");
        AddEnchantment("Aura 3");

        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);
        creature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(creature);

        var land = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(_alice);
        land.SetController(_alice);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);

        SanctumWeaverFactory.CountEnchantments(_alice).Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Activation: X of chosen colour added; creature taps
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Green_ThreeOtherEnchantments_AddsFourGreen_TapsWeaver()
    {
        // Weaver itself is an Enchantment Creature (Deferral #10), so with 3
        // OTHER enchantments the controller controls 4 enchantments total.
        var weaver = PlaceOnBattlefield();
        AddEnchantment("Enchantment 1");
        AddEnchantment("Enchantment 2");
        AddEnchantment("Enchantment 3");

        var ability = weaver.Abilities.OfType<SanctumWeaverManaAbility>()
            .Single(a => a.ColorPip == "G");

        ability.CanActivate().Should().BeTrue();
        var mana = ability.Activate();

        weaver.IsTapped.Should().BeTrue(because: "the {T} cost was paid");
        mana.Green.Should().Be(4, because: "Weaver + 3 enchantments → 4{G}");
        mana.White.Should().Be(0);
        mana.Blue.Should().Be(0);
        mana.Black.Should().Be(0);
        mana.Red.Should().Be(0);
        mana.Generic.Should().Be(0);
    }

    [Theory]
    [InlineData("W", 2, 0, 0, 0, 0)]
    [InlineData("U", 0, 2, 0, 0, 0)]
    [InlineData("B", 0, 0, 2, 0, 0)]
    [InlineData("R", 0, 0, 0, 2, 0)]
    [InlineData("G", 0, 0, 0, 0, 2)]
    public void Activate_OneOtherEnchantment_ProducesTwoOfChosenColor(
        string pip, int w, int u, int b, int r, int g)
    {
        // Weaver (an Enchantment Creature) + 1 other enchantment = 2.
        var weaver = PlaceOnBattlefield();
        AddEnchantment();

        var ability = weaver.Abilities.OfType<SanctumWeaverManaAbility>()
            .Single(a => a.ColorPip == pip);

        var mana = ability.Activate();

        mana.White.Should().Be(w);
        mana.Blue.Should().Be(u);
        mana.Black.Should().Be(b);
        mana.Red.Should().Be(r);
        mana.Green.Should().Be(g);
        mana.Generic.Should().Be(0);
    }

    [Fact]
    public void Activate_OnlyWeaver_ProducesOne_FromSelfCount()
    {
        // CR 605.1c — activation is always legal. With only the Weaver on the
        // battlefield, X = 1: an Enchantment Creature counts itself (Deferral
        // #10), so the ability produces one mana rather than zero.
        var weaver = PlaceOnBattlefield();
        // No OTHER enchantments on battlefield.

        var ability = weaver.Abilities.OfType<SanctumWeaverManaAbility>()
            .Single(a => a.ColorPip == "G");

        ability.CanActivate().Should().BeTrue(
            because: "activation is always legal per CR 605.1c");

        var mana = ability.Activate();

        weaver.IsTapped.Should().BeTrue(because: "the {T} cost was paid");
        mana.Green.Should().Be(1, because: "the Weaver counts itself as an enchantment");
        mana.TotalValue.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // CanActivate guards
    // -----------------------------------------------------------------------

    [Fact]
    public void CanActivate_FalseWhenAlreadyTapped()
    {
        var weaver = PlaceOnBattlefield();
        weaver.Tap();

        var ability = weaver.Abilities.OfType<SanctumWeaverManaAbility>()
            .Single(a => a.ColorPip == "G");

        ability.CanActivate().Should().BeFalse(
            because: "already tapped — {T} cost cannot be paid");
    }

    [Fact]
    public void CanActivate_FalseWhenSummoningSick()
    {
        // Do NOT clear summoning sickness — Sanctum Weaver just entered.
        var weaver = SanctumWeaverFactory.Create(_alice);
        weaver.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(weaver);
        // HasSummoningSickness defaults to true for creatures.

        var ability = weaver.Abilities.OfType<SanctumWeaverManaAbility>()
            .Single(a => a.ColorPip == "G");

        ability.CanActivate().Should().BeFalse(
            because: "CR 302.6 / 605.3a — summoning-sick creatures cannot tap for mana abilities");
    }

    [Fact]
    public void CanActivate_TrueWhenUntappedAndNoSummoningSickness()
    {
        var weaver = PlaceOnBattlefield();

        var ability = weaver.Abilities.OfType<SanctumWeaverManaAbility>()
            .Single(a => a.ColorPip == "G");

        ability.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // BuildColorMana internal helper
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildColorMana_ZeroOrNegative_ReturnsZero(int n)
    {
        SanctumWeaverFactory.BuildColorMana("G", n).Should().Be(ManaCost.Zero);
    }

    [Theory]
    [InlineData("W", 2, 2, 0, 0, 0, 0)]
    [InlineData("U", 3, 0, 3, 0, 0, 0)]
    [InlineData("B", 1, 0, 0, 1, 0, 0)]
    [InlineData("R", 4, 0, 0, 0, 4, 0)]
    [InlineData("G", 5, 0, 0, 0, 0, 5)]
    public void BuildColorMana_PositiveN_ReturnsCorrectPips(
        string pip, int n, int w, int u, int b, int r, int g)
    {
        var result = SanctumWeaverFactory.BuildColorMana(pip, n);

        result.White.Should().Be(w);
        result.Blue.Should().Be(u);
        result.Black.Should().Be(b);
        result.Red.Should().Be(r);
        result.Green.Should().Be(g);
        result.Generic.Should().Be(0);
    }
}
