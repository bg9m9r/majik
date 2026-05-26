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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Force of Rage (Modern Horizons, {2}{R}{R}).
/// Mirrors the Force-of-Despair test shape:
///   * Card shape + dispatch.
///   * Pitch cast on opponent's turn — exiles a red card, no life loss.
///   * Resolve spawns three 3/1 red Elemental tokens with Trample + Haste.
///   * Delayed end-step trigger sacrifices the three tokens.
///   * PitchAltCostProbe surfaces a Red / 0-life candidate from
///     <see cref="PitchAltCostProbe.DefaultLookup"/>.
/// </summary>
public class ForceOfRageFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ForceOfRageFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasSorceryShape_Red()
    {
        var four = ForceOfRageFactory.Create(_alice);

        four.Name.Should().Be("Force of Rage");
        four.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(four).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsForceOfRageShape()
    {
        var dispatched = NamedCardFactory.Create("Force of Rage", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Force of Rage");
    }

    [Fact]
    public void PitchAltCostProbe_DefaultLookup_RecognisesForceOfRage_RedZeroLife()
    {
        var four = ForceOfRageFactory.Create(_alice);
        four.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(four);

        var redFuel = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        redFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(redFuel);

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 1, PhaseStateType.Main, _stack);

        var candidates = probe.CandidatesFor(four, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        var pitch = candidates[0].Should().BeOfType<PitchAlternativeCost>().Subject;
        pitch.RequiredColor.Should().Be(ManaColor.Red);
        pitch.LifeCost.Should().Be(0);
        pitch.ExiledCard.Should().BeSameAs(redFuel);
    }

    [Fact]
    public void Resolve_SpawnsThreeRedElementalTokens_TrampleAndHaste()
    {
        var def = ForceOfRageFactory.BuildSpellDefinition(_alice, _zones, triggers: null);
        var picks = new ChosenSpellParams(null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        tokens.Should().HaveCount(3, because: "Force of Rage creates three tokens");
        tokens.Should().AllSatisfy(t =>
        {
            t.IsToken.Should().BeTrue();
            t.Power.Should().Be(3);
            t.Toughness.Should().Be(1);
            t.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
            CardColors.GetColors(t).Should().Contain(ManaColor.Red);
            t.HasSummoningSickness.Should().BeFalse(
                because: "Haste lifts summoning sickness (CR 702.10b)");
            // Keywords are attached via KeywordAbility markers.
            var kws = t.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
            kws.Should().Contain("Trample");
            kws.Should().Contain("Haste");
        });
    }

    [Fact]
    public void Resolve_DelayedTrigger_SacrificesTokens_AtNextEndStep()
    {
        var triggers = new TriggerManager(_stack, _bus);

        var def = ForceOfRageFactory.BuildSpellDefinition(_alice, _zones, triggers);
        var picks = new ChosenSpellParams(null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        tokens.Should().HaveCount(3);

        // Fire the next End step — the delayed trigger should match and
        // queue itself onto the stack.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        // Resolve everything on the stack — the delayed trigger sacrifices
        // each token.
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // CR 704.5d — tokens cease to exist once they leave the battlefield.
        // Factory drops them from the graveyard to mirror the live SBA pass.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty(
            because: "all three Elemental tokens were sacrificed at next end step");
        foreach (var t in tokens)
        {
            t.Zone.Should().NotBe(ZoneType.Battlefield,
                because: "token sacrificed (CR 701.16)");
        }
    }

    [Fact]
    public async Task CastViaPitch_OnOpponentsTurn_ExilesRedCard_NoLifeLoss()
    {
        var four = ForceOfRageFactory.Create(_alice);
        four.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(four);

        var redFuel = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        redFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(redFuel);

        var startingLife = _alice.LifeTotal;

        var pitchCost = new PitchAlternativeCost(ManaColor.Red, redFuel, lifeCost: 0);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        // NOTE: Force of Rage is a sorcery, but its pitch alt-cost is what
        // gates it — pitching on opponent's turn (CR 118.9 + Force-cycle
        // not-your-turn restriction) cuts through the sorcery-speed gate
        // (CR 117.1) the same way Force of Despair does.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, four,
            ForceOfRageFactory.BuildSpellDefinition(_alice, _zones, triggers: null),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        redFuel.Zone.Should().Be(ZoneType.Exile,
            because: "pitched red card is exiled (CR 118.9)");
        _alice.LifeTotal.Should().Be(startingLife,
            because: "Force of Rage has no life rider");

        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        tokens.Should().HaveCount(3,
            because: "Force of Rage resolves on the stack and spawns three tokens");
    }
}
