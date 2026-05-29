using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Thalia, Heretic Cathar (Eldritch Moon, {2}{W}).
///
/// Oracle:
///   "First strike
///    Creatures and nonbasic lands your opponents control enter tapped."
///
/// Coverage:
///   * Identity: Legendary 3/2 Human Soldier {2}{W} with First strike.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * Opponent's creatures enter tapped while Thalia is on the battlefield.
///   * Opponent's nonbasic lands enter tapped (CR 305.6).
///   * Opponent's BASIC lands are unaffected.
///   * Thalia's controller's own creatures / lands are unaffected (CR 109.5
///     — one-sided "your opponents control").
///   * Thalia leaving the battlefield unregisters the replacement.
///   * Single-arg path registers nothing.
/// </summary>
public class ThaliaHereticCatharTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;

    public ThaliaHereticCatharTests()
    {
        _zones = new ZoneService(_bus, _replacements);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var thalia = ThaliaHereticCatharFactory.Create(_alice);

        thalia.Name.Should().Be("Thalia, Heretic Cathar");
        thalia.HasType(CardType.Creature).Should().BeTrue();
        thalia.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        thalia.HasSubtype(CardSubtype.Human).Should().BeTrue();
        thalia.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        thalia.ManaCost.Should().Be("{2}{W}");
        thalia.ManaCostValue.Generic.Should().Be(2);
        thalia.ManaCostValue.White.Should().Be(1);
        thalia.Power.Should().Be(3);
        thalia.Toughness.Should().Be(2);
        thalia.Owner.Should().BeSameAs(_alice);
        thalia.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasFirstStrikeKeyword()
    {
        var thalia = ThaliaHereticCatharFactory.Create(_alice);

        thalia.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k =>
                k.Keyword.Equals("First strike", StringComparison.OrdinalIgnoreCase),
                "CR 702.7 — First strike keyword marker must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsThaliaShape()
    {
        var card = NamedCardFactory.Create("Thalia, Heretic Cathar", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Thalia, Heretic Cathar");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ((Creature)card).Power.Should().Be(3);
        ((Creature)card).Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Opponent enters-tapped static (CR 614.1c / CR 109.5 / CR 305.6)
    // -----------------------------------------------------------------------

    private Creature ThaliaOnBattlefield()
    {
        var thalia = ThaliaHereticCatharFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(thalia);
        thalia.SetZone(ZoneType.Library);
        _zones.MoveCard(thalia, ZoneType.Library, ZoneType.Battlefield);
        return thalia;
    }

    [Fact]
    public void OpponentCreature_EntersTapped_WhileThaliaIsOut()
    {
        ThaliaOnBattlefield();

        // Bob (Alice's opponent) plays a creature.
        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        _bob.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        _zones.MoveCard(goblin, ZoneType.Hand, ZoneType.Battlefield, _bob);

        goblin.IsTapped.Should().BeTrue(
            "Thalia makes opponents' creatures enter tapped (CR 614.1c)");
    }

    [Fact]
    public void OpponentNonbasicLand_EntersTapped()
    {
        ThaliaOnBattlefield();

        // A nonbasic land Bob plays (no Basic supertype).
        var nonbasic = new Land("Steam Vents");
        nonbasic.SetOwner(_bob);
        nonbasic.SetController(_bob);
        _bob.Zones.Hand.AddCard(nonbasic);
        nonbasic.SetZone(ZoneType.Hand);

        _zones.MoveCard(nonbasic, ZoneType.Hand, ZoneType.Battlefield, _bob);

        nonbasic.IsTapped.Should().BeTrue(
            "Thalia makes opponents' nonbasic lands enter tapped (CR 305.6)");
    }

    [Fact]
    public void OpponentBasicLand_EntersUntapped()
    {
        ThaliaOnBattlefield();

        // A basic land — has the Basic supertype, so Thalia does NOT tap it.
        var island = new Land("Island", supertypes: new[] { CardSupertype.Basic });
        island.SetOwner(_bob);
        island.SetController(_bob);
        _bob.Zones.Hand.AddCard(island);
        island.SetZone(ZoneType.Hand);

        _zones.MoveCard(island, ZoneType.Hand, ZoneType.Battlefield, _bob);

        island.IsTapped.Should().BeFalse(
            "basic lands are NOT affected — printed text says 'nonbasic lands' (CR 305.6)");
    }

    [Fact]
    public void ControllerOwnCreature_EntersUntapped_OneSided()
    {
        ThaliaOnBattlefield();

        // Alice (Thalia's controller) plays her own creature.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        _zones.MoveCard(bear, ZoneType.Hand, ZoneType.Battlefield, _alice);

        bear.IsTapped.Should().BeFalse(
            "Thalia is one-sided — only 'your opponents control' permanents enter tapped (CR 109.5)");
    }

    [Fact]
    public void ControllerOwnNonbasicLand_EntersUntapped_OneSided()
    {
        ThaliaOnBattlefield();

        var ownNonbasic = new Land("Steam Vents");
        ownNonbasic.SetOwner(_alice);
        ownNonbasic.SetController(_alice);
        _alice.Zones.Hand.AddCard(ownNonbasic);
        ownNonbasic.SetZone(ZoneType.Hand);

        _zones.MoveCard(ownNonbasic, ZoneType.Hand, ZoneType.Battlefield, _alice);

        ownNonbasic.IsTapped.Should().BeFalse(
            "Thalia's controller's own nonbasic lands enter untapped (CR 109.5)");
    }

    [Fact]
    public void ThaliaLeavesBattlefield_ReplacementUnregisters()
    {
        var thalia = ThaliaOnBattlefield();

        // Fires while Thalia is out.
        var goblinBefore = new Creature("Goblin Guide", "{R}", 2, 2);
        goblinBefore.SetOwner(_bob);
        goblinBefore.SetController(_bob);
        _bob.Zones.Hand.AddCard(goblinBefore);
        goblinBefore.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinBefore, ZoneType.Hand, ZoneType.Battlefield, _bob);
        goblinBefore.IsTapped.Should().BeTrue();

        // Thalia dies.
        _zones.MoveCard(thalia, ZoneType.Battlefield, ZoneType.Graveyard);

        // A fresh opponent creature now enters untapped.
        var goblinAfter = new Creature("Goblin Guide", "{R}", 2, 2);
        goblinAfter.SetOwner(_bob);
        goblinAfter.SetController(_bob);
        _bob.Zones.Hand.AddCard(goblinAfter);
        goblinAfter.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinAfter, ZoneType.Hand, ZoneType.Battlefield, _bob);

        goblinAfter.IsTapped.Should().BeFalse(
            "replacement must be removed when Thalia leaves the battlefield");
    }

    [Fact]
    public void SingleArgPath_RegistersNoReplacement()
    {
        // Single-arg path — no replacement bus, so no registration.
        ThaliaHereticCatharFactory.Create(_alice);

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        var intent = new ZoneMoveIntent(
            goblin, ZoneType.Hand, ZoneType.Battlefield, Controller: _bob);

        var emptyBus = new ReplacementBus();
        emptyBus.Apply(intent)!.EntersTapped.Should().BeFalse(
            "no replacement is registered on the single-arg path");
    }
}
