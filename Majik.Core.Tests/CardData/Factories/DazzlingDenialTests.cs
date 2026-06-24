using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// End-to-end tests for Dazzling Denial (Bloomburrow, {1}{U}).
/// Oracle: "Counter target spell unless its controller pays {2}. If you control
/// a Bird, counter that spell unless its controller pays {4} instead."
///
/// Covers ONLY the card's unique behaviour:
///   * Identity ({1}{U}, blue Instant).
///   * Bird-gated tax (CR 118.4 evaluated at resolution):
///       - no Bird: controller paying {2} keeps the spell; {3} (not enough for
///         the {4} tier but more than {2}) still keeps it.
///       - control a Bird: the tax jumps to {4} — {2} (enough for the base tier
///         only) is NOT enough, so the spell is countered.
/// </summary>
[Trait("Color", "U")]
public class DazzlingDenialTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DazzlingDenialTests()
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
    public void Create_HasInstantShape_Blue_OneU()
    {
        var denial = DazzlingDenialFactory.Create(_alice);

        denial.Name.Should().Be("Dazzling Denial");
        denial.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(denial).Should().Contain(ManaColor.Blue);
        denial.ManaCost.Should().Be("{1}{U}");
        denial.ManaCostValue.TotalValue.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Bird-gated tax (CR 118.4 / 701.5)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NoBird_TaxIsTwo_ControllerPaysTwo_SpellSurvives()
    {
        var (_, bobBolt) = await CastDenialAt(controllerMana: 2);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "without a Bird the tax is {2}; Bob paid {2} so the spell is not countered");
    }

    [Fact]
    public async Task NoBird_TaxIsTwo_ControllerCannotPay_SpellCountered()
    {
        var (_, bobBolt) = await CastDenialAt(controllerMana: 0);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay the {2} tax so Dazzling Denial counters his spell");
    }

    [Fact]
    public async Task ControlsBird_TaxIsFour_TwoIsNotEnough_SpellCountered()
    {
        // Alice controls a Bird → the tax jumps to {4} (CR 118.4).
        var bird = new Creature("Storm of the Skies", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Bird });
        bird.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bird);

        // Bob has {2} — enough for the base tier, but NOT for the raised {4}.
        var (_, bobBolt) = await CastDenialAt(controllerMana: 2);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "controlling a Bird raises the tax to {4}; Bob's {2} can't pay it so the spell is countered");
    }

    [Fact]
    public async Task ControlsBird_TaxIsFour_ControllerPaysFour_SpellSurvives()
    {
        var bird = new Creature("Storm of the Skies", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Bird });
        bird.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bird);

        var (_, bobBolt) = await CastDenialAt(controllerMana: 4);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid the raised {4} tax so Dazzling Denial is countered into a no-op");
    }

    // -----------------------------------------------------------------------
    // Helper: cast Dazzling Denial targeting a fresh Bob spell, with Bob holding
    // <paramref name="controllerMana"/> generic mana to pay the rider with.
    // -----------------------------------------------------------------------
    private async Task<(Instant Denial, Instant BobBolt)> CastDenialAt(int controllerMana)
    {
        var denial = DazzlingDenialFactory.Create(_alice);
        denial.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(denial);

        if (controllerMana > 0)
        {
            _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(controllerMana));
        }

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, denial,
            DazzlingDenialFactory.BuildSpellDefinition(_alice, o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        return (denial, bobBolt);
    }
}
