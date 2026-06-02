using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="TopiaryStomperFactory"/> — Creature — Plant Dinosaur
/// {1}{G}{G} 4/4 (Streets of New Capenna). Oracle (verified against Scryfall):
///   "Vigilance (Attacking doesn't cause this creature to tap.)
///    When this creature enters, search your library for a basic land card,
///    put it onto the battlefield tapped, then shuffle.
///    This creature can't attack or block unless you control seven or more
///    lands."
///
/// Covers:
///   - Card identity (Creature + Plant/Dinosaur, {1}{G}{G}, 4/4, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Vigilance keyword marker.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB resolve: tutors ONE basic land onto the battlefield TAPPED (CR 603.6a).
///   - ETB resolve: only nonbasics in library → no card moved.
///   - "can't attack or block unless seven or more lands" predicate-mode
///     CombatRestrictionEffects (CannotAttack + CannotBlock), self-scoped,
///     evaluated against the controller's live land count.
/// </summary>
[Trait("Color", "G")]
public class TopiaryStomperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land Basic(string name, CardSubtype sub) =>
        new(name, supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { sub });

    // -------------------------------------------------------------------------
    // Identity / shape
    // -------------------------------------------------------------------------

    [Fact]
    public void TopiaryStomper_IsPlantDinosaur_AtOneGG_FourFour()
    {
        var c = TopiaryStomperFactory.Create(_alice);

        c.Name.Should().Be("Topiary Stomper");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TopiaryStomper_HasVigilanceMarker()
    {
        var c = TopiaryStomperFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Vigilance");
    }

    [Fact]
    public void TopiaryStomper_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = TopiaryStomperFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = TopiaryStomperFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // ETB tutor → battlefield tapped
    // -------------------------------------------------------------------------

    [Fact]
    public void Etb_Tutors_OneBasicOntoBattlefieldTapped()
    {
        var forest = Basic("Forest", CardSubtype.Forest);
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // A second basic so we exercise the "search for A basic" (singular)
        // path — only ONE should be moved to the battlefield.
        var island = Basic("Island", CardSubtype.Island);
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var startBf = _alice.Zones.Battlefield.GetCards().Count();

        var stomper = TopiaryStomperFactory.Create(_alice);
        var etb = stomper.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var bf = _alice.Zones.Battlefield.GetCards().OfType<Land>().ToList();
        bf.Should().HaveCount(1, "searches for A (one) basic land");
        bf[0].Zone.Should().Be(ZoneType.Battlefield);
        bf[0].IsTapped.Should().BeTrue("the basic enters tapped");
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(startBf + 1);
        _alice.Zones.Library.GetCards().OfType<Land>().Should().HaveCount(1,
            "only one of the two basics is taken");
    }

    [Fact]
    public void Etb_NoBasicsInLibrary_MovesNoCard()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var startBf = _alice.Zones.Battlefield.GetCards().Count();

        var stomper = TopiaryStomperFactory.Create(_alice);
        var etb = stomper.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Battlefield.GetCards().Count().Should().Be(startBf,
            "no basic land in library → nothing put onto the battlefield");
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }

    // -------------------------------------------------------------------------
    // Can't attack or block unless seven or more lands
    // -------------------------------------------------------------------------

    [Fact]
    public void FewerThanSevenLands_StomperCannotAttackOrBlock()
    {
        var effects = new ContinuousEffectsService();
        var stomper = TopiaryStomperFactory.Create(_alice, triggers: null, continuousEffects: effects);
        _alice.Zones.Battlefield.AddCard(stomper);
        stomper.SetZone(ZoneType.Battlefield);

        AddLands(6);

        effects.HasRestriction(stomper, CombatRestriction.CannotAttack)
            .Should().BeTrue("six lands < seven — can't attack");
        effects.HasRestriction(stomper, CombatRestriction.CannotBlock)
            .Should().BeTrue("six lands < seven — can't block");
    }

    [Fact]
    public void SevenOrMoreLands_StomperCanAttackAndBlock()
    {
        var effects = new ContinuousEffectsService();
        var stomper = TopiaryStomperFactory.Create(_alice, triggers: null, continuousEffects: effects);
        _alice.Zones.Battlefield.AddCard(stomper);
        stomper.SetZone(ZoneType.Battlefield);

        AddLands(7);

        effects.HasRestriction(stomper, CombatRestriction.CannotAttack)
            .Should().BeFalse("seven lands satisfies 'seven or more'");
        effects.HasRestriction(stomper, CombatRestriction.CannotBlock)
            .Should().BeFalse("seven lands satisfies 'seven or more'");
    }

    [Fact]
    public void Restriction_ReachingSeventhLand_LiftsImmediately()
    {
        var effects = new ContinuousEffectsService();
        var stomper = TopiaryStomperFactory.Create(_alice, triggers: null, continuousEffects: effects);
        _alice.Zones.Battlefield.AddCard(stomper);
        stomper.SetZone(ZoneType.Battlefield);

        AddLands(6);
        effects.HasRestriction(stomper, CombatRestriction.CannotAttack).Should().BeTrue();

        AddLands(1); // seventh land — lock recomputes live
        effects.HasRestriction(stomper, CombatRestriction.CannotAttack)
            .Should().BeFalse("predicate re-reads live land count every pass");
    }

    [Fact]
    public void Restriction_GatedToStomperOnly_NotOtherCreatures()
    {
        var effects = new ContinuousEffectsService();
        var stomper = TopiaryStomperFactory.Create(_alice, triggers: null, continuousEffects: effects);
        _alice.Zones.Battlefield.AddCard(stomper);
        stomper.SetZone(ZoneType.Battlefield);

        AddLands(3); // under seven — Stomper is locked

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        effects.HasRestriction(stomper, CombatRestriction.CannotAttack).Should().BeTrue();
        effects.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeFalse("the restriction is scoped to the Stomper only");
    }

    [Fact]
    public void Restriction_SuppressedOffBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var stomper = TopiaryStomperFactory.Create(_alice, triggers: null, continuousEffects: effects);
        // Not on the battlefield — static restriction suppressed (CR 603.6e).
        AddLands(2); // under seven would otherwise lock it

        effects.HasRestriction(stomper, CombatRestriction.CannotAttack)
            .Should().BeFalse("static restriction functions only on the battlefield");
    }

    private void AddLands(int n)
    {
        for (var i = 0; i < n; i++)
        {
            var land = Basic($"Forest{_alice.Zones.Battlefield.GetCards().Count()}_{i}", CardSubtype.Forest);
            land.SetOwner(_alice);
            land.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }
    }
}
