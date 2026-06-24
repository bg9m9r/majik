using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="OutcasterGreenbladeFactory"/> — Creature — Human
/// Mercenary {2}{G} 1/2 (Outlaws of Thunder Junction). Oracle text (verified
/// against Scryfall 2026-06-24):
///   "When this creature enters, search your library for a basic land card or
///    a Desert card, reveal it, put it into your hand, then shuffle.
///    This creature gets +1/+1 for each Desert you control."
///
/// Covers ONLY the card's unique behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - A single *_Identity assert (non-vanilla stats: {2}{G}, 1/2, Human/Mercenary).
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB tutor: a basic land in library → into hand (CR 603.6a).
///   - ETB tutor: a non-basic Desert card in library → into hand.
///   - Dynamic self-pump: +1/+1 for each Desert the controller controls
///     (CR 613.1g — Layer 7c).
/// </summary>
[Trait("Color", "G")]
public class OutcasterGreenbladeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void OutcasterGreenblade_Identity_HumanMercenary_AtTwoG_OneTwo()
    {
        var c = OutcasterGreenbladeFactory.Create(_alice);

        c.Name.Should().Be("Outcaster Greenblade");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Mercenary).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OutcasterGreenblade_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = OutcasterGreenbladeFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = OutcasterGreenbladeFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_Tutors_OneBasicLandIntoHand()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var greenblade = OutcasterGreenbladeFactory.Create(_alice);
        var etb = greenblade.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Count.Should().Be(startHand + 1,
            "Outcaster Greenblade tutors a basic land into hand");
        hand.OfType<Land>().Single().Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Etb_Tutors_NonbasicDesertIntoHand()
    {
        // A non-basic Desert card (e.g. "Hostile Desert"): not Basic, but has
        // the Desert subtype — the search clause is "basic land card OR a
        // Desert card".
        var desert = new Land("Hostile Desert",
            subtypes: new[] { CardSubtype.Desert });
        desert.SetOwner(_alice);
        _alice.Zones.Library.AddCard(desert);
        desert.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var greenblade = OutcasterGreenbladeFactory.Create(_alice);
        var etb = greenblade.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Count.Should().Be(startHand + 1,
            "a non-basic Desert card is a valid search target");
        hand.OfType<Land>().Single().Name.Should().Be("Hostile Desert");
        hand.OfType<Land>().Single().Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Etb_NoBasicOrDesertInLibrary_MovesNoCard()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic, not a Desert
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var greenblade = OutcasterGreenbladeFactory.Create(_alice);
        var etb = greenblade.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            "no basic land or Desert card in library → nothing put into hand");
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }

    // -----------------------------------------------------------------------
    // Dynamic self-pump — "gets +1/+1 for each Desert you control" (Layer 7c).
    // -----------------------------------------------------------------------

    private Land MakeDesert(string name)
    {
        var d = new Land(name, subtypes: new[] { CardSubtype.Desert });
        d.SetOwner(_alice);
        d.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(d);
        d.SetZone(ZoneType.Battlefield);
        return d;
    }

    /// <summary>
    /// Build a fully-wired Greenblade ON the battlefield: the lifecycle binder
    /// registers the Desert pump when the ETB <see cref="CardMovedEvent"/> lands
    /// (mirrors how production enters the permanent). Returns it.
    /// </summary>
    private Creature EnterGreenblade(ContinuousEffectsService continuous, IEventBus bus)
    {
        var greenblade = OutcasterGreenbladeFactory.Create(_alice, continuous, bus);
        _alice.Zones.Battlefield.AddCard(greenblade);
        greenblade.SetZone(ZoneType.Battlefield);
        greenblade.ActiveEffects = continuous;
        bus.Publish(new CardMovedEvent(greenblade, ZoneType.Stack, ZoneType.Battlefield));
        return greenblade;
    }

    [Fact]
    public void SelfPump_NoDesert_GetsBaseStats()
    {
        var continuous = new ContinuousEffectsService();
        var bus = new EventBus();

        var greenblade = EnterGreenblade(continuous, bus);

        var chars = continuous.Compute(greenblade);
        chars.Power.Should().Be(1, "no Deserts controlled → +0/+0");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void SelfPump_TwoDeserts_GetsPlusTwoPlusTwo()
    {
        var continuous = new ContinuousEffectsService();
        var bus = new EventBus();

        MakeDesert("Desert");
        MakeDesert("Hostile Desert");

        var greenblade = EnterGreenblade(continuous, bus);

        var chars = continuous.Compute(greenblade);
        chars.Power.Should().Be(1 + 2, "1/2 base + (2 Deserts you control) = +2/+2");
        chars.Toughness.Should().Be(2 + 2);
    }

    [Fact]
    public void SelfPump_OnlyBuffsItself_NotOtherCreatures()
    {
        var continuous = new ContinuousEffectsService();
        var bus = new EventBus();

        MakeDesert("Desert");

        EnterGreenblade(continuous, bus);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        bear.ActiveEffects = continuous;

        continuous.Compute(bear).Power.Should().Be(2,
            "the +1/+1-per-Desert pump applies only to Outcaster Greenblade itself");
    }

    [Fact]
    public void CountDeserts_CountsControllersDeserts()
    {
        MakeDesert("Desert");
        MakeDesert("Sunscorched Desert");

        // A non-Desert land does not count.
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        OutcasterGreenbladeFactory.CountDeserts(_alice).Should().Be(2,
            "two Deserts; the basic Forest is not a Desert");
    }
}
