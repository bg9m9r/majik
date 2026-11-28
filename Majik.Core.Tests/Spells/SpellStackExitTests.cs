using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Spells;

/// <summary>
/// One rule for where a spell's card goes when it leaves the stack
/// (SpellStackExit), on every exit: resolution (CR 608.2n), a counter
/// (CR 701.6a), and the 608.2b "all targets illegal" removal.
///   * flashback / jump-start: exile on ANY exit (CR 702.34a, 702.133a);
///   * rebound: exile only on resolution of a spell cast from hand (CR 702.88a);
///   * buyback: to hand only on resolution (CR 702.27a);
///   * a copy: ceases to exist, its card never moves (CR 707.10a, 704.5e);
///   * a counter's own destination ("exile it instead", "put it on top of
///     its owner's library") stands otherwise.
/// </summary>
public class SpellStackExitTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    private (Majik.Core.Stack.Stack Stack, Spell Spell, Instant Card) OnStack(Action<Spell>? stamp = null, Action<Instant>? setup = null)
    {
        var stack = new Majik.Core.Stack.Stack(_bus) { ZoneService = new ZoneService(_bus) };
        var card = new Instant("Think Twice", "{1}{U}") { Owner = _alice };
        card.SetController(_alice);
        setup?.Invoke(card);
        card.SetZone(ZoneType.Stack);
        _alice.Zones.Stack.AddCard(card);
        var spell = new Spell(card, _alice);
        stamp?.Invoke(spell);
        stack.Push(spell);
        return (stack, spell, card);
    }

    private void ShouldBeOnlyIn(ICard card, ZoneType zone)
    {
        card.Zone.Should().Be(zone);
        foreach (var z in new[] { ZoneType.Stack, ZoneType.Graveyard, ZoneType.Exile, ZoneType.Hand, ZoneType.Library })
            _alice.Zones.GetZone(z).GetCards().Contains(card).Should().Be(z == zone, $"{card.Name} is listed in {z} only if it is there");
    }

    [Theory]
    [InlineData(StackExitKind.Resolved, ZoneType.Graveyard)]
    [InlineData(StackExitKind.Countered, ZoneType.Graveyard)]
    [InlineData(StackExitKind.Countered, ZoneType.Library)]
    [InlineData(StackExitKind.Countered, ZoneType.Hand)]
    [InlineData(StackExitKind.Fizzled, ZoneType.Graveyard)]
    public void Flashback_ExilesOnEveryExit(StackExitKind kind, ZoneType intended)
    {
        var (_, spell, _) = OnStack(s => s.ExileWhenLeavesStack = true);
        SpellStackExit.Destination(spell, kind, intended).Should().Be(ZoneType.Exile);
    }

    [Theory]
    [InlineData(StackExitKind.Resolved, ZoneType.Graveyard, ZoneType.Hand)]
    [InlineData(StackExitKind.Countered, ZoneType.Graveyard, ZoneType.Graveyard)]
    [InlineData(StackExitKind.Fizzled, ZoneType.Graveyard, ZoneType.Graveyard)]
    public void Buyback_ReturnsToHandOnlyOnResolution(StackExitKind kind, ZoneType intended, ZoneType expected)
    {
        var (_, spell, _) = OnStack(s => s.ReturnToHandOnResolution = true);
        SpellStackExit.Destination(spell, kind, intended).Should().Be(expected);
    }

    [Theory]
    [InlineData(true, StackExitKind.Resolved, ZoneType.Exile)]
    [InlineData(false, StackExitKind.Resolved, ZoneType.Graveyard)]
    [InlineData(true, StackExitKind.Countered, ZoneType.Graveyard)]
    [InlineData(true, StackExitKind.Fizzled, ZoneType.Graveyard)]
    public void Rebound_ExilesOnlyOnResolutionOfASpellCastFromHand(bool fromHand, StackExitKind kind, ZoneType expected)
    {
        var (_, spell, _) = OnStack(s => s.WasCastFromHand = fromHand,
            c => c.AddAbility(new KeywordAbility("Rebound", c, _alice)));
        SpellStackExit.Destination(spell, kind, ZoneType.Graveyard).Should().Be(expected);
    }

    [Theory]
    [InlineData(StackExitKind.Resolved)]
    [InlineData(StackExitKind.Countered)]
    [InlineData(StackExitKind.Fizzled)]
    public void ACopy_CeasesToExist(StackExitKind kind)
    {
        var (_, spell, _) = OnStack(s => s.IsCopy = true);
        SpellStackExit.Destination(spell, kind, ZoneType.Graveyard).Should().BeNull();
    }

    [Theory]
    [InlineData(ZoneType.Exile)]
    [InlineData(ZoneType.Library)]
    [InlineData(ZoneType.Hand)]
    public void ACountersOwnDestination_StandsForAPlainSpell(ZoneType intended)
    {
        var (_, spell, _) = OnStack();
        SpellStackExit.Destination(spell, StackExitKind.Countered, intended).Should().Be(intended);
    }

    [Fact]
    public void CounteringAFlashbackSpell_ExilesItsCard()
    {
        var (stack, spell, card) = OnStack(s => s.ExileWhenLeavesStack = true);

        OracleSpellBinder.CounterSpell(stack, spell).Should().BeTrue();

        stack.IsEmpty.Should().BeTrue();
        ShouldBeOnlyIn(card, ZoneType.Exile);
    }

    [Fact]
    public void CounteringAPlainSpell_PutsItsCardInTheGraveyard_ThroughTheZoneService()
    {
        var (stack, spell, card) = OnStack();
        var moves = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(moves.Add);

        OracleSpellBinder.CounterSpell(stack, spell);

        ShouldBeOnlyIn(card, ZoneType.Graveyard);
        moves.Should().ContainSingle(m => ReferenceEquals(m.Card, card) && m.ToZone == ZoneType.Graveyard);
    }

    [Fact]
    public void CounteringACopy_LeavesTheOriginalCardWhereItIs()
    {
        var (stack, original, card) = OnStack();
        var copy = new Spell(card, _alice) { IsCopy = true };
        stack.Push(copy);

        OracleSpellBinder.CounterSpell(stack, copy).Should().BeTrue();

        stack.GetAll().Should().ContainSingle().Which.Should().BeSameAs(original);
        ShouldBeOnlyIn(card, ZoneType.Stack);
    }

    [Fact]
    public void Fx_Counter_UsesTheSameRule()
    {
        var (stack, spell, card) = OnStack(s => s.ExileWhenLeavesStack = true);

        Majik.Core.Primitives.Fx.Counter(stack, spell);

        ShouldBeOnlyIn(card, ZoneType.Exile);
    }

    [Fact]
    public async Task AResolvingFlashbackSpell_IsExiled_AndListedOnlyInExile()
    {
        var (stack, _, card) = OnStack(s => s.ExileWhenLeavesStack = true);

        await new StackResolver(_bus, stack.ZoneService).ResolveTopAsync(stack);

        ShouldBeOnlyIn(card, ZoneType.Exile);
    }

    [Fact]
    public async Task AResolvingBuybackSpell_ReturnsToHand()
    {
        var (stack, _, card) = OnStack(s => s.ReturnToHandOnResolution = true);

        await new StackResolver(_bus, stack.ZoneService).ResolveTopAsync(stack);

        ShouldBeOnlyIn(card, ZoneType.Hand);
    }

    [Fact]
    public async Task AFlashbackSpellWithEveryTargetIllegal_IsExiled()
    {
        var (stack, spell, card) = OnStack(s => s.ExileWhenLeavesStack = true);
        spell.ChosenTargets.Add(new object());
        spell.TargetLegalityPredicate = _ => false;

        await new StackResolver(_bus, stack.ZoneService).ResolveTopAsync(stack);

        ShouldBeOnlyIn(card, ZoneType.Exile);
    }

    [Fact]
    public async Task ASpellCastWithFlashback_ThenCountered_IsExiled()
    {
        // The cast stamps the rule (SpellCastFlow), so every exit applies it.
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        stack.ZoneService = zones;
        var flow = new Majik.Core.Game.SpellCastFlow(stack, zones, _bus);
        var card = new Instant("Think Twice", "{1}{U}") { Owner = _alice };
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        var def = new Majik.Core.Game.SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: Array.Empty<Majik.Core.Players.Agents.TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("draw", () => { }) });
        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueMana(Majik.Core.Players.Agents.ManaPayment.Empty);
        var ctx = new Majik.Core.Game.GameContext(_alice, new[] { _alice }, _alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var spell = await flow.CastAsync(_alice, card, def, agent, ctx,
            alternativeCost: new Majik.Core.Costs.FlashbackAlternativeCost(Majik.Core.ValueObjects.ManaCost.Zero));
        ((Spell)spell).ExileWhenLeavesStack.Should().BeTrue();

        Majik.Core.Primitives.Fx.Counter(stack, spell);

        ShouldBeOnlyIn(card, ZoneType.Exile);
    }
}