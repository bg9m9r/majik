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
/// Tests for Authority of the Consuls (Kaladesh, {W} Enchantment).
///
/// Oracle:
///   "Creatures your opponents control enter tapped.
///    Whenever a creature an opponent controls enters, you gain 1 life."
///
/// Coverage (UNIQUE behaviour only — CardFactoryContractTests covers
/// dispatch + well-formedness):
///   * Identity: {W} Enchantment.
///   * Opponent's creatures enter tapped while Authority is on the battlefield
///     (CR 614.1c).
///   * Authority's controller's own creatures enter untapped (CR 109.5 —
///     one-sided "your opponents control").
///   * Opponent's non-creature permanents are unaffected (creatures only).
///   * Authority leaving the battlefield unregisters the replacement.
///   * Single-arg path registers no replacement.
///   * Lifegain trigger: matches an opponent's creature entering; does NOT
///     match the controller's own creature or a non-creature; resolution gains
///     1 life.
/// </summary>
[Trait("Color", "W")]
public class AuthorityOfTheConsulsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;

    public AuthorityOfTheConsulsTests()
    {
        _zones = new ZoneService(_bus, _replacements);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Authority_Identity()
    {
        var c = AuthorityOfTheConsulsFactory.Create(_alice);

        c.Name.Should().Be("Authority of the Consuls");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.ManaCostValue.White.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Opponent enters-tapped static (CR 614.1c / CR 109.5)
    // -----------------------------------------------------------------------

    private Enchantment AuthorityOnBattlefield()
    {
        var authority = AuthorityOfTheConsulsFactory.Create(
            _alice, _replacements, _bus, triggers: null);
        _alice.Zones.Library.AddCard(authority);
        authority.SetZone(ZoneType.Library);
        _zones.MoveCard(authority, ZoneType.Library, ZoneType.Battlefield);
        return authority;
    }

    [Fact]
    public void OpponentCreature_EntersTapped_WhileAuthorityIsOut()
    {
        AuthorityOnBattlefield();

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        _bob.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        _zones.MoveCard(goblin, ZoneType.Hand, ZoneType.Battlefield, _bob);

        goblin.IsTapped.Should().BeTrue(
            "Authority makes opponents' creatures enter tapped (CR 614.1c)");
    }

    [Fact]
    public void ControllerOwnCreature_EntersUntapped_OneSided()
    {
        AuthorityOnBattlefield();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        _zones.MoveCard(bear, ZoneType.Hand, ZoneType.Battlefield, _alice);

        bear.IsTapped.Should().BeFalse(
            "Authority is one-sided — only 'your opponents control' creatures "
            + "enter tapped (CR 109.5)");
    }

    [Fact]
    public void OpponentNonCreaturePermanent_EntersUntapped_CreaturesOnly()
    {
        AuthorityOnBattlefield();

        // Authority only affects creatures — an opponent's nonbasic land is
        // unaffected (unlike Thalia, Heretic Cathar).
        var nonbasic = new Land("Steam Vents");
        nonbasic.SetOwner(_bob);
        nonbasic.SetController(_bob);
        _bob.Zones.Hand.AddCard(nonbasic);
        nonbasic.SetZone(ZoneType.Hand);

        _zones.MoveCard(nonbasic, ZoneType.Hand, ZoneType.Battlefield, _bob);

        nonbasic.IsTapped.Should().BeFalse(
            "Authority only taps creatures, not lands");
    }

    [Fact]
    public void AuthorityLeavesBattlefield_ReplacementUnregisters()
    {
        var authority = AuthorityOnBattlefield();

        var goblinBefore = new Creature("Goblin Guide", "{R}", 2, 2);
        goblinBefore.SetOwner(_bob);
        goblinBefore.SetController(_bob);
        _bob.Zones.Hand.AddCard(goblinBefore);
        goblinBefore.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinBefore, ZoneType.Hand, ZoneType.Battlefield, _bob);
        goblinBefore.IsTapped.Should().BeTrue();

        _zones.MoveCard(authority, ZoneType.Battlefield, ZoneType.Graveyard);

        var goblinAfter = new Creature("Goblin Guide", "{R}", 2, 2);
        goblinAfter.SetOwner(_bob);
        goblinAfter.SetController(_bob);
        _bob.Zones.Hand.AddCard(goblinAfter);
        goblinAfter.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinAfter, ZoneType.Hand, ZoneType.Battlefield, _bob);

        goblinAfter.IsTapped.Should().BeFalse(
            "replacement must be removed when Authority leaves the battlefield");
    }

    [Fact]
    public void SingleArgPath_RegistersNoReplacement()
    {
        AuthorityOfTheConsulsFactory.Create(_alice);

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        var intent = new ZoneMoveIntent(
            goblin, ZoneType.Hand, ZoneType.Battlefield, Controller: _bob);

        var emptyBus = new ReplacementBus();
        emptyBus.Apply(intent)!.EntersTapped.Should().BeFalse(
            "no replacement is registered on the single-arg path");
    }

    // -----------------------------------------------------------------------
    // Opponent-creature-ETB lifegain trigger (CR 603.6e / CR 119.3 / CR 109.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void Lifegain_OpponentCreatureEnters_TriggerMatches()
    {
        var authority = AuthorityOfTheConsulsFactory.Create(_alice);
        authority.SetZone(ZoneType.Battlefield);

        var oppCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        oppCreature.SetOwner(_bob);
        oppCreature.SetController(_bob);

        var trigger = authority.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            oppCreature, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Authority fires when a creature an opponent controls enters");
    }

    [Fact]
    public void Lifegain_OwnCreatureEnters_DoesNotMatch()
    {
        var authority = AuthorityOfTheConsulsFactory.Create(_alice);
        authority.SetZone(ZoneType.Battlefield);

        var ownCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ownCreature.SetOwner(_alice);
        ownCreature.SetController(_alice);

        var trigger = authority.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            ownCreature, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Authority's trigger is opponent-scoped — your own creatures "
            + "don't fire it (CR 109.5)");
    }

    [Fact]
    public void Lifegain_OpponentNonCreatureEnters_DoesNotMatch()
    {
        var authority = AuthorityOfTheConsulsFactory.Create(_alice);
        authority.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Mox Pearl", "{0}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);

        var trigger = authority.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            artifact, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Authority's trigger only fires on a creature entering");
    }

    [Fact]
    public void Lifegain_OnResolve_ControllerGainsOneLife()
    {
        var authority = AuthorityOfTheConsulsFactory.Create(_alice);
        authority.SetZone(ZoneType.Battlefield);

        var trigger = authority.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(21,
            "Authority gains its controller 1 life (CR 119.3)");
    }
}
