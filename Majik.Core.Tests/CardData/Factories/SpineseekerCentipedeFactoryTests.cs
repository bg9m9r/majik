using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SpineseekerCentipedeFactory"/> — Creature — Insect
/// {2}{G} 2/1 (Outlaws of Thunder Junction). Oracle:
///   "When this creature enters, search your library for a basic land card,
///    reveal it, put it into your hand, then shuffle.
///    Delirium — This creature gets +1/+2 and has vigilance as long as there
///    are four or more card types among cards in your graveyard."
///
/// Covers ONLY the card's unique behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - Identity assert (exact cost / P-T / subtype) — non-vanilla stats.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>.
///   - ETB resolve: tutors ONE basic land into the controller's hand (CR 603.6a),
///     MANDATORY (no "you may").
///   - ETB resolve: only nonbasics in library → no card moved.
///   - Delirium active (4+ types): +1/+2 and vigilance (CR 702.105).
///   - Delirium inactive (3 types): printed 2/1, no vigilance.
///   - Delirium dynamic: gaining a 4th type lights the static up live.
/// </summary>
[Trait("Color", "G")]
public class SpineseekerCentipedeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void SeedGraveyard(Player owner, params CardType[][] typeBundles)
    {
        var i = 0;
        foreach (var types in typeBundles)
        {
            var card = new Card($"Seed{i++}", "0", types);
            card.SetOwner(owner);
            owner.Zones.Graveyard.AddCard(card);
        }
    }

    private Creature CreateOnBattlefield(EventBus bus, ContinuousEffectsService effects)
    {
        var c = SpineseekerCentipedeFactory.Create(_alice, bus, triggers: null, effects: effects);
        c.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield));
        return c;
    }

    [Fact]
    public void Spineseeker_Identity_Insect_2_1_AtTwoG()
    {
        var c = SpineseekerCentipedeFactory.Create(_alice);

        c.Name.Should().Be("Spineseeker Centipede");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = SpineseekerCentipedeFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Etb_Tutors_OneBasicIntoHand()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // A second basic so we exercise the "search for A basic" (singular)
        // path — only ONE should be moved to hand.
        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var c = SpineseekerCentipedeFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Count.Should().Be(startHand + 1,
            "Spineseeker searches for A (one) basic land and puts it into hand");
        hand.OfType<Land>().Single().Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(c => c is Land,
            "only one of the two basics is taken");
    }

    [Fact]
    public void Etb_NoBasicsInLibrary_MovesNoCard()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var c = SpineseekerCentipedeFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            "no basic land in library → nothing put into hand");
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }

    [Fact]
    public void DeliriumInactive_ThreeTypes_Is_2_1_NoVigilance()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var c = CreateOnBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        SpineseekerCentipedeFactory.IsDeliriumActive(_alice).Should().BeFalse();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        CombatAbilities.HasVigilance(c).Should().BeFalse();
    }

    [Fact]
    public void DeliriumActive_FourTypes_Is_3_3_WithVigilance()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var c = CreateOnBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        SpineseekerCentipedeFactory.IsDeliriumActive(_alice).Should().BeTrue();
        c.Power.Should().Be(3, "+1 from delirium");
        c.Toughness.Should().Be(3, "+2 from delirium");
        CombatAbilities.HasVigilance(c).Should().BeTrue();
    }

    [Fact]
    public void DeliriumDynamic_GainingFourthType_LightsUpStatic()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var c = CreateOnBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        c.Power.Should().Be(2, "3 types is below threshold");
        CombatAbilities.HasVigilance(c).Should().BeFalse();

        var enchant = new Card("Holy Aura", "1W", new[] { CardType.Enchantment });
        enchant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(enchant);
        effects.Clear();

        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        CombatAbilities.HasVigilance(c).Should().BeTrue();
    }
}
