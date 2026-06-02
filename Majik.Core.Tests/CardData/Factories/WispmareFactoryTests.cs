using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Wispmare (Lorwyn / Modern Horizons reprints, {2}{W}, Creature —
/// Elemental 1/3).
///
/// Oracle:
///   "Flying
///    When this creature enters, destroy target enchantment.
///    Evoke {W}"
///
/// Near-sibling of <see cref="FoundationBreakerFactory"/> (white evoke
/// Elemental with an ETB destroy trigger), differing in three ways:
///   - Flying keyword marker.
///   - The ETB destroy is <b>mandatory</b> and <b>enchantment-only</b>
///     (MinTargets = MaxTargets = 1), not the "you may artifact or
///     enchantment" of Foundation Breaker.
///   - Evoke cost is pure-mana {W}.
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Elemental subtype, owner /
///     controller) + NamedCardFactory dispatch.
///   - Ability shape: Flying + Evoke markers, evoke-sacrifice trigger, ETB
///     "destroy target enchantment" trigger with MinTargets = 1.
///   - Normal cast -> ETB destroys target enchantment + Wispmare stays.
///   - Evoke cast -> ETB destroys target enchantment + evoke-sac sends
///     Wispmare to its owner's graveyard.
///   - Illegal target on resolution (non-enchantment) -> no destroy.
/// </summary>
[Trait("Color", "W")]
public class WispmareFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public WispmareFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasCorrectShape()
    {
        var w = WispmareFactory.Create(_alice);

        w.Name.Should().Be("Wispmare");
        w.ManaCost.Should().Be("{2}{W}");
        w.BasePower.Should().Be(1);
        w.BaseToughness.Should().Be(3);
        w.HasType(CardType.Creature).Should().BeTrue();
        w.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        w.Owner.Should().BeSameAs(_alice);
        w.Controller.Should().BeSameAs(_alice);

        var keywords = w.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[] { "Flying", "Evoke" });

        // Two triggered abilities: ETB destroy + evoke sacrifice.
        w.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }
    [Fact]
    public void EtbTrigger_HasMandatorySingleTarget_Enchantment()
    {
        var w = WispmareFactory.Create(_alice);

        var destroyTrigger = w.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

        destroyTrigger.TargetRequests.Should().HaveCount(1);
        destroyTrigger.TargetRequests[0].MinTargets.Should().Be(1,
            "the printed text is a mandatory 'destroy target enchantment'");
        destroyTrigger.TargetRequests[0].MaxTargets.Should().Be(1);
        destroyTrigger.TargetRequests[0].Description.Should()
            .Contain("enchantment");
    }

    // ── ETB destroy ──────────────────────────────────────────────────────

    [Fact]
    public async Task NormalCast_TargetEnchantment_DestroysIt_AndWispmareStays()
    {
        var w = WispmareInHand(_alice);

        var aura = new Enchantment("Bob's Aura", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        await CastNormalAndResolveAndTarget(w, aura);

        // Enchantment destroyed.
        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);

        // Wispmare stays on the battlefield — no evoke sacrifice because the
        // evoke cost wasn't paid.
        w.Zone.Should().Be(ZoneType.Battlefield);
        w.EvokeWasPaid.Should().BeFalse();
    }

    [Fact]
    public async Task NormalCast_IllegalTargetAtResolution_Creature_NoDestroy()
    {
        // ChosenTargets is set to a Creature (not an enchantment). CR 608.2b:
        // illegal target at resolve -> no destroy. Wispmare still stays.
        var w = WispmareInHand(_alice);

        var bear = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        await CastNormalAndResolveAndTarget(w, bear);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        w.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Evoke path ───────────────────────────────────────────────────────

    [Fact]
    public async Task EvokeCast_DestroysTarget_ThenSacrificesWispmare()
    {
        var w = WispmareInHand(_alice);

        var aura = new Enchantment("Bob's Aura", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        // Evoke {W} — pure-mana evoke (no pitch component).
        var evokeCost = new EvokeAlternativeCost(ManaCost.Parse("{W}"));

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, w,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        w.Zone.Should().Be(ZoneType.Battlefield);
        w.EvokeWasPaid.Should().BeTrue();

        // Both triggers fired on the ETB CardMovedEvent.
        _triggers.PendingCount.Should().Be(2);

        // Point the destroy trigger at Bob's aura.
        var destroyTrigger = w.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        destroyTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aura },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Aura destroyed AND Wispmare sacrificed.
        aura.Zone.Should().Be(ZoneType.Graveyard);
        w.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(w);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private Creature WispmareInHand(Player owner)
    {
        var w = WispmareFactory.Create(owner);
        w.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(w);
        return w;
    }

    private async Task CastNormalAndResolveAndTarget(Creature w, Permanent target)
    {
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, w,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Only the destroy trigger should be pending — evoke sac's
        // intervening-if (EvokeWasPaid == false) drops it.
        var destroyTrigger = w.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        destroyTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }
    }
}
