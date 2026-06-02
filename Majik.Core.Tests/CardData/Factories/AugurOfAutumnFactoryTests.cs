using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Augur of Autumn (Innistrad: Midnight Hunt, {1}{G}{G}).
///
/// Covers the v1 simplified scope (mirrors <see cref="ConspicuousSnoopFactory"/>):
///   - Card identity (name, mana cost, P/T, Human + Druid subtypes), loaded
///     from the embedded JSON definition.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Three static-ability "riders" describe the three oracle clauses
///     (look at top any time, play lands from top, Coven cast creatures from
///     top). Wired as description-only static abilities pending the
///     cast-from-zone permission / Coven primitives.
///   - <see cref="AugurOfAutumnFactory.LookAtTopOfLibrary"/> helper: returns
///     top of library for the controller (or null on empty).
///   - <see cref="AugurOfAutumnFactory.HasCoven"/> helper: true when the
///     controller controls three or more creatures with different powers.
///
/// Deferred items (documented in factory class doc): live "play lands from
/// top" / "cast creature spells from top" cast-from-zone permission and the
/// continuous Coven grant.
/// </summary>
public class AugurOfAutumnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeCreature(Player owner, string name, int power)
    {
        var c = new Creature(name, "{G}", power, power, subtypes: new[] { CardSubtype.Druid });
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Fact]
    public void AugurOfAutumn_Is_HumanDruidCreature_2_3_AtCost1GG()
    {
        var augur = AugurOfAutumnFactory.Create(_alice);

        augur.Name.Should().Be("Augur of Autumn");
        augur.ManaCost.Should().Be("{1}{G}{G}");
        augur.HasType(CardType.Creature).Should().BeTrue();
        augur.HasSubtype(CardSubtype.Human).Should().BeTrue();
        augur.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        augur.BasePower.Should().Be(2);
        augur.BaseToughness.Should().Be(3);
        augur.Owner.Should().BeSameAs(_alice);
        augur.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AugurOfAutumn()
    {
        var card = NamedCardFactory.Create("Augur of Autumn", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Augur of Autumn");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Abilities.OfType<StaticAbility>().Should().HaveCount(3,
            "three oracle riders are wired as description-only static abilities");
    }

    [Fact]
    public void AugurOfAutumn_HasThree_StaticRiders_WithPrintedDescriptions()
    {
        var augur = AugurOfAutumnFactory.Create(_alice);

        var statics = augur.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().HaveCount(3);

        statics.Select(s => s.Description).Should().Contain(new[]
        {
            AugurOfAutumnFactory.LookAtTopDescription,
            AugurOfAutumnFactory.PlayLandsFromTopDescription,
            AugurOfAutumnFactory.CovenCastFromTopDescription,
        });
    }

    [Fact]
    public void LookAtTopOfLibrary_EmptyLibrary_ReturnsNull()
    {
        var alice = new Player("Alice", 20);
        AugurOfAutumnFactory.LookAtTopOfLibrary(alice).Should().BeNull();
    }

    [Fact]
    public void LookAtTopOfLibrary_ReturnsFirstCardInLibrary()
    {
        var alice = new Player("Alice", 20);

        var forest = new Card("Forest", "");
        forest.SetOwner(alice);
        alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var bait = new Card("Random Card", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        AugurOfAutumnFactory.LookAtTopOfLibrary(alice).Should().BeSameAs(forest,
            "first card added is the top of the library");
    }

    [Fact]
    public void HasCoven_TrueWith_ThreeCreatures_DifferentPowers()
    {
        var alice = new Player("Alice", 20);

        foreach (var (name, pow) in new[] { ("A", 1), ("B", 2), ("C", 3) })
        {
            var c = MakeCreature(alice, name, pow);
            alice.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
        }

        AugurOfAutumnFactory.HasCoven(alice).Should().BeTrue(
            "three creatures with distinct powers satisfy Coven");
    }

    [Fact]
    public void HasCoven_FalseWith_ThreeCreatures_SomeSharedPowers()
    {
        var alice = new Player("Alice", 20);

        // Three creatures but only two distinct powers (2, 2, 5).
        foreach (var (name, pow) in new[] { ("A", 2), ("B", 2), ("C", 5) })
        {
            var c = MakeCreature(alice, name, pow);
            alice.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
        }

        AugurOfAutumnFactory.HasCoven(alice).Should().BeFalse(
            "Coven needs three or more DIFFERENT powers, not just three creatures");
    }

    [Fact]
    public void HasCoven_FalseWith_TwoCreatures()
    {
        var alice = new Player("Alice", 20);

        foreach (var (name, pow) in new[] { ("A", 1), ("B", 4) })
        {
            var c = MakeCreature(alice, name, pow);
            alice.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
        }

        AugurOfAutumnFactory.HasCoven(alice).Should().BeFalse(
            "fewer than three creatures cannot satisfy Coven");
    }

    [Fact]
    public void HasCoven_FalseWith_NoCreatures()
    {
        var alice = new Player("Alice", 20);
        AugurOfAutumnFactory.HasCoven(alice).Should().BeFalse();
    }
}
