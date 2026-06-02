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
/// Tests for <see cref="DwynensEliteFactory"/> (Magic Origins, {1}{G}).
/// Creature — Elf Warrior 2/2. Oracle text (verified against Scryfall):
///   "When this creature enters, if you control another Elf, create a 1/1
///    green Elf Warrior creature token."
///
/// Covers:
/// - Identity (Elf Warrior, mana cost, P/T, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB <see cref="TriggeredAbility"/> shape.
/// - <see cref="DwynensEliteFactory.CreateElfWarriorToken"/> builds a 1/1 green
///   Elf Warrior creature token.
/// - Intervening-if (CR 603.4): the ETB effect mints a token ONLY when the
///   controller controls another Elf, and no-ops when it does not.
/// </summary>
[Trait("Color", "G")]
public class DwynensEliteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DwynensElite_Identity()
    {
        var card = DwynensEliteFactory.Create(_alice);

        card.Name.Should().Be("Dwynen's Elite");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void DwynensElite_HasOneEtbTrigger()
    {
        var card = DwynensEliteFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB token trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Elf Warrior token shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateElfWarriorToken_Builds_1_1_Green_ElfWarrior()
    {
        var token = DwynensEliteFactory.CreateElfWarriorToken(_alice);

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

    // -----------------------------------------------------------------------
    // Intervening-if (CR 603.4) — token only when controlling another Elf.
    // -----------------------------------------------------------------------

    [Fact]
    public void DwynensElite_EtbEffect_NoToken_WhenNoOtherElf()
    {
        var elite = DwynensEliteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elite);
        elite.SetZone(ZoneType.Battlefield);

        var trigger = elite.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Only Dwynen's Elite is on the battlefield — it does not count as
        // "another Elf", so no token is minted (CR 603.4 intervening-if false).
        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Elf Warrior")
            .ToList();

        tokens.Should().BeEmpty(
            "Dwynen's Elite controls no OTHER Elf, so the intervening-if fails");
    }

    [Fact]
    public void DwynensElite_EtbEffect_CreatesToken_WhenAnotherElfControlled()
    {
        var elite = DwynensEliteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elite);
        elite.SetZone(ZoneType.Battlefield);

        // Put a second Elf on the battlefield so "another Elf" is satisfied.
        var otherElf = new Creature(
            name: "Llanowar Elves",
            manaCost: "{G}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Elf });
        otherElf.SetOwner(_alice);
        otherElf.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(otherElf);
        otherElf.SetZone(ZoneType.Battlefield);

        var trigger = elite.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Elf Warrior")
            .ToList();

        tokens.Should().HaveCount(1,
            "the controller controls another Elf, so the ETB mints one token");
        tokens[0].Power.Should().Be(1);
        tokens[0].Toughness.Should().Be(1);
        tokens[0].IsToken.Should().BeTrue();
        tokens[0].HasSubtype(CardSubtype.Elf).Should().BeTrue();
        tokens[0].HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        CardColors.GetColors(tokens[0]).Should().Contain(ManaColor.Green);
    }
}
