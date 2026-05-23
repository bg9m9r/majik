using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TheOneRingFactory"/>.
///
/// The One Ring (Tales of Middle-earth, {4}, Legendary Artifact):
///   "Indestructible."
///   "When The One Ring enters, if you cast it, you gain protection
///    from everything until your next turn."
///   "At the beginning of your upkeep, you lose 1 life for each burden
///    counter on The One Ring."
///   "{T}: Put a burden counter on The One Ring, then draw a card for
///    each burden counter on The One Ring."
///
/// Covers:
///   - Card identity (name, artifact type, legendary supertype, mana cost,
///     owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name produces an
///     <see cref="Artifact"/>.
///   - Indestructible <see cref="KeywordAbility"/> marker is present so
///     SBA / combat lookups treat the artifact as indestructible.
///   - The activated {T} ability adds a burden counter and draws N
///     cards where N is the new burden count (1 → 1, 2 → 2, …).
///   - The upkeep trigger drains life equal to the current burden count.
/// </summary>
public class TheOneRingTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TheOneRing_HasExpectedShape()
    {
        var card = TheOneRingFactory.Create(_alice);

        card.Name.Should().Be("The One Ring");
        card.ManaCost.Should().Be("{4}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TheOneRing()
    {
        var card = NamedCardFactory.Create("The One Ring", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("The One Ring");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.ManaCost.Should().Be("{4}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TheOneRing_HasIndestructibleKeyword()
    {
        var card = TheOneRingFactory.Create(_alice);

        card.Abilities
            .OfType<KeywordAbility>()
            .Should().ContainSingle(k =>
                string.Equals(k.Keyword, "Indestructible",
                    StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Activated {T}: add burden counter, draw N
    // -----------------------------------------------------------------------

    [Fact]
    public void TapAbility_FirstTap_AddsOneBurdenAndDrawsOne()
    {
        // Library: [a, b, c, d]. First activation adds burden #1 and
        // draws 1 card (= the new burden count). After: 1 burden, hand
        // has the top card, library has [b, c, d].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var ring = TheOneRingFactory.Create(_alice);
        PutOnBattlefield(ring);

        var tap = ring.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in tap.Effects) e.Execute();

        ring.Counters.Count(CounterType.Burden).Should().Be(1);
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, d });
    }

    [Fact]
    public void TapAbility_SecondTap_AddsAnotherBurdenAndDrawsTwo()
    {
        // Library: [a, b, c, d]. Two activations: 1st draws 1 (a), 2nd
        // bumps burdens to 2 and draws 2 (b, c). After: 2 burdens, hand
        // [a, b, c], library [d].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var ring = TheOneRingFactory.Create(_alice);
        PutOnBattlefield(ring);

        var tap = ring.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in tap.Effects) e.Execute();
        foreach (var e in tap.Effects) e.Execute();

        ring.Counters.Count(CounterType.Burden).Should().Be(2);
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b, c });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { d });
    }

    [Fact]
    public void TapAbility_TapsRequireTapCost()
    {
        // Sanity check: the activated ability includes AdditionalCost.Tap
        // so it can't be free-fired in production. (Effect-only invocation
        // in the other tests bypasses cost payment intentionally.)
        var ring = TheOneRingFactory.Create(_alice);

        var tap = ring.Abilities.OfType<ActivatedAbility>().Single();

        tap.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        tap.Costs.OfType<ManaCostCost>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Upkeep trigger: lose 1 life per burden counter
    // -----------------------------------------------------------------------

    [Fact]
    public void UpkeepTrigger_LosesLifeEqualToBurdenCount()
    {
        var ring = TheOneRingFactory.Create(_alice);
        PutOnBattlefield(ring);
        ring.Counters.Add(CounterType.Burden, 3);

        // Find the upkeep trigger (the only triggered ability scoped to
        // a step rather than to ETB).
        var upkeep = ring.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);

        var startLife = _alice.LifeTotal;
        foreach (var e in upkeep.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(startLife - 3);
    }

    [Fact]
    public void UpkeepTrigger_ZeroBurdens_NoLifeLoss()
    {
        var ring = TheOneRingFactory.Create(_alice);
        PutOnBattlefield(ring);

        var upkeep = ring.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);

        var startLife = _alice.LifeTotal;
        foreach (var e in upkeep.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(startLife);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private void PutOnBattlefield(Artifact ring)
    {
        _alice.Zones.Battlefield.AddCard(ring);
        ring.SetZone(ZoneType.Battlefield);
    }
}
