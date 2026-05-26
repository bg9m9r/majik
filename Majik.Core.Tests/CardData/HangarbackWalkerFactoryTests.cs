using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
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
/// - ETB trigger: PendingCastX=3 → 3 +1/+1 counters on Hangarback.
/// - ETB trigger: no PendingCastX → 0 counters (non-cast entry).
/// - Dies trigger: with 2 counters, creates 2 Thopter tokens (1/1
///   artifact creature with Flying).
/// - Dies trigger: with 0 counters, creates no tokens.
/// - Activated ability: {1}, {T} → +1/+1 counter on self.
/// </summary>
public class HangarbackWalkerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

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
    public void Hangarback_AttachesEtbAndDiesTriggers()
    {
        var h = HangarbackWalkerFactory.Create(_alice);
        h.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB-counters and dies-tokens triggers");
        h.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{1}, {T}: +1/+1 counter on self");
    }

    [Fact]
    public void Hangarback_EtbWithXEquals3_GainsThreePlusOneCounters()
    {
        var h = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);

        // SpellCastFlow stamps PendingCastX after ChooseXAsync; simulate.
        h.SetPendingCastX(3);

        // ETB trigger is the first registered ability (ETB attached
        // before dies trigger in HangarbackWalkerFactory.Create).
        var etb = h.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Effects.Any(e => e.Description.Contains("enters with X")));
        foreach (var e in etb.Effects) e.Execute();

        h.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hangarback enters with X (=3) +1/+1 counters per CR 122.1g");
        h.PendingCastX.Should().BeNull(
            "PendingCastX stamp consumed; re-entries don't double-count");
    }

    [Fact]
    public void Hangarback_NonCastEntry_ZeroCounters()
    {
        // PendingCastX null → 0 counters → 0/0 → SBA puts in graveyard.
        var h = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(h);
        h.SetZone(ZoneType.Battlefield);
        h.PendingCastX.Should().BeNull();

        var etb = h.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Effects.Any(e => e.Description.Contains("enters with X")));
        foreach (var e in etb.Effects) e.Execute();

        h.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-cast entry → no PendingCastX → zero counters");
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
