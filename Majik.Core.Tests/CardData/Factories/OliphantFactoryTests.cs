using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="OliphantFactory"/> (Tarkir: Dragonstorm, {5}{R}).
///
/// Creature — Elephant 6/4. Oracle text:
///   "Trample
///    Whenever this creature attacks, another target creature you control
///    gets +2/+0 and gains trample until end of turn.
///    Mountaincycling {1}"
///
/// Covers:
/// - Identity: {5}{R} Creature — Elephant 6/4, red, mana value 6.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Trample keyword marker; <see cref="CombatAbilities.HasTrample"/> reads it.
/// - Attack trigger present, targeted, fires on self-attack.
/// - Attack trigger end-to-end: +2/+0 + Trample grant to ANOTHER target
///   creature the controller controls (not Oliphaunt itself).
/// - Self-exclusion guard: targeting Oliphaunt itself is a no-op.
/// - Mountaincycling {1}: keyword markers + activated ability shape.
/// - Mountaincycling end-to-end: pays {1}, discards self, tutors a Mountain
///   card to hand, publishes <see cref="CardCycledEvent"/>.
/// - Cycling cost gate: DiscardSelfCost CanPay is hand-only.
/// </summary>
public class OliphantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Oliphaunt_Identity_Elephant_6_4_At5R()
    {
        var card = OliphantFactory.Create(_alice);

        card.Name.Should().Be("Oliphaunt");
        card.ManaCost.ToString().Should().Be("{5}{R}");
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(4);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elephant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Oliphaunt_ManaValue_IsSix()
    {
        var card = OliphantFactory.Create(_alice);

        // {5}{R} = 5 + 1 = 6 (CR 202.3)
        card.ManaCostValue.TotalValue.Should().Be(6);
    }

    [Fact]
    public void Oliphaunt_Color_IsRed()
    {
        var card = OliphantFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Red,
            "Oliphaunt has {R} in its mana cost (CR 105.2)");
    }

    [Fact]
    public void Oliphaunt_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Oliphaunt", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Oliphaunt");
        card.HasSubtype(CardSubtype.Elephant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Trample keyword — CR 702.19
    // -----------------------------------------------------------------------

    [Fact]
    public void Oliphaunt_HasTrampleKeywordAbility()
    {
        var card = OliphantFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Oliphaunt has printed Trample (CR 702.19)");
    }

    [Fact]
    public void Oliphaunt_CombatAbilitiesHasTrample_ReturnsTrue()
    {
        var card = OliphantFactory.Create(_alice);

        CombatAbilities.HasTrample(card).Should().BeTrue(
            "CombatAbilities.HasTrample must recognise the Trample marker");
    }

    // -----------------------------------------------------------------------
    // Attack trigger shape — CR 508.1f / 603.1
    // -----------------------------------------------------------------------

    [Fact]
    public void Oliphaunt_HasOneTriggeredAbility()
    {
        var card = OliphantFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Oliphaunt has exactly one triggered ability (the attack trigger)");
    }

    [Fact]
    public void Oliphaunt_AttackTrigger_HasOneTargetRequest()
    {
        var card = OliphantFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1,
            "one target request: another target creature you control");
    }

    [Fact]
    public void Oliphaunt_AttackTrigger_FiresOnSelfAttack()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var oliphaunt = OliphantFactory.Create(_alice, eventBus: bus, triggers: triggers);
        oliphaunt.SetZone(ZoneType.Battlefield);

        var bob = new Player("Bob", 20);
        bus.Publish(new CreatureAttacksEvent(oliphaunt, bob));

        triggers.PendingCount.Should().Be(1, "attack trigger must fire when Oliphaunt attacks");
    }

    [Fact]
    public void Oliphaunt_AttackTrigger_DoesNotFireOnOtherAttacker()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var oliphaunt = OliphantFactory.Create(_alice, eventBus: bus, triggers: triggers);
        oliphaunt.SetZone(ZoneType.Battlefield);

        var otherCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        otherCreature.SetOwner(_alice);
        otherCreature.SetController(_alice);

        var bob = new Player("Bob", 20);
        bus.Publish(new CreatureAttacksEvent(otherCreature, bob));

        triggers.PendingCount.Should().Be(0, "attack trigger must NOT fire for another attacker");
    }

    // -----------------------------------------------------------------------
    // Attack trigger end-to-end: +2/+0 + Trample grant to ANOTHER creature
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_PumpsAnotherCreaturePlus2Plus0_AndGrantsTrample()
    {
        var effects = new ContinuousEffectsService();

        var oliphaunt = OliphantFactory.Create(_alice);
        oliphaunt.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(oliphaunt);

        // A second creature that will receive the pump.
        var ally = new Creature("Ally Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = effects;

        // Obtain the attack trigger and wire the chosen target directly.
        var trigger = oliphaunt.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ally },
        });

        foreach (var eff in trigger.Effects) eff.Execute();

        // Ally gets +2/+0 until end of turn (CR 613.1g Layer 7c).
        ally.Power.Should().Be(4,  "base 2 + pump +2");
        ally.Toughness.Should().Be(2, "toughness unchanged by +2/+0");

        // Ally gains Trample until end of turn (CR 702.19 / CR 613.1c Layer 6).
        effects.Compute(ally).Keywords.Should().Contain("Trample",
            "Oliphaunt's attack trigger grants Trample until end of turn");
    }

    [Fact]
    public void AttackTrigger_TargetIsOliphantItself_IsNoOp()
    {
        // Oracle text says "ANOTHER target creature you control" — Oliphaunt
        // cannot be the target of its own attack trigger.
        var effects = new ContinuousEffectsService();

        var oliphaunt = OliphantFactory.Create(_alice);
        oliphaunt.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(oliphaunt);
        oliphaunt.ActiveEffects = effects;

        var trigger = oliphaunt.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { oliphaunt },
        });

        foreach (var eff in trigger.Effects) eff.Execute();

        // Self-targeting is a no-op — base P/T unchanged.
        oliphaunt.BasePower.Should().Be(6,   "Oliphaunt is excluded from its own attack trigger");
        oliphaunt.BaseToughness.Should().Be(4, "Oliphaunt is excluded from its own attack trigger");
    }

    // -----------------------------------------------------------------------
    // Mountaincycling {1} — CR 702.29 / 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void Oliphaunt_HasMountaincyclingAndCyclingKeywordMarkers()
    {
        var card = OliphantFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Mountaincycling",
            "typed-cycling keyword marker must be surfaced (CR 702.32d)");
        keywords.Should().Contain("Cycling",
            "CR 702.32d — typecycling IS Cycling; generic marker also present");
    }

    [Fact]
    public void Oliphaunt_MountaincyclingActivatedAbility_CostsOneGenericPlusSelf()
    {
        var card = OliphantFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2, "mountaincycling = {1} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1, "mountaincycling {1} charges one generic mana");
    }

    [Fact]
    public void Oliphaunt_Mountaincycling_EndToEnd_TutorsMountainAndPublishesEvent()
    {
        // Seed library: a Forest, a Mountain (the target), and a non-land instant.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var mountain = new Land(
            "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);

        var noise = new Instant("Shock", "{R}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var card = OliphantFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("1"));

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self when cycling");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(mountain,
            "Mountaincycling tutors a Mountain card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(forest,
            "Mountaincycling filters to Mountain subtype only");
        _alice.Zones.Hand.GetCards().Should().NotContain(noise,
            "Mountaincycling does not tutor non-Mountain cards");
        mountain.Zone.Should().Be(ZoneType.Hand);

        captured.Should().NotBeNull("CR 702.32d — CardCycledEvent must be published");
        captured!.Card.Should().BeSameAs(card);
        captured.Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Oliphaunt_Mountaincycling_DiscardSelfCost_CannotPayFromLibrary()
    {
        var card = OliphantFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}
