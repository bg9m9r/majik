using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CoalitionRelicFactory"/> — Coalition Relic
/// (Future Sight, {3}). Artifact. Oracle text:
///   "{T}: Add one mana of any color.
///    {T}: Put a charge counter on this artifact.
///    At the beginning of your first main phase, remove all charge counters
///    from this artifact. Add one mana of any color for each charge counter
///    removed this way."
///
/// Covers:
/// - Identity (Artifact, {3}) + <see cref="NamedCardFactory"/> dispatch.
/// - Five mana abilities (one per WUBRG) — "{T}: Add one mana of any color".
/// - The {T}: put-a-charge-counter activated ability (non-mana, stack-using).
///   Resolving it adds one charge counter (CR 122).
/// - The first-main-phase trigger removes every charge counter and adds that
///   many mana of a single chosen color to the controller's pool (CR 106.6).
/// - Cashing zero charge counters adds no mana.
/// - The chosen color defaults to green and honors the colorSelector.
/// </summary>
[Trait("Color", "C")]
public class CoalitionRelicFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CoalitionRelic_IsArtifact_ThreeCost()
    {
        var relic = CoalitionRelicFactory.Create(_alice);

        relic.Name.Should().Be("Coalition Relic");
        relic.HasType(CardType.Artifact).Should().BeTrue();
        relic.HasType(CardType.Creature).Should().BeFalse();
        relic.ManaCost.Should().Be("{3}");
        relic.Owner.Should().BeSameAs(_alice);
        relic.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CoalitionRelic_IsNotLegendary()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        relic.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CoalitionRelic()
    {
        var card = NamedCardFactory.Create("Coalition Relic", _alice);

        card.Should().BeOfType<Artifact>();
        card!.Name.Should().Be("Coalition Relic");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one mana ability per WUBRG colour");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1, "the {T}: put a charge counter ability");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the first-main-phase cash-out trigger");
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of any color (CR 605.1a)
    // -----------------------------------------------------------------------

    [Fact]
    public void CoalitionRelic_HasFiveManaAbilities_OnePerColor()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        var mas = relic.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");
        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void CoalitionRelic_ManaAbility_TapsAndProducesColor()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        var blue = relic.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue("untapped relic on the battlefield can tap for mana");

        var produced = blue.Activate();
        produced.Blue.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        relic.IsTapped.Should().BeTrue("the printed {T} cost taps the relic (CR 605.1a)");
    }

    [Fact]
    public void CoalitionRelic_ManaAbility_CannotActivate_WhenTapped()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        relic.Abilities.OfType<ManaAbility>().First().Activate();
        relic.IsTapped.Should().BeTrue();

        foreach (var ma in relic.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse("a tapped relic can't pay {T}");
        }
    }

    // -----------------------------------------------------------------------
    // {T}: Put a charge counter on this artifact (non-mana; CR 605.1a)
    // -----------------------------------------------------------------------

    [Fact]
    public void CoalitionRelic_PutCharge_IsNotAManaAbility()
    {
        var relic = CoalitionRelicFactory.Create(_alice);

        var putCharge = relic.Abilities.OfType<ActivatedAbility>().ToList();
        putCharge.Should().HaveCount(1,
            "\"{T}: Put a charge counter\" has a visible effect, so it is a normal "
            + "activated ability, not a mana ability (CR 605.1a)");
    }

    [Fact]
    public void CoalitionRelic_PutCharge_AddsOneChargeCounter()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        relic.Counters.Count(CounterType.Charge).Should().Be(0);

        var putCharge = relic.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in putCharge.Effects) e.Execute();

        relic.Counters.Count(CounterType.Charge).Should().Be(1,
            "the ability puts one charge counter on the relic (CR 122)");
    }

    // -----------------------------------------------------------------------
    // First-main-phase cash-out trigger (CR 122 / CR 106.6)
    // -----------------------------------------------------------------------

    [Fact]
    public void CoalitionRelic_HasExactlyOneTrigger()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        relic.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the first-main-phase remove-all-charge-counters trigger");
    }

    [Fact]
    public void CoalitionRelic_Cash_RemovesAllCounters_AndAddsThatMuchMana_DefaultGreen()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);
        relic.Counters.Add(CounterType.Charge, 3);

        var trigger = relic.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        relic.Counters.Count(CounterType.Charge).Should().Be(0,
            "remove all charge counters from this artifact (CR 122)");

        var pool = _alice.ManaPool;
        pool.Green.Should().Be(3,
            "add one mana of any color for each charge counter removed — defaults to green");
        pool.Total.Should().Be(3, "exactly three pips, all one color");
    }

    [Fact]
    public void CoalitionRelic_Cash_HonorsColorSelector()
    {
        var relic = CoalitionRelicFactory.Create(
            _alice, triggers: null, colorSelector: _ => ManaColor.Red);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);
        relic.Counters.Add(CounterType.Charge, 2);

        var trigger = relic.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.ManaPool.Red.Should().Be(2, "cashed mana follows the chosen color (CR 106.6)");
        _alice.ManaPool.Total.Should().Be(2);
    }

    [Fact]
    public void CoalitionRelic_Cash_WithNoCounters_AddsNoMana()
    {
        var relic = CoalitionRelicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        var trigger = relic.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.ManaPool.Total.Should().Be(0,
            "no charge counters banked → no mana added");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void CoalitionRelic_Create_ThrowsOnNullOwner()
    {
        var act = () => CoalitionRelicFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
