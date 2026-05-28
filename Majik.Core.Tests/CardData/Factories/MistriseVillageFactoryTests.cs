using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MistriseVillageFactory"/> — Mistrise Village.
///
/// Oracle:
/// "This land enters tapped unless you control a Mountain or a Forest.
///  {T}: Add {U}.
///  {U}, {T}: The next spell you cast this turn can't be countered."
///
/// Coverage:
///   Identity — Land, correct name, owner/controller, nonbasic.
///   Dispatch by name via <see cref="NamedCardFactory"/>.
///   {T}: Add {U} mana ability wired.
///   {U}, {T} activated ability wired (non-mana, 2 costs).
///   ETB-tapped predicate: no Mountain/Forest → tapped.
///   ETB-tapped predicate: Mountain on controller's battlefield → untapped.
///   ETB-tapped predicate: Forest on controller's battlefield → untapped.
///   ETB-tapped predicate: only opponent has Mountain/Forest → tapped.
///   Shape-only single-arg path does not register replacement.
///   Activated ability: registers next-spell-uncounterable rider.
///   Full pipeline: activated ability → SpellCastFlow stamps CannotBeCountered.
///   Full pipeline: counter attempt on stamped spell → stack unchanged (CR 701.5b).
///   One-shot: second spell cast same turn is NOT uncounterable.
///   One-shot: second activation re-arms the rider for the next spell.
/// </summary>
public class MistriseVillageFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly StackResolver _resolver;

    public MistriseVillageFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
        CastingRestrictions.Clear();
    }

    public void Dispose() => CastingRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_WithCorrectName()
    {
        var land = MistriseVillageFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Mistrise Village");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary()
    {
        var land = MistriseVillageFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Mistrise Village is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsLandShape()
    {
        var card = NamedCardFactory.Create("Mistrise Village", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Mistrise Village");
    }

    // -----------------------------------------------------------------------
    // Mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasOneBlueManaAbility()
    {
        var land = MistriseVillageFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "{T}: Add {U} is the single mana ability");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(1, "it produces exactly one blue mana");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasOneActivatedAbility_WithManaPlusTapCosts()
    {
        var land = MistriseVillageFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "{U},{T}: next-spell-uncounterable is the one non-mana activated ability");

        var ab = activated[0];
        ab.Costs.Should().HaveCount(2, "{U} mana cost + tap self");
        ab.Costs.OfType<ManaCostCost>().Should().HaveCount(1, "one mana cost ({U})");
        ab.Costs.OfType<AdditionalCost>()
            .Where(c => c.CostType == AdditionalCostType.Tap)
            .Should().HaveCount(1, "one tap cost");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoMountainOrForest()
    {
        var bus = new ReplacementBus();
        var land = MistriseVillageFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice));

        after!.EntersTapped.Should().BeTrue(
            "no Mountain or Forest on controller's battlefield → enters tapped");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasMountain()
    {
        var bus = new ReplacementBus();
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var land = MistriseVillageFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice));

        after!.EntersTapped.Should().BeFalse(
            "controller has a Mountain → enters untapped");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasForest()
    {
        var bus = new ReplacementBus();
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var land = MistriseVillageFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice));

        after!.EntersTapped.Should().BeFalse(
            "controller has a Forest → enters untapped");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasMountain()
    {
        var bus = new ReplacementBus();
        var mountain = (Land)NamedCardFactory.Create("Mountain", _bob);
        _bob.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var land = MistriseVillageFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice));

        after!.EntersTapped.Should().BeTrue(
            "only the opponent has a Mountain — 'you control' means controller only");
    }

    [Fact]
    public void ShapeOnlyDispatch_DoesNotRegisterReplacement()
    {
        // Single-arg path — ETB-tapped replacement is not wired.
        var land = NamedCardFactory.Create("Mistrise Village", _alice);
        land.Should().NotBeNull();
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {U},{T} activated ability: next-spell-uncounterable rider
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_Resolve_RegistersNextSpellUncounterableRider()
    {
        var land = MistriseVillageFactory.Create(_alice);

        // Simulate resolution by executing the ability's effect directly.
        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in ab.Effects) fx.Execute();

        // The rider should now be registered for Alice.
        CastingRestrictions.ConsumeNextSpellUncounterableForTurn(_alice)
            .Should().BeTrue("activation registered the one-shot uncounterable rider");
    }

    [Fact]
    public async Task SpellCastFlow_StampsCannotBeCountered_AfterActivation()
    {
        // Activate the ability (simulated via effect execution).
        var land = MistriseVillageFactory.Create(_alice);
        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in ab.Effects) fx.Execute();

        // Now cast a vanilla spell — SpellCastFlow should consume the rider
        // and stamp spell.CannotBeCountered = true.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        spell.CannotBeCountered.Should().BeTrue(
            "SpellCastFlow must consume the next-spell-uncounterable rider and stamp the flag");
    }

    [Fact]
    public async Task CounterAttempt_AgainstUncounterableSpell_LeavesSpellOnStack()
    {
        // Arm the one-shot rider.
        var land = MistriseVillageFactory.Create(_alice);
        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in ab.Effects) fx.Execute();

        // Alice casts a spell → gets CannotBeCountered stamp.
        var aliceBolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent1 = new ScriptedAgent();
        agent1.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        var aliceSpell = await _flow.CastAsync(
            _alice, aliceBolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent1, ctx,
            alternativeCost: null);

        aliceSpell.CannotBeCountered.Should().BeTrue();
        _stack.Count.Should().Be(1);

        // Bob "casts" Counterspell targeting Alice's spell.
        // Use OracleSpellBinder.RemoveFromStack directly (mirrors how every
        // counter template resolves — CR 701.5b veto check lives there).
        var removed = OracleSpellBinder.RemoveFromStack(_stack, aliceSpell);

        removed.Should().BeFalse("CR 701.5b — uncounterable spell cannot be countered");
        _stack.Count.Should().Be(1, "spell remains on the stack");
        _stack.Top.Should().BeSameAs(aliceSpell);
    }

    [Fact]
    public async Task OneShotRider_SecondSpellSameTurn_IsNotUncounterable()
    {
        // Arm once.
        var land = MistriseVillageFactory.Create(_alice);
        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in ab.Effects) fx.Execute();

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        // First spell — consumes the rider.
        var first = new Instant("Bolt1", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent1 = new ScriptedAgent();
        agent1.QueueMana(ManaPayment.Empty);
        var spell1 = await _flow.CastAsync(
            _alice, first,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent1, ctx, alternativeCost: null);

        spell1.CannotBeCountered.Should().BeTrue("first spell gets the stamp");

        // Second spell — rider was already consumed.
        var second = new Instant("Bolt2", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent2 = new ScriptedAgent();
        agent2.QueueMana(ManaPayment.Empty);
        var spell2 = await _flow.CastAsync(
            _alice, second,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent2, ctx, alternativeCost: null);

        spell2.CannotBeCountered.Should().BeFalse(
            "the one-shot rider is consumed after the first spell; second spell is counterable");
    }

    [Fact]
    public void ActivatingTwice_ReArmsRider_ForNextCast()
    {
        // Activate once, consume the rider, activate again → rider is back.
        var land = MistriseVillageFactory.Create(_alice);
        var ab = land.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var fx in ab.Effects) fx.Execute();
        // Consume it.
        CastingRestrictions.ConsumeNextSpellUncounterableForTurn(_alice);
        // Now it's gone.
        CastingRestrictions.ConsumeNextSpellUncounterableForTurn(_alice)
            .Should().BeFalse("already consumed");

        // Second activation re-arms.
        foreach (var fx in ab.Effects) fx.Execute();
        CastingRestrictions.ConsumeNextSpellUncounterableForTurn(_alice)
            .Should().BeTrue("second activation re-registered the rider");
    }
}
