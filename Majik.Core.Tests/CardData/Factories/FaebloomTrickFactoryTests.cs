using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FaebloomTrickFactory"/>.
///
/// Faebloom Trick (Bloomburrow, {1}{U}). Instant. Oracle text
/// (verified against Scryfall):
///   "Create two 1/1 blue Faerie creature tokens with flying. When you do,
///    tap target creature an opponent controls."
///
/// ## v1 model
/// The printed text wraps the tap in a reflexive trigger ("When you do, …").
/// Because token creation is unconditional (the spell always makes the two
/// Faeries), the reflexive trigger always fires, so the v1 model collapses to
/// a single-target spell: at resolution it creates the two flying Faerie
/// tokens (CR 111 / 111.4) and then taps the chosen target (CR 701.20). This
/// matches the cast-time single-target posture of
/// <see cref="IntoTheFloodMawFactory"/>. The reflexive-trigger nuance (the
/// tap going on the stack as a separate object) is a documented v1
/// simplification — gameplay-equivalent because the tap target is mandatory
/// whenever an opponent controls a creature.
///
/// Coverage (unique behaviour only — CardFactoryContractTests covers
/// dispatch + well-formedness for every implemented card):
///   * Identity per Scryfall (Instant, {1}{U}, mana value 2, blue).
///   * Resolution creates exactly two 1/1 blue Faerie creature tokens with
///     flying under the caster.
///   * Resolution taps the chosen target creature an opponent controls.
///   * Targeting the caster's own creature is illegal at resolution
///     (CR 608.2b — "an opponent controls"); the tap is a no-op but the
///     tokens are still created.
/// </summary>
[Trait("Color", "U")]
public class FaebloomTrickFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FaebloomTrickFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void FaebloomTrick_Identity()
    {
        var card = FaebloomTrickFactory.Create(_alice);

        card.Name.Should().Be("Faebloom Trick");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().ContainSingle()
            .Which.Should().Be(ManaColor.Blue, "single {U} pip — mono-blue (CR 202.2c)");
        card.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {1}{U} has mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public async Task Resolve_CreatesTwoFlyingFaeries_AndTapsOpponentCreature()
    {
        // Bob controls a 2/2 Bear he owns — the tap target.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.IsTapped.Should().BeFalse("precondition — bear starts untapped");

        var card = FaebloomTrickFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            FaebloomTrickFactory.BuildDefinition(_alice, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Two 1/1 blue Faerie creature tokens with flying under the caster.
        var faeries = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Faerie))
            .ToList();
        faeries.Should().HaveCount(2, "CR 111 — two Faerie tokens created");
        faeries.Should().OnlyContain(f =>
            f.BasePower == 1 && f.BaseToughness == 1,
            "1/1 stats");
        foreach (var f in faeries)
        {
            CardColors.GetColors(f).Should().Contain(ManaColor.Blue, "blue Faerie token");
            f.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Flying", "CR 702.9 — Faerie tokens have flying");
        }

        // Tap rider — Bob's bear is tapped (CR 701.20).
        bear.IsTapped.Should().BeTrue("Faebloom Trick taps target creature an opponent controls");
    }

    [Fact]
    public async Task Resolve_OwnCreatureTarget_TapIsNoOp_TokensStillCreated()
    {
        // Alice's own creature is NOT a legal target ("an opponent controls").
        var ally = new Creature("Ally", "{G}", 1, 1)
        { Owner = _alice, Controller = _alice };
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);

        var card = FaebloomTrickFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)ally });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            FaebloomTrickFactory.BuildDefinition(_alice, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — own creature is illegal; the tap does nothing.
        ally.IsTapped.Should().BeFalse("CR 608.2b — own creature is not a legal target for the tap");

        // The two Faerie tokens are still created regardless (unconditional).
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Faerie))
            .Should().Be(2, "token creation is unconditional");
    }
}
