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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EndlessOneFactory"/> (Battle for Zendikar, {X}).
///
/// Covers:
/// - Identity (Creature — Eldrazi, {X}, 0/0, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="Card.ManaCostValue.HasX"/> reports true (X-cost spell).
/// - ETB trigger placed via <see cref="Card.PendingCastX"/>; X=3 → 3
///   +1/+1 counters; PendingCastX cleared post-consume.
/// - X=0 → no counters added (and PendingCastX still cleared).
/// - Hardened Scales bump applies through <see cref="CountersService.Add"/>.
/// </summary>
public class EndlessOneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EndlessOne_Identity()
    {
        var eo = EndlessOneFactory.Create(_alice);

        eo.Name.Should().Be("Endless One");
        eo.ManaCost.Should().Be("{X}");
        eo.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        eo.HasType(CardType.Creature).Should().BeTrue();
        eo.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        eo.BasePower.Should().Be(0);
        eo.BaseToughness.Should().Be(0);
        eo.Owner.Should().BeSameAs(_alice);
        eo.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EndlessOne_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Endless One", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Endless One");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(0);
        ((Creature)card).BaseToughness.Should().Be(0);
    }

    [Fact]
    public void EndlessOne_AttachesEtbTrigger()
    {
        var eo = EndlessOneFactory.Create(_alice);
        eo.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Endless One has exactly one ETB trigger");
    }

    [Fact]
    public void EndlessOne_EtbWithXEquals3_GainsThreePlusOneCounters()
    {
        var eo = EndlessOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);

        // SpellCastFlow stamps PendingCastX after ChooseXAsync; simulate.
        eo.SetPendingCastX(3);

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Endless One enters with X (=3) +1/+1 counters per CR 122.1g");
        eo.PendingCastX.Should().BeNull(
            "PendingCastX stamp consumed once the ETB effect reads it — re-entries don't double-count");
    }

    [Fact]
    public void EndlessOne_EtbWithXEquals0_NoCountersPlaced_AndStateLossViaSba()
    {
        var eo = EndlessOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.SetPendingCastX(0);

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X=0 → zero counters placed");
        eo.PendingCastX.Should().BeNull(
            "PendingCastX cleared regardless of X value");
        // 0/0 + 0 counters → SBA-fodder. The Power/Toughness check is the
        // observable state — SBA loop itself isn't run here.
        eo.BasePower.Should().Be(0);
        eo.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void EndlessOne_NoPendingX_NonCastEntry_NoCountersPlaced()
    {
        // Non-cast entries (blink, copy, etc.) leave PendingCastX = null —
        // the ETB effect must no-op rather than throw or place arbitrary
        // counters.
        var eo = EndlessOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.PendingCastX.Should().BeNull();

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-cast entry leaves Endless One as a 0/0 with no counters (CR 122.1g — no chosen X)");
    }

    [Fact]
    public void EndlessOne_RoutesThroughCountersService_HardenedScalesBumpsApply()
    {
        // Hardened Scales rewrites +1/+1 counter placements via a
        // ReplacementBus subscriber. Wire EO with a replacement bus +
        // register Hardened Scales' factory replacement, then cast for X=2.
        // Expected: 2 + 1 = 3 counters land.
        var bus = new ReplacementBus();
        // Bump amount by 1 for every PlusOnePlusOne placement (sufficient
        // to simulate Hardened Scales without instantiating the factory).
        bus.Register(new Majik.Core.Effects.LambdaReplacement<CounterAddIntent>(
            applies: (intent, _) => intent.Type == CounterType.PlusOnePlusOne,
            replace: (intent, _) => intent with { Amount = intent.Amount + 1 }));

        var eo = EndlessOneFactory.Create(_alice, triggers: null, replacements: bus);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.SetPendingCastX(2);

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hardened Scales (or any +1 replacement on PlusOnePlusOne placements) bumps the count via CountersService.Add");
    }
}
