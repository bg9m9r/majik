using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HeapedHarvestFactory"/> (Bloomburrow, {2}{G}).
///
/// Artifact — Food. Oracle text (verified against Scryfall 2026-06-24):
///   "When this artifact enters and when you sacrifice it, you may search your
///    library for a basic land card, put it onto the battlefield tapped, then
///    shuffle.
///    {2}, {T}, Sacrifice this artifact: You gain 3 life."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: Artifact — Food at {2}{G} (non-vanilla cost + subtype).
/// - The standard Food sacrifice ability ({2}, {T}, Sac: gain 3 life) is
///   materialised from JSON (same posture as <see cref="LembasFactory"/> /
///   <see cref="GingerbruteFactory"/>) — assert its cost shape + life-gain.
/// - The shared tutor effect fires off BOTH the ETB trigger (CardMovedEvent)
///   and the reflexive "when you sacrifice it" trigger
///   (PermanentSacrificedEvent): each puts a basic land onto the battlefield
///   tapped (CR 701.18) and shuffles (CR 701.20a).
///
/// <see cref="NamedCardFactory"/> dispatch + well-formedness are asserted for
/// every implemented card by <c>CardFactoryContractTests</c> — not re-tested
/// here.
/// </summary>
[Trait("Color", "G")]
public class HeapedHarvestTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HeapedHarvest_Identity_ArtifactFood_At2G()
    {
        var card = HeapedHarvestFactory.Create(_alice);

        card.Name.Should().Be("Heaped Harvest");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Food).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HeapedHarvest_HasSacrificeForLifeAbility_FromJson()
    {
        var card = HeapedHarvestFactory.Create(_alice);

        // {2}, {T}, Sacrifice this artifact: You gain 3 life. (CR 602.1)
        // The JSON sacrifice_self cost materialises as an AdditionalCost of
        // type Sacrifice (Primitives.Costs.SacrificeSelf -> AdditionalCost),
        // paired with the {T} AdditionalCost.
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();
        var additional = activated.Costs.OfType<AdditionalCost>().ToList();
        additional.Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
            "the printed Food sacrifice cost");
        additional.Should().Contain(c => c.CostType == AdditionalCostType.Tap,
            "the printed {T} cost");
    }

    [Fact]
    public void HeapedHarvest_SacrificeAbility_GainsThreeLife()
    {
        var card = HeapedHarvestFactory.Create(_alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var effect in activated.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(23,
            "the Food sacrifice ability gains its controller 3 life (CR 119.3)");
    }

    [Fact]
    public void HeapedHarvest_HasTwoTutorTriggers_EtbAndSelfSacrifice()
    {
        var card = HeapedHarvestFactory.Create(_alice);

        // "When this artifact enters AND when you sacrifice it, …" — two
        // triggered abilities sharing the tutor effect (CR 603.6a / CR 701.16).
        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2);
        triggers.Should().ContainSingle(t => t.Condition.EventType == typeof(CardMovedEvent),
            "the ETB trigger fires on the enters-the-battlefield CardMovedEvent");
        triggers.Should().ContainSingle(t => t.Condition.EventType == typeof(PermanentSacrificedEvent),
            "the reflexive trigger fires on the self-sacrifice PermanentSacrificedEvent");
    }

    [Fact]
    public void HeapedHarvest_EtbTrigger_PutsBasicOntoBattlefieldTapped()
    {
        var card = HeapedHarvestFactory.Create(_alice);
        var forest = NewBasicForest();
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var etb = EtbTutorTrigger(card);
        etb.Resolve();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "the tutored basic land is put onto the battlefield (CR 701.18)");
        forest.IsTapped.Should().BeTrue("…tapped");
    }

    [Fact]
    public void HeapedHarvest_SelfSacrificeTrigger_PutsBasicOntoBattlefieldTapped()
    {
        var card = HeapedHarvestFactory.Create(_alice);
        var forest = NewBasicForest();
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var sacTrigger = SelfSacrificeTutorTrigger(card);

        // The reflexive "when you sacrifice it" trigger fires only on THIS
        // artifact's sacrifice.
        sacTrigger.Condition.Matches(
            new PermanentSacrificedEvent(card, _alice, wasToken: false), sacTrigger)
            .Should().BeTrue();

        sacTrigger.Resolve();

        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void HeapedHarvest_SelfSacrificeTrigger_DoesNotFireOnOtherSacrifice()
    {
        var card = HeapedHarvestFactory.Create(_alice);
        var other = new Artifact("Other Food", "{1}");

        var sacTrigger = SelfSacrificeTutorTrigger(card);

        sacTrigger.Condition.Matches(
            new PermanentSacrificedEvent(other, _alice, wasToken: false), sacTrigger)
            .Should().BeFalse(
                "only the sacrifice of THIS artifact tutors a basic land");
    }

    private static Land NewBasicForest()
    {
        return new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
    }

    private TriggeredAbility EtbTutorTrigger(Artifact card)
    {
        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition.EventType == typeof(CardMovedEvent));
        foreach (var land in _alice.Zones.Library.GetCards().OfType<Land>())
            land.SetOwner(_alice);
        return trigger;
    }

    private static TriggeredAbility SelfSacrificeTutorTrigger(Artifact card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition.EventType == typeof(PermanentSacrificedEvent));
}
