using FluentAssertions;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Geistlight Snare (Duskmourn, {2}{U}).
/// Oracle: "This spell costs {1} less to cast if you control a Spirit. It also
/// costs {1} less to cast if you control an enchantment.
/// Counter target spell unless its controller pays {3}."
///
/// Covers ONLY the card's unique behaviour:
///   * Identity ({2}{U}, blue Instant).
///   * Board-conditional cost reduction: neither / Spirit-only / enchantment-
///     only / both ({2}{U} → {1}{U} → {U}).
///   * Counter-unless-pay-{3}: success (pays → resolves) and failure
///     (no mana → countered into graveyard).
/// </summary>
[Trait("Color", "U")]
public class GeistlightSnareTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GeistlightSnareTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Blue_TwoU()
    {
        var snare = GeistlightSnareFactory.Create(_alice);

        snare.Name.Should().Be("Geistlight Snare");
        snare.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(snare).Should().Contain(ManaColor.Blue);
        snare.ManaCost.Should().Be("{2}{U}");
        snare.ManaCostValue.TotalValue.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Cost reduction — caster board state (CR 117.7a)
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectiveCost_PrintedTwoU_WhenControllingNoSpiritOrEnchantment()
    {
        var snare = GeistlightSnareFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(snare, _alice);

        effective.Generic.Should().Be(2);
        effective.Blue.Should().Be(1);
    }

    [Fact]
    public void EffectiveCost_DroppedByOne_WhenControllingOnlyASpirit()
    {
        var snare = GeistlightSnareFactory.Create(_alice);

        // A Spirit creature, no enchantment.
        var ghost = new Creature("Spectral Sailor", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit });
        ghost.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ghost);

        var effective = CostReduction.GetEffectiveCost(snare, _alice);

        effective.Generic.Should().Be(1,
            because: "controlling a Spirit drops {1} ({2}{U} → {1}{U})");
        effective.Blue.Should().Be(1, because: "the {U} pip is untouched (CR 117.7c)");
    }

    [Fact]
    public void EffectiveCost_DroppedByOne_WhenControllingOnlyAnEnchantment()
    {
        var snare = GeistlightSnareFactory.Create(_alice);

        // An enchantment that is not a Spirit.
        var aura = new Enchantment("Wedding Announcement", "{2}{W}");
        aura.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(aura);

        var effective = CostReduction.GetEffectiveCost(snare, _alice);

        effective.Generic.Should().Be(1,
            because: "controlling an enchantment drops {1} ({2}{U} → {1}{U})");
        effective.Blue.Should().Be(1);
    }

    [Fact]
    public void EffectiveCost_DroppedByTwo_WhenControllingBothSpiritAndEnchantment()
    {
        var snare = GeistlightSnareFactory.Create(_alice);

        var ghost = new Creature("Spectral Sailor", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit });
        ghost.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ghost);

        var aura = new Enchantment("Wedding Announcement", "{2}{W}");
        aura.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(aura);

        var effective = CostReduction.GetEffectiveCost(snare, _alice);

        effective.Generic.Should().Be(0,
            because: "both conditions apply independently, dropping {2} ({2}{U} → {U})");
        effective.Blue.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Counter unless pay {3} (CR 118.4 / 701.5)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Counters_TargetSpell_WhenControllerCannotPayThree()
    {
        var snare = GeistlightSnareFactory.Create(_alice);
        snare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snare);

        // Bob casts Lightning Bolt {R} with 0 mana → cannot pay {3} → countered.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, snare,
            GeistlightSnareFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {3} so Geistlight Snare counters his spell");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysThree()
    {
        var snare = GeistlightSnareFactory.Create(_alice);
        snare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snare);

        // Bob has {3} available → he auto-pays the rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, snare,
            GeistlightSnareFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {3} so Geistlight Snare is countered into a no-op");
    }
}
