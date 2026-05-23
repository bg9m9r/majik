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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Force of Vigor (Modern Horizons, {2}{G}{G}).
/// Mirrors the Force-of-Negation test shape:
///   * Card shape + dispatch.
///   * Pitch cast on opponent's turn — exiles a green card, no life loss.
///   * Destroys up to two artifacts and/or enchantments (a mix counts).
///   * PitchAltCostProbe surfaces a Green / 0-life candidate from
///     <see cref="PitchAltCostProbe.DefaultLookup"/>.
/// </summary>
public class ForceOfVigorFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ForceOfVigorFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Green()
    {
        var fov = ForceOfVigorFactory.Create(_alice);

        fov.Name.Should().Be("Force of Vigor");
        fov.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(fov).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsForceOfVigorFactoryShape()
    {
        var dispatched = NamedCardFactory.Create("Force of Vigor", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Force of Vigor");
    }

    [Fact]
    public async Task CastViaPitch_OnOpponentsTurn_DestroysBothTargets_AndExilesGreenCard()
    {
        // Force of Vigor in Alice's hand; pitch fuel is a second green card.
        var fov = ForceOfVigorFactory.Create(_alice);
        fov.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fov);

        var pitchFuel = new Instant("Veil of Summer", "{G}") { Owner = _alice };
        pitchFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchFuel);

        var startingLife = _alice.LifeTotal;

        // Bob has an artifact and an enchantment on the battlefield.
        var bobArtifact = new Artifact("Sol Ring", "{1}") { Owner = _bob, Controller = _bob };
        bobArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobArtifact);

        var bobEnchantment = new Enchantment("Sylvan Library", "{1}{G}") { Owner = _bob, Controller = _bob };
        bobEnchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobEnchantment);

        var pitchCost = new PitchAlternativeCost(ManaColor.Green, pitchFuel, lifeCost: 0);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bobArtifact, bobEnchantment });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, fov,
            ForceOfVigorFactory.BuildDefinition(o => o),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Force of Vigor destroys the artifact target");
        bobEnchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Force of Vigor destroys the enchantment target");
        pitchFuel.Zone.Should().Be(ZoneType.Exile,
            because: "the pitched green card is exiled (CR 118.9)");
        _alice.LifeTotal.Should().Be(startingLife,
            because: "Force of Vigor has no life rider");
    }

    [Fact]
    public async Task CastViaPitch_WithSingleTarget_DestroysJustThatOne()
    {
        // "Up to two" — casting with one chosen target is legal (CR 601.2c).
        var fov = ForceOfVigorFactory.Create(_alice);
        fov.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fov);

        var pitchFuel = new Instant("Veil of Summer", "{G}") { Owner = _alice };
        pitchFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchFuel);

        var bobArtifact = new Artifact("Sol Ring", "{1}") { Owner = _bob, Controller = _bob };
        bobArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobArtifact);

        // A bystander enchantment that should NOT be destroyed.
        var bystander = new Enchantment("Sylvan Library", "{1}{G}") { Owner = _bob, Controller = _bob };
        bystander.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bystander);

        var pitchCost = new PitchAlternativeCost(ManaColor.Green, pitchFuel, lifeCost: 0);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bobArtifact });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, fov,
            ForceOfVigorFactory.BuildDefinition(o => o),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard);
        bystander.Zone.Should().Be(ZoneType.Battlefield,
            because: "only the chosen target is destroyed");
    }

    [Fact]
    public void PitchAltCostProbe_DefaultLookup_RecognisesForceOfVigor_GreenZeroLife()
    {
        var fov = ForceOfVigorFactory.Create(_alice);
        fov.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fov);

        var greenFuel = new Instant("Veil of Summer", "{G}") { Owner = _alice };
        greenFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(greenFuel);

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 1, PhaseStateType.Main, _stack);

        var candidates = probe.CandidatesFor(fov, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        var pitch = candidates[0].Should().BeOfType<PitchAlternativeCost>().Subject;
        pitch.RequiredColor.Should().Be(ManaColor.Green);
        pitch.LifeCost.Should().Be(0);
        pitch.ExiledCard.Should().BeSameAs(greenFuel);
    }
}
