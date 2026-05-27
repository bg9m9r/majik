using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HibernationFactory"/>.
///
/// Card: Hibernation — Instant {1}{U} (Visions).
///   "Return all green creatures to their owners' hands."
///
/// Covers:
///   - Identity / dispatch.
///   - All green creatures (any controller) bounced to owner's hand.
///   - Non-green creatures (and non-creature green permanents) untouched.
///   - Hybrid + Phyrexian pip colour detection via CardColors.GetColors.
///   - Token colour via TokenColorsOverride is honoured.
///   - Empty-battlefield no-op.
/// </summary>
public class HibernationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------- Identity + dispatch -----------------------------------------

    [Fact]
    public void Hibernation_Identity()
    {
        var c = HibernationFactory.Create(_alice);

        c.Name.Should().Be("Hibernation");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Hibernation()
    {
        var card = NamedCardFactory.Create("Hibernation", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Hibernation");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
    }

    // -------- Resolve body ------------------------------------------------

    [Fact]
    public void Resolve_BouncesAllGreenCreatures_FromBothBattlefields()
    {
        // Mono-green creatures on both sides.
        var llanowar = PlaceCreatureOnBattlefield("Llanowar Elves", "{G}", 1, 1, _alice);
        var grizzly = PlaceCreatureOnBattlefield("Grizzly Bears", "{1}{G}", 2, 2, _bob);

        HibernationFactory.BuildResolveEffect(new[] { _alice, _bob })
            .ToList().ForEach(e => e.Execute());

        llanowar.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(llanowar);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(llanowar);

        grizzly.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(grizzly);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(grizzly);
    }

    [Fact]
    public void Resolve_LeavesNonGreenCreaturesAlone()
    {
        var savannahLions = PlaceCreatureOnBattlefield("Savannah Lions", "{W}", 2, 1, _alice);
        var goblin = PlaceCreatureOnBattlefield("Raging Goblin", "{R}", 1, 1, _bob);
        var greyOgre = PlaceCreatureOnBattlefield("Grey Ogre", "{2}{R}", 2, 2, _alice);
        var phantomMonster = PlaceCreatureOnBattlefield("Phantom Monster", "{3}{U}", 3, 3, _bob);

        HibernationFactory.BuildResolveEffect(new[] { _alice, _bob })
            .ToList().ForEach(e => e.Execute());

        savannahLions.Zone.Should().Be(ZoneType.Battlefield);
        goblin.Zone.Should().Be(ZoneType.Battlefield);
        greyOgre.Zone.Should().Be(ZoneType.Battlefield);
        phantomMonster.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_IgnoresNonCreatureGreenPermanents()
    {
        // Green enchantment / artifact / land — Hibernation only targets creatures.
        var oracle = new Enchantment("Oracle of Mul Daya", "{3}{G}");
        oracle.SetOwner(_alice);
        oracle.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(oracle);
        oracle.SetZone(ZoneType.Battlefield);

        HibernationFactory.BuildResolveEffect(new[] { _alice, _bob })
            .ToList().ForEach(e => e.Execute());

        oracle.Zone.Should().Be(ZoneType.Battlefield,
            "Hibernation only returns creatures, not all green permanents");
    }

    [Fact]
    public void Resolve_BouncesHybridGreenCreature()
    {
        // {G/W} pip — colour set includes both Green and White; counts as green.
        var kitchenFinks = PlaceCreatureOnBattlefield("Kitchen Finks", "{1}{G/W}", 3, 2, _alice);

        HibernationFactory.BuildResolveEffect(new[] { _alice })
            .ToList().ForEach(e => e.Execute());

        kitchenFinks.Zone.Should().Be(ZoneType.Hand,
            "hybrid {G/W} pip contributes Green and White colors — Hibernation hits it");
    }

    [Fact]
    public void Resolve_BouncesPhyrexianGreenCreature()
    {
        // {G/P} pip — Phyrexian green; CardColors counts it as green.
        var birthingPod = PlaceCreatureOnBattlefield("Glistener Elf", "{G/P}", 1, 1, _alice);

        HibernationFactory.BuildResolveEffect(new[] { _alice })
            .ToList().ForEach(e => e.Execute());

        birthingPod.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_GreenTokenViaOverride_IsBounced()
    {
        // Tokens have no printed mana cost — colour comes from TokenColorsOverride.
        var saproling = new Creature("Saproling", "", 1, 1);
        saproling.SetOwner(_alice);
        saproling.SetController(_alice);
        saproling.SetTokenColors(new[] { Majik.Core.ValueObjects.ManaColor.Green });
        _alice.Zones.Battlefield.AddCard(saproling);
        saproling.SetZone(ZoneType.Battlefield);

        HibernationFactory.BuildResolveEffect(new[] { _alice })
            .ToList().ForEach(e => e.Execute());

        saproling.Zone.Should().Be(ZoneType.Hand,
            "TokenColorsOverride contributes Green — Hibernation hits it (CR 111.4)");
    }

    [Fact]
    public void Resolve_EmptyBattlefield_NoOp()
    {
        // No creatures at all — resolve should be a clean no-op (no exception,
        // no spurious zone moves).
        var act = () => HibernationFactory.BuildResolveEffect(new[] { _alice, _bob })
            .ToList().ForEach(e => e.Execute());

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_MultiColorGreenCreature_IsBounced()
    {
        // Tarmogoyf — {1}{G}, mono-green. Trick is the colour-detection
        // path correctly reads green pips.
        var goyf = PlaceCreatureOnBattlefield("Tarmogoyf", "{1}{G}", 1, 2, _alice);
        // A multi-colour green creature: Mantis Rider {U}{R}{W} — no green pip.
        var mantis = PlaceCreatureOnBattlefield("Mantis Rider", "{U}{R}{W}", 3, 3, _bob);

        HibernationFactory.BuildResolveEffect(new[] { _alice, _bob })
            .ToList().ForEach(e => e.Execute());

        goyf.Zone.Should().Be(ZoneType.Hand);
        mantis.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -------- Helpers ----------------------------------------------------

    private static Creature PlaceCreatureOnBattlefield(
        string name, string cost, int power, int toughness, Player owner)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
