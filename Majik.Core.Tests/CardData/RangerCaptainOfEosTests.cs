using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Ranger-Captain of Eos — Creature — Human Soldier Ranger
/// {1}{W}{W} 3/3 (Modern Horizons).
///
/// Covers:
/// - Card identity (P/T, subtype, mana cost) + dispatcher routing.
/// - ETB tutor: searches the controller's library for the first creature
///   card with mana value ≤ 1 and moves it to hand (CR 603.6a / 701.19a).
/// - Sacrifice activated ability: registers a turn-scoped noncreature-spell
///   restriction against each opponent (CR 601.3); validator rejects
///   noncreature casts; creature casts still pass.
/// - Restriction clears via <see cref="TurnEndedEvent"/> when an event
///   bus is supplied (CR 514.2).
/// </summary>
public class RangerCaptainOfEosTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RangerCaptainOfEosTests()
    {
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RangerCaptain_HasCorrectIdentity_AndPT_AndSubtypes()
    {
        var rc = RangerCaptainOfEosFactory.Create(_alice);

        rc.Name.Should().Be("Ranger-Captain of Eos");
        rc.ManaCost.Should().Be("{1}{W}{W}");
        rc.Power.Should().Be(3);
        rc.Toughness.Should().Be(3);
        rc.HasType(CardType.Creature).Should().BeTrue();
        rc.HasSubtype(CardSubtype.Human).Should().BeTrue();
        rc.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        rc.HasSubtype(CardSubtype.Ranger).Should().BeTrue();
        rc.Owner.Should().BeSameAs(_alice);
        rc.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesRangerCaptain_ToFactory()
    {
        var card = NamedCardFactory.Create("Ranger-Captain of Eos", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ranger-Captain of Eos");
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.HasSubtype(CardSubtype.Ranger).Should().BeTrue();
        ((Creature)card).Power.Should().Be(3);
        ((Creature)card).Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // ETB tutor (CR 603.6a / 701.19a)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTutor_OnResolve_MovesMvLeq1Creature_FromLibrary_ToHand()
    {
        var rc = RangerCaptainOfEosFactory.Create(_alice);

        // Seed library with one mv-≤-1 creature + one too-expensive creature.
        var savage = new Creature("Savannah Lions", "{W}", 2, 1);
        savage.SetOwner(_alice);
        _alice.Zones.Library.AddCard(savage);
        savage.SetZone(ZoneType.Library);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var trigger = rc.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(savage);
        _alice.Zones.Library.GetCards().Should().NotContain(savage);
        // Bear stays in library — mv=2 fails the ≤1 gate.
        _alice.Zones.Library.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void EtbTutor_OnResolve_NoEligibleCard_LeavesHandUntouched()
    {
        var rc = RangerCaptainOfEosFactory.Create(_alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var trigger = rc.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(bear);
    }

    // -----------------------------------------------------------------------
    // Sacrifice activated ability (CR 602 / 601.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void SacAbility_OnResolve_RegistersNoncreatureRestriction_AgainstEachOpponent()
    {
        var rc = RangerCaptainOfEosFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(rc);
        rc.SetZone(ZoneType.Battlefield);

        var ability = rc.Abilities.OfType<ActivatedAbility>().First();
        foreach (var effect in ability.Effects) effect.Execute();

        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeTrue();
        // Controller (Alice) is not opponent-side — not restricted.
        CastingRestrictions.CannotCastNoncreatureSpell(_alice).Should().BeFalse();
        // Sacrifice moved Ranger-Captain to the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(rc);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(rc);
    }

    [Fact]
    public void Validator_BlocksOpponentNoncreatureCast_WhenRestrictionActive()
    {
        CastingRestrictions.AddNoncreatureSpellRestrictionForTurn(_bob);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void Validator_AllowsCreatureCast_WhenRestrictionActive()
    {
        // CR 601.3 — restriction is strictly noncreature; creature casts pass.
        CastingRestrictions.AddNoncreatureSpellRestrictionForTurn(_bob);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        var action = new CastSpellAction(creature, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TurnEnd_ClearsRestriction_WhenEventBusWired()
    {
        var bus = new EventBus();
        var rc = RangerCaptainOfEosFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: bus,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(rc);
        rc.SetZone(ZoneType.Battlefield);

        var ability = rc.Abilities.OfType<ActivatedAbility>().First();
        foreach (var effect in ability.Effects) effect.Execute();
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeTrue();

        // Publish a TurnEndedEvent — handler clears the restriction.
        bus.Publish(new TurnEndedEvent(_alice, 1));

        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeFalse();
    }

    [Fact]
    public void CastingRestrictions_AddAndClear_NoncreatureForTurn_Toggles()
    {
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeFalse();

        CastingRestrictions.AddNoncreatureSpellRestrictionForTurn(_bob);
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeTrue();

        // Idempotent for the same player.
        CastingRestrictions.AddNoncreatureSpellRestrictionForTurn(_bob);
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeTrue();

        CastingRestrictions.ClearNoncreatureRestrictionForTurn();
        CastingRestrictions.CannotCastNoncreatureSpell(_bob).Should().BeFalse();
    }
}
