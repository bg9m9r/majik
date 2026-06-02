using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ImperiousPerfectFactory"/> (Lorwyn, {2}{G}).
/// Creature — Elf Warrior 2/2. Oracle text (verified against Scryfall):
///   "Other Elves you control get +1/+1.
///    {G}, {T}: Create a 1/1 green Elf Warrior creature token."
///
/// Covers:
/// - Identity (Elf Warrior, mana cost, P/T, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Anthem static: other controller-Elves get +1/+1; self NOT pumped
///   (CR 613.1g "Other"); opponent's Elf and non-Elf creatures unaffected.
/// - Token-minting <see cref="ActivatedAbility"/> shape + cost ({G} + tap).
/// - <see cref="ImperiousPerfectFactory.CreateElfWarriorToken"/> builds a 1/1
///   green Elf Warrior creature token.
/// </summary>
[Trait("Color", "G")]
public class ImperiousPerfectFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeElf(Player owner, string name = "Llanowar Elves")
    {
        var c = new Creature(name, "{G}", 1, 1, subtypes: new[] { CardSubtype.Elf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonElf(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ImperiousPerfect_Identity()
    {
        var card = ImperiousPerfectFactory.Create(_alice);

        card.Name.Should().Be("Imperious Perfect");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ImperiousPerfect_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Imperious Perfect", _alice);

        card.Should().BeOfType<Creature>("the [CardName] factory is registered");
        card.Name.Should().Be("Imperious Perfect");
    }

    // -----------------------------------------------------------------------
    // Anthem static — "Other Elves you control get +1/+1."
    // -----------------------------------------------------------------------

    [Fact]
    public void ImperiousPerfect_BuffsOtherControllerElf_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherElf = MakeElf(_alice, "Llanowar Elves");
        otherElf.ActiveEffects = svc;

        var perfect = ImperiousPerfectFactory.Create(_alice, svc, zoneService: null);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        otherElf.GetPower().Should().Be(2,
            "other Elves controlled by Imperious Perfect's controller get +1/+1 (1 → 2).");
        otherElf.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ImperiousPerfect_DoesNotBuffItself()
    {
        var svc = new ContinuousEffectsService();

        var perfect = ImperiousPerfectFactory.Create(_alice, svc, zoneService: null);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        perfect.GetPower().Should().Be(2,
            "printed 'Other Elves' excludes Imperious Perfect itself (CR 613.1g).");
        perfect.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ImperiousPerfect_DoesNotBuffOpponentElf()
    {
        var svc = new ContinuousEffectsService();

        var bobElf = MakeElf(_bob, "Heritage Druid");
        bobElf.ActiveEffects = svc;

        var perfect = ImperiousPerfectFactory.Create(_alice, svc, zoneService: null);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        bobElf.GetPower().Should().Be(1,
            "controller-scoped anthem — Bob's Elves are unaffected (allPlayers: false).");
        bobElf.GetToughness().Should().Be(1);
    }

    [Fact]
    public void ImperiousPerfect_DoesNotBuffNonElfCreature()
    {
        var svc = new ContinuousEffectsService();

        var bears = MakeNonElf(_alice);
        bears.ActiveEffects = svc;

        var perfect = ImperiousPerfectFactory.Create(_alice, svc, zoneService: null);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        bears.GetPower().Should().Be(2,
            "matching subtype = Elf only; non-Elf creatures aren't buffed.");
        bears.GetToughness().Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Token-minting activated ability — "{G}, {T}: Create ... token."
    // -----------------------------------------------------------------------

    [Fact]
    public void ImperiousPerfect_HasOneActivatedAbility_WithManaAndTapCost()
    {
        var card = ImperiousPerfectFactory.Create(_alice);

        var ability = card.Abilities.OfType<ActivatedAbility>().Should()
            .ContainSingle("the {G}, {T} token ability is attached").Subject;

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activated cost includes the {G} mana payment")
            .Which.Cost.ToString().Should().Be("G");
        ability.Costs.Any(c => c.Description.Contains("Tap", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the activated cost includes the tap symbol {T}");
    }

    // -----------------------------------------------------------------------
    // Elf Warrior token shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateElfWarriorToken_Builds_1_1_Green_ElfWarrior()
    {
        var token = ImperiousPerfectFactory.CreateElfWarriorToken(_alice);

        token.Name.Should().Be("Elf Warrior");
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.IsToken.Should().BeTrue();
        token.HasType(CardType.Creature).Should().BeTrue();
        token.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        token.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        CardColors.GetColors(token).Should().Contain(ManaColor.Green,
            "the printed token is a 1/1 green Elf Warrior creature token");
        token.Owner.Should().BeSameAs(_alice);
        token.Controller.Should().BeSameAs(_alice);
        token.Zone.Should().Be(ZoneType.Battlefield,
            "the Elf Warrior token enters the battlefield directly (CR 111.6)");
    }

    [Fact]
    public void ImperiousPerfect_TokenAbilityEffect_MintsOneElfWarrior()
    {
        var perfect = ImperiousPerfectFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(perfect);
        perfect.SetZone(ZoneType.Battlefield);

        var ability = perfect.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Elf Warrior")
            .ToList();

        tokens.Should().HaveCount(1, "the activated ability mints exactly one Elf Warrior token");
        tokens[0].Power.Should().Be(1);
        tokens[0].Toughness.Should().Be(1);
        tokens[0].IsToken.Should().BeTrue();
        tokens[0].HasSubtype(CardSubtype.Elf).Should().BeTrue();
        tokens[0].HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        CardColors.GetColors(tokens[0]).Should().Contain(ManaColor.Green);
    }
}
