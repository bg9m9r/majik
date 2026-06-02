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
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KnightOfAutumnFactory"/>.
///
/// Card: Knight of Autumn — Creature — Dryad Knight {1}{G}{W}, 2/1.
///   "When this creature enters, choose one —
///    • Put two +1/+1 counters on this creature.
///    • Destroy target artifact or enchantment.
///    • You gain 4 life."
///
/// Covers:
/// - Identity ({1}{G}{W} Creature — Dryad Knight, 2/1, GW colors, mana value 3).
/// - Exactly one battlefield-active ETB modal triggered ability.
/// - Mode 0 (two +1/+1 counters): Knight gains two +1/+1 counters → 4/3.
/// - Mode 1 (destroy artifact or enchantment): a targeted artifact / enchantment
///   is destroyed; an opponent's creature (illegal target) is a clean no-op.
/// - Mode 2 (gain 4 life): controller gains exactly 4 life.
/// </summary>
[Trait("Color", "GW")]
public class KnightOfAutumnFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KnightOfAutumn_Identity()
    {
        var c = KnightOfAutumnFactory.Create(_alice);

        c.Name.Should().Be("Knight of Autumn");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Dryad).Should().BeTrue("Knight of Autumn is a Dryad");
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue("Knight of Autumn is a Knight");
        c.ManaCost.Should().Be("{1}{G}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KnightOfAutumn_IsGreenWhite()
    {
        var c = KnightOfAutumnFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "{G} pip");
        colors.Should().Contain(ManaColor.White, "{W} pip");
        colors.Should().HaveCount(2, "exactly two colors");
    }

    [Fact]
    public void KnightOfAutumn_ManaValue_IsThree()
    {
        var c = KnightOfAutumnFactory.Create(_alice);

        // {1}{G}{W} = mana value 3 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(3, "CR 202.3 — {1}{G}{W} has mana value 3");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void KnightOfAutumn_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = KnightOfAutumnFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB modal trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — Put two +1/+1 counters on this creature
    // -----------------------------------------------------------------------

    [Fact]
    public void KnightOfAutumn_Mode0_AddsTwoPlusOneCounters()
    {
        var knight = KnightOfAutumnFactory.Create(_alice, mode: KnightOfAutumnFactory.ModeCounters);

        var etb = knight.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        knight.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "mode 0 puts two +1/+1 counters on Knight of Autumn");
    }

    [Fact]
    public void KnightOfAutumn_Mode0_CountersReflectInComputedPT()
    {
        var effects = new ContinuousEffectsService();
        var knight = KnightOfAutumnFactory.Create(_alice, mode: KnightOfAutumnFactory.ModeCounters);
        knight.SetZone(ZoneType.Battlefield);

        var etb = knight.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var chars = effects.Compute(knight);
        chars.Power.Should().Be(4, "base 2 + two +1/+1 counters");
        chars.Toughness.Should().Be(3, "base 1 + two +1/+1 counters");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Destroy target artifact or enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void KnightOfAutumn_Mode1_DestroysTargetArtifact()
    {
        var knight = KnightOfAutumnFactory.Create(_alice, mode: KnightOfAutumnFactory.ModeDestroy);

        var signet = new Artifact("Boros Signet", "{2}");
        signet.SetOwner(_bob);
        signet.SetController(_bob);
        signet.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(signet);

        var etb = knight.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { signet } });
        foreach (var effect in etb.Effects) effect.Execute();

        signet.Zone.Should().Be(ZoneType.Graveyard,
            "mode 1 destroys the targeted artifact (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(signet,
            "destroyed permanent goes to its owner's graveyard");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(signet);
    }

    [Fact]
    public void KnightOfAutumn_Mode1_DestroysTargetEnchantment()
    {
        var knight = KnightOfAutumnFactory.Create(_alice, mode: KnightOfAutumnFactory.ModeDestroy);

        var oblivion = new Enchantment("Oblivion Ring", "{2}{W}");
        oblivion.SetOwner(_bob);
        oblivion.SetController(_bob);
        oblivion.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(oblivion);

        var etb = knight.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { oblivion } });
        foreach (var effect in etb.Effects) effect.Execute();

        oblivion.Zone.Should().Be(ZoneType.Graveyard,
            "mode 1 destroys the targeted enchantment (CR 701.7)");
    }

    [Fact]
    public void KnightOfAutumn_Mode1_IllegalTarget_IsNoOp()
    {
        var knight = KnightOfAutumnFactory.Create(_alice, mode: KnightOfAutumnFactory.ModeDestroy);

        // A creature is NOT a legal "artifact or enchantment" target.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var etb = knight.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "a creature is an illegal target — clean no-op (CR 608.2b)");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Mode 2 — You gain 4 life
    // -----------------------------------------------------------------------

    [Fact]
    public void KnightOfAutumn_Mode2_ControllerGainsFourLife()
    {
        var knight = KnightOfAutumnFactory.Create(_alice, mode: KnightOfAutumnFactory.ModeGainLife);

        var etb = knight.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(24,
            "mode 2 gains controller exactly 4 life (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // Wired path: bus event triggers ETB end-to-end (mode 2)
    // -----------------------------------------------------------------------

    [Fact]
    public void KnightOfAutumn_WiredCreate_Mode2_EnteringBattlefield_GainsFourLife()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var knight = KnightOfAutumnFactory.Create(
            alice, mode: KnightOfAutumnFactory.ModeGainLife, triggers: triggerManager);
        knight.SetZone(ZoneType.Battlefield);

        bus.Publish(new CardMovedEvent(knight, ZoneType.Hand, ZoneType.Battlefield));

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0) stack.Pop()?.Resolve();

        alice.LifeTotal.Should().Be(24,
            "entering the battlefield via the bus with mode 2 gains controller 4 life end-to-end");
    }
}
