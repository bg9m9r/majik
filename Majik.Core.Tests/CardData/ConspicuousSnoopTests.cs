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
/// Tests for Conspicuous Snoop (Jumpstart, {R}).
///
/// Covers the v1 simplified scope:
///   - Card identity (name, mana cost, P/T, Goblin + Rogue subtypes).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Three static-ability "riders" describe the three oracle clauses
///     (play with top revealed, may cast top if Goblin, has activated
///     abilities of top if Goblin). Wired as description-only static
///     abilities pending continuous-effect / cast-from-zone primitives.
///   - <see cref="ConspicuousSnoopFactory.LookAtTopOfLibrary"/> helper:
///     returns top of library for the controller (or null on empty).
///   - <see cref="ConspicuousSnoopFactory.IsTopOfLibraryGoblin"/> helper:
///     true when the top card has the Goblin subtype; false otherwise.
///
/// Deferred items (documented in factory class doc):
///   - Public reveal of top card to opponents.
///   - "May cast top from library" cast-from-zone permission.
///   - Layer 6 activated-ability copy from top card.
/// </summary>
public class ConspicuousSnoopTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ConspicuousSnoop_Is_GoblinRogueCreature_1_1_AtCostR()
    {
        var snoop = ConspicuousSnoopFactory.Create(_alice);

        snoop.Name.Should().Be("Conspicuous Snoop");
        snoop.ManaCost.Should().Be("{R}");
        snoop.HasType(CardType.Creature).Should().BeTrue();
        snoop.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        snoop.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        snoop.BasePower.Should().Be(1);
        snoop.BaseToughness.Should().Be(1);
        snoop.Owner.Should().BeSameAs(_alice);
        snoop.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ConspicuousSnoop()
    {
        var card = NamedCardFactory.Create("Conspicuous Snoop", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Conspicuous Snoop");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<StaticAbility>().Should().HaveCount(3,
            "three oracle riders are wired as description-only static abilities");
    }

    [Fact]
    public void ConspicuousSnoop_HasThree_StaticRiders_WithPrintedDescriptions()
    {
        var snoop = ConspicuousSnoopFactory.Create(_alice);

        var statics = snoop.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().HaveCount(3);

        statics.Select(s => s.Description).Should().Contain(new[]
        {
            ConspicuousSnoopFactory.PlayRevealedDescription,
            ConspicuousSnoopFactory.MayCastGoblinDescription,
            ConspicuousSnoopFactory.CopyActivatedAbilitiesDescription,
        });
    }

    [Fact]
    public void LookAtTopOfLibrary_EmptyLibrary_ReturnsNull()
    {
        var alice = new Player("Alice", 20);
        ConspicuousSnoopFactory.LookAtTopOfLibrary(alice).Should().BeNull();
    }

    [Fact]
    public void LookAtTopOfLibrary_ReturnsFirstCardInLibrary()
    {
        var alice = new Player("Alice", 20);

        var goblinGuide = new Creature(
            name: "Goblin Guide",
            manaCost: "{R}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinGuide.SetOwner(alice);
        alice.Zones.Library.AddCard(goblinGuide);
        goblinGuide.SetZone(ZoneType.Library);

        var bait = new Card("Random Card", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        ConspicuousSnoopFactory.LookAtTopOfLibrary(alice).Should().BeSameAs(goblinGuide,
            "first card added is the top of the library");
    }

    [Fact]
    public void IsTopOfLibraryGoblin_TrueWhenGoblinOnTop()
    {
        var alice = new Player("Alice", 20);

        var goblinGuide = new Creature(
            name: "Goblin Guide",
            manaCost: "{R}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinGuide.SetOwner(alice);
        alice.Zones.Library.AddCard(goblinGuide);
        goblinGuide.SetZone(ZoneType.Library);

        ConspicuousSnoopFactory.IsTopOfLibraryGoblin(alice).Should().BeTrue();
    }

    [Fact]
    public void IsTopOfLibraryGoblin_FalseWhenNonGoblinOnTop()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(alice);
        alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        ConspicuousSnoopFactory.IsTopOfLibraryGoblin(alice).Should().BeFalse();
    }

    [Fact]
    public void IsTopOfLibraryGoblin_FalseWhenLibraryEmpty()
    {
        var alice = new Player("Alice", 20);
        ConspicuousSnoopFactory.IsTopOfLibraryGoblin(alice).Should().BeFalse(
            "no top card = no Goblin");
    }
}
