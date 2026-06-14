using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="IdyllicGrangeFactory"/>.
///
/// Oracle (Scryfall-confirmed, Throne of Eldraine):
///   "({T}: Add {W}.)
///    This land enters tapped unless you control three or more other Plains.
///    When this land enters untapped, put a +1/+1 counter on target creature
///    you control."
///
/// Covers the card's UNIQUE behaviour only (dispatch + well-formedness are
/// asserted for every implemented card by CardFactoryContractTests):
/// - Identity: Land type, name, Plains subtype, nonbasic, non-legendary.
/// - {T}: Add {W} mana ability (the intrinsic Plains mana ability).
/// - "enters tapped unless you control three or more other Plains" predicate
///   (CR 614.1c) via <see cref="ReplacementBus"/>:
///     · 0/1/2 other Plains controlled → enters tapped.
///     · 3 other Plains controlled → enters untapped.
///     · Plains on the opponent's battlefield don't count.
///     · Idyllic Grange itself is a Plains but is excluded as "other"
///       (CR 109.2) — it can't help satisfy its own gate.
/// - "When this land enters untapped, put a +1/+1 counter on target creature
///   you control" (CR 603.6e) resolution: a +1/+1 counter lands on the chosen
///   creature; resolution-time legality rechecks (CR 608.2b) drop illegal
///   targets.
/// </summary>
[Trait("Color", "W")]
public class IdyllicGrangeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Land AddPlains(Player controller)
    {
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = controller, Controller = controller };
        plains.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(plains);
        return plains;
    }

    private Creature AddCreature(Player controller, string name = "Grizzly Bears")
    {
        var creature = new Creature(name, "{1}{G}", 2, 2);
        creature.SetOwner(controller);
        creature.SetController(controller);
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        return creature;
    }

    private static bool? EntersTappedFor(Player controller)
    {
        var bus = new ReplacementBus();
        var grange = IdyllicGrangeFactory.Create(controller, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: grange,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        return after!.EntersTapped;
    }

    // -----------------------------------------------------------------------
    // Identity (non-vanilla land — Plains subtype is load-bearing for the gate)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_Identity_LandPlainsNonbasicNonlegendary()
    {
        var grange = IdyllicGrangeFactory.Create(_alice);

        grange.Name.Should().Be("Idyllic Grange");
        grange.HasType(CardType.Land).Should().BeTrue();
        grange.HasSubtype(CardSubtype.Plains).Should().BeTrue(
            "Idyllic Grange is a Plains (its own gate excludes it as 'other', not by lacking the subtype)");
        grange.HasSupertype(CardSupertype.Basic).Should().BeFalse("nonbasic");
        grange.HasSupertype(CardSupertype.Legendary).Should().BeFalse("not legendary");
        grange.Owner.Should().BeSameAs(_alice);
        grange.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasManaAbility_ProducesWhite()
    {
        var grange = IdyllicGrangeFactory.Create(_alice);
        grange.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grange);

        var mana = (IManaAbility)grange.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeTrue();

        var produced = mana.Activate();
        produced.White.Should().Be(1, "{T}: Add {W}");
        produced.Generic.Should().Be(0);
        grange.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EntersTapped_WhenFewerThanThreeOtherPlains(int plainsCount)
    {
        var alice = new Player("Alice", 20);
        for (var i = 0; i < plainsCount; i++) AddPlains(alice);

        EntersTappedFor(alice).Should().BeTrue(
            $"with {plainsCount} other Plains (<3) Idyllic Grange enters tapped");
    }

    [Fact]
    public void EntersUntapped_WhenThreeOrMoreOtherPlains()
    {
        var alice = new Player("Alice", 20);
        AddPlains(alice);
        AddPlains(alice);
        AddPlains(alice);

        EntersTappedFor(alice).Should().BeFalse(
            "with three other Plains Idyllic Grange enters untapped");
    }

    [Fact]
    public void EntersTapped_WhenPlainsBelongToOpponent()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        AddPlains(bob);
        AddPlains(bob);
        AddPlains(bob);

        EntersTappedFor(alice).Should().BeTrue(
            "the 'you control' predicate checks the controller's battlefield, not the opponent's");
    }

    [Fact]
    public void PredicateExcludesSelf_ThreeOthersStillRequired()
    {
        // Two other Plains + Idyllic Grange itself on the battlefield = only
        // TWO *other* Plains (CR 109.2), so it still enters tapped.
        var alice = new Player("Alice", 20);
        AddPlains(alice);
        AddPlains(alice);

        var bus = new ReplacementBus();
        var grange = IdyllicGrangeFactory.Create(alice, replacements: bus, triggers: null);
        grange.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(grange);

        var intent = new ZoneMoveIntent(
            Card: grange,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Idyllic Grange does not count itself toward 'three or more other Plains'");
    }

    // -----------------------------------------------------------------------
    // Enters-untapped trigger: +1/+1 counter on target creature (CR 603.6e)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersUntappedTrigger_PlacesPlusOnePlusOneCounterOnTarget()
    {
        var grange = IdyllicGrangeFactory.Create(_alice);
        var trigger = grange.Abilities.OfType<TriggeredAbility>().Single();

        var creature = AddCreature(_alice);
        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0, "no counters before resolution");

        // Resolve the trigger with the creature chosen as the target.
        trigger.SetChosenTargets(new[] { new List<object> { creature } });
        trigger.Effects.Single().Execute();

        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the enters-untapped trigger places one +1/+1 counter on the target");
    }

    [Fact]
    public void EntersUntappedTrigger_DropsTargetThatLeftBattlefield()
    {
        var grange = IdyllicGrangeFactory.Create(_alice);
        var trigger = grange.Abilities.OfType<TriggeredAbility>().Single();

        var creature = AddCreature(_alice);
        // Target left the battlefield before resolution (CR 608.2b).
        _alice.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);

        trigger.SetChosenTargets(new[] { new List<object> { creature } });
        trigger.Effects.Single().Execute();

        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "an illegal-on-resolution target gets no counter (CR 608.2b)");
    }
}
