using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HangarbackWalkerFactory"/> (Magic Origins, {X}{X}).
///
/// Covers:
/// - Identity (Artifact Creature — Construct, 0/0, {X}{X}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger places X +1/+1 counters via <see cref="Card.PendingCastX"/>;
///   X=3 → 3 counters; PendingCastX cleared post-consume.
/// - Death trigger creates N Thopter tokens (1/1 colourless artifact creature
///   with Flying) where N = +1/+1 counter count at LTB.
/// - Activated ability `{1}, {T}: +1/+1 counter` has matching cost shape and
///   adds a counter on resolution.
/// - Hardened Scales bump applies through <see cref="CountersService.Add"/>.
/// </summary>
public class HangarbackWalkerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HangarbackWalker_Identity()
    {
        var hw = HangarbackWalkerFactory.Create(_alice);

        hw.Name.Should().Be("Hangarback Walker");
        hw.ManaCost.Should().Be("{X}{X}");
        hw.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        hw.HasType(CardType.Creature).Should().BeTrue();
        hw.HasType(CardType.Artifact).Should().BeTrue(
            "Hangarback Walker is an Artifact Creature (CR 301.1 / 302.1)");
        hw.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        hw.BasePower.Should().Be(0);
        hw.BaseToughness.Should().Be(0);
        hw.Owner.Should().BeSameAs(_alice);
        hw.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HangarbackWalker_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Hangarback Walker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Hangarback Walker");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB with X +1/+1 counters
    // -----------------------------------------------------------------------

    [Fact]
    public void HangarbackWalker_EtbWithXEquals3_GainsThreePlusOneCounters()
    {
        var hw = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hw);
        hw.SetZone(ZoneType.Battlefield);

        // SpellCastFlow stamps PendingCastX after ChooseXAsync.
        hw.SetPendingCastX(3);

        var etb = hw.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CardMovedEvent(
                hw, ZoneType.Stack, ZoneType.Battlefield)));

        foreach (var e in etb.Effects) e.Execute();

        hw.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hangarback Walker enters with X (=3) +1/+1 counters per CR 122.1g");
        hw.PendingCastX.Should().BeNull(
            "PendingCastX stamp consumed once the ETB effect reads it");
    }

    [Fact]
    public void HangarbackWalker_NonCastEntry_NoCounters()
    {
        // Blink / copy entries leave PendingCastX = null.
        var hw = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hw);
        hw.SetZone(ZoneType.Battlefield);

        var etb = hw.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CardMovedEvent(
                hw, ZoneType.Hand, ZoneType.Battlefield)));

        foreach (var e in etb.Effects) e.Execute();

        hw.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void HangarbackWalker_HardenedScales_BumpsEtbCount()
    {
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<CounterAddIntent>(
            applies: (intent, _) => intent.Type == CounterType.PlusOnePlusOne,
            replace: (intent, _) => intent with { Amount = intent.Amount + 1 }));

        var hw = HangarbackWalkerFactory.Create(
            _alice, triggers: null, replacements: bus, zones: null);
        _alice.Zones.Battlefield.AddCard(hw);
        hw.SetZone(ZoneType.Battlefield);
        hw.SetPendingCastX(2);

        var etb = hw.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CardMovedEvent(
                hw, ZoneType.Stack, ZoneType.Battlefield)));
        foreach (var e in etb.Effects) e.Execute();

        hw.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hardened Scales (+1 replacement on PlusOnePlusOne placements) bumps the ETB count");
    }

    // -----------------------------------------------------------------------
    // Death trigger — N Thopter tokens with flying
    // -----------------------------------------------------------------------

    [Fact]
    public void HangarbackWalker_DeathTrigger_CreatesNThopterTokensWithFlying()
    {
        var hw = HangarbackWalkerFactory.Create(_alice);

        // Simulate Hangarback dying with 3 +1/+1 counters — bag survives the
        // zone move (Undying-shape), so the death effect reads 3 from it.
        hw.Counters.Add(CounterType.PlusOnePlusOne, 3);

        // Move to graveyard so the controller's battlefield only has the
        // tokens after the death effect resolves.
        _alice.Zones.Graveyard.AddCard(hw);
        hw.SetZone(ZoneType.Graveyard);

        var death = hw.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CardMovedEvent(
                hw, ZoneType.Battlefield, ZoneType.Graveyard)));

        foreach (var e in death.Effects) e.Execute();

        var thopters = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Thopter")
            .ToList();

        thopters.Should().HaveCount(3,
            "death trigger creates one 1/1 Thopter for each +1/+1 counter at LTB");
        thopters.Should().AllSatisfy(t =>
        {
            t.BasePower.Should().Be(1);
            t.BaseToughness.Should().Be(1);
            t.HasType(CardType.Creature).Should().BeTrue();
            t.HasType(CardType.Artifact).Should().BeTrue(
                "Thopter tokens are 'artifact creature' tokens per the printed text");
            t.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
            t.Abilities.OfType<KeywordAbility>().Should().Contain(
                k => k.Keyword == "Flying",
                "Thopter tokens have flying");
            t.Controller.Should().BeSameAs(_alice,
                "tokens enter under Hangarback Walker's controller");
        });
    }

    [Fact]
    public void HangarbackWalker_DeathTrigger_WithZeroCounters_NoTokens()
    {
        var hw = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(hw);
        hw.SetZone(ZoneType.Graveyard);

        var death = hw.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CardMovedEvent(
                hw, ZoneType.Battlefield, ZoneType.Graveyard)));
        foreach (var e in death.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .Should().BeEmpty("zero counters → zero tokens");
    }

    // -----------------------------------------------------------------------
    // Activated ability — {1}, {T}: +1/+1 counter
    // -----------------------------------------------------------------------

    [Fact]
    public void HangarbackWalker_ActivatedAbility_AddsPlusOneCounter()
    {
        var hw = HangarbackWalkerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hw);
        hw.SetZone(ZoneType.Battlefield);

        var activated = hw.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.Should().HaveCount(2,
            "{1}, {T} = two costs (mana + tap)");

        // Run the resolve effect (cost payment is owned by AbilityActivator;
        // here we exercise the resolve body the same way other v1 activated-
        // ability tests do).
        foreach (var e in activated.Effects) e.Execute();

        hw.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "activated ability adds exactly one +1/+1 counter on resolve");
    }
}
