using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HangarbackWalkerFactory"/> (Magic Origins, {X}{X}).
///
/// Card: Hangarback Walker — Artifact Creature — Construct 0/0.
/// Oracle:
///   "Hangarback Walker enters with X +1/+1 counters on it.
///    When Hangarback Walker dies, create a 1/1 colorless Thopter
///    artifact creature token with flying for each +1/+1 counter on
///    Hangarback Walker.
///    {1}, {T}: Put a +1/+1 counter on Hangarback Walker."
///
/// Covers:
/// - Identity (Artifact Creature — Construct, {X}{X}, 0/0).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="Card.ManaCostValue.HasX"/> reports true.
/// - "Enters with X +1/+1 counters" is owned by the generic
///   <see cref="EntersWithCountersBinder"/> (NOT a self-managed ETB trigger):
///   the factory attaches no ETB-counters trigger and does not self-manage;
///   the binder reads <see cref="Card.PendingCastX"/> and places the counters
///   as Hangarback enters (CR 614.1d). X=3 → 3 counters; X=0 → 0/0.
/// - Dies trigger: with 2 counters, creates 2 Thopter tokens (1/1
///   artifact creature with Flying).
/// - Dies trigger: with 0 counters, creates no tokens.
/// - Activated ability: {1}, {T} → +1/+1 counter on self.
/// </summary>
public class HangarbackWalkerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static CardEntity HangarbackEntity() =>
        new EmbeddedCardRepository().GetByName("Hangarback Walker")!;

    [Fact]
    public void Hangarback_Identity()
    {
        var h = HangarbackWalkerFactory.Create(_alice);

        h.Name.Should().Be("Hangarback Walker");
        h.ManaCost.Should().Be("{X}{X}");
        h.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        h.HasType(CardType.Creature).Should().BeTrue();
        h.HasType(CardType.Artifact).Should().BeTrue("Hangarback is an artifact creature");
        h.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        h.BasePower.Should().Be(0);
        h.BaseToughness.Should().Be(0);
        h.Owner.Should().BeSameAs(_alice);
        h.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Hangarback_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Hangarback Walker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Hangarback Walker");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    [Fact]
    public void Hangarback_AttachesDiesTriggerOnly_NoEtbCountersTrigger()
    {
        var h = HangarbackWalkerFactory.Create(_alice);

        // CR 614.1d — the ETB counters are a binder-registered replacement, NOT
        // a factory-attached trigger. Only the dies-tokens trigger remains.
        h.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "only the dies-tokens trigger is factory-attached; the ETB-X counters " +
            "are owned by the EntersWithCountersBinder");
        h.Abilities.OfType<TriggeredAbility>().Single()
            .Effects.Should().Contain(e => e.Description.Contains("Thopter"),
                "the lone factory trigger is the dies-tokens trigger");
        h.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{1}, {T}: +1/+1 counter on self");
    }

    [Fact]
    public void Hangarback_DoesNotSelfManageEntersWithCounters()
    {
        // The factory must leave SelfManagesEntersWithCounters false so the
        // EntersWithCountersBinder DOES register the variable-X replacement on
        // the prod route. Setting the flag suppresses the binder → 0 counters.
        var h = HangarbackWalkerFactory.Create(_alice);

        h.SelfManagesEntersWithCounters.Should().BeFalse(
            "the binder owns the ETB-X replacement; self-managing suppresses it " +
            "and yields zero counters on the Approach-B prod route");
    }

    [Fact]
    public void Hangarback_BinderReplacement_EntersWithXEquals3_Counters()
    {
        // The prod mechanism: factory build + binder (reads the card's real
        // oracle text, which ALSO contains a "for each +1/+1 counter" dies
        // clause — the binder must scope its conditional-clause guard to the
        // ETB sentence so the clean variable-X clause still binds) + ZoneService
        // move. X = 3 (cast {3}{3}).
        var bus = new ReplacementBus();
        var h = HangarbackWalkerFactory.Create(_alice);

        EntersWithCountersBinder.Bind(h, HangarbackEntity(), bus).Should().BeTrue(
            "the binder matches 'enters with X +1/+1 counters on it' even though the " +
            "card carries an unrelated 'for each +1/+1 counter' dies clause");

        h.SetOwner(_alice);
        h.SetController(_alice);
        _alice.Zones.Library.AddCard(h);
        h.SetZone(ZoneType.Library);
        h.SetPendingCastX(3);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(h, ZoneType.Library, ZoneType.Battlefield, _alice);

        h.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hangarback enters WITH X (=3) +1/+1 counters per CR 614.1d → 3/3");
    }

    [Fact]
    public void Hangarback_BinderReplacement_ZeroX_NoCounters()
    {
        // No PendingCastX stamp → X = 0 → a 0/0 the SBA layer sends to the
        // graveyard (CR 704.5f). Non-cast entries (blink, copy) take this path.
        var bus = new ReplacementBus();
        var h = HangarbackWalkerFactory.Create(_alice);

        EntersWithCountersBinder.Bind(h, HangarbackEntity(), bus).Should().BeTrue();

        h.SetOwner(_alice);
        h.SetController(_alice);
        _alice.Zones.Library.AddCard(h);
        h.SetZone(ZoneType.Library);
        // No SetPendingCastX → X defaults to 0.

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(h, ZoneType.Library, ZoneType.Battlefield, _alice);

        h.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X = 0 → zero counters placed → 0/0 SBA-fodder (CR 704.5f)");
    }

    [Fact]
    public void Hangarback_DiesTrigger_CreatesThoptersPerCounter()
    {
        var h = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        // Hangarback dies with 2 +1/+1 counters (e.g. cast for X=2).
        h.Counters.Add(CounterType.PlusOnePlusOne, 2);

        // Move Hangarback to graveyard manually (death).
        _alice.Zones.Battlefield.RemoveCard(h);
        _alice.Zones.Graveyard.AddCard(h);
        h.SetZone(ZoneType.Graveyard);

        // Resolve the dies trigger.
        var dies = h.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Thopter")));
        foreach (var e in dies.Effects) e.Execute();

        var thopters = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Thopter")
            .ToList();
        thopters.Should().HaveCount(2,
            "one Thopter token per +1/+1 counter (2 counters → 2 Thopters)");
        thopters.Should().AllSatisfy(t =>
        {
            t.HasType(CardType.Artifact).Should().BeTrue("Thopter tokens are artifact creatures");
            t.HasType(CardType.Creature).Should().BeTrue();
            t.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
            t.BasePower.Should().Be(1);
            t.BaseToughness.Should().Be(1);
        });
    }

    [Fact]
    public void Hangarback_DiesTrigger_ZeroCounters_NoTokens()
    {
        var h = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);
        // No counters — 0/0 dies via SBA before activated abilities run.

        _alice.Zones.Battlefield.RemoveCard(h);
        _alice.Zones.Graveyard.AddCard(h);
        h.SetZone(ZoneType.Graveyard);

        var dies = h.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Thopter")));
        foreach (var e in dies.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.Name == "Thopter").Should().BeEmpty(
                "0 counters → 0 Thopter tokens");
    }

    [Fact]
    public void Hangarback_ActivatedAbility_AddsCounter()
    {
        var h = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        h.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        var activated = h.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        h.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "{1}, {T}: Put a +1/+1 counter on Hangarback Walker (CR 605.1)");
    }
}
