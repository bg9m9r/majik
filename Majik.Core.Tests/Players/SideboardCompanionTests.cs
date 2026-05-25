using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.Aggregates;
using Majik.Core.Effects;
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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Players;

/// <summary>
/// CR 100.4 / CR 702.139 — Sideboard zone + Companion runtime
/// cast-from-outside-the-game pipeline.
///
/// Covers:
/// <list type="bullet">
///   <item>Sideboard zone exists per player and can hold cards.</item>
///   <item>Companion {3} tax + sideboard → hand move via
///         <see cref="SpellCastFlow.CastCompanionAsync"/>.</item>
///   <item>Second attempt rejected (once-per-game ledger).</item>
///   <item>Sorcery-speed restriction (own main phase, empty stack).</item>
///   <item>Lurrus end-to-end: register companion + cast-from-outside
///         + cast normally → resolves to battlefield as Lifelink 3/2.</item>
/// </list>
/// </summary>
public class SideboardCompanionTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SideboardCompanionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    private GameContext MainPhaseCtx(Player active) =>
        new(active, new[] { _alice, _bob }, active, 1,
            PhaseStateType.Main, _stack);

    // ── Zone shape ─────────────────────────────────────────────────────

    [Fact]
    public void Sideboard_ZoneExists_PerPlayer_AndCanHoldCards()
    {
        _alice.Sideboard.Should().NotBeNull();
        _alice.Sideboard.Type.Should().Be(ZoneType.Sideboard);
        _alice.Sideboard.Count.Should().Be(0);

        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);
        lurrus.SetZone(ZoneType.Sideboard);
        _alice.Zones.Sideboard.AddCard(lurrus);

        _alice.Sideboard.Count.Should().Be(1);
        _alice.Sideboard.ContainsCard(lurrus).Should().BeTrue();

        // Bob's sideboard is independent.
        _bob.Sideboard.Count.Should().Be(0);
    }

    [Fact]
    public void Game_RegisterCompanion_PlacesCardInOwnersSideboard()
    {
        var game = new Majik.Core.Domain.Aggregates.Game(_bus);
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        var alice = game.GetPlayer("Alice")!;
        var bob = game.GetPlayer("Bob")!;

        var lurrus = LurrusOfTheDreamDenFactory.Create(alice);
        game.RegisterCompanion(alice, lurrus);

        lurrus.Owner.Should().Be(alice);
        lurrus.Controller.Should().Be(alice);
        lurrus.Zone.Should().Be(ZoneType.Sideboard);
        alice.Sideboard.ContainsCard(lurrus).Should().BeTrue();
        bob.Sideboard.ContainsCard(lurrus).Should().BeFalse();

        // Idempotent — second call with the same card is a no-op.
        game.RegisterCompanion(alice, lurrus);
        alice.Sideboard.Count.Should().Be(1);
    }

    // ── Runtime cast-from-outside ──────────────────────────────────────

    [Fact]
    public async Task CastCompanion_PaysThreeTaxAndMovesCardToHand()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);
        lurrus.SetZone(ZoneType.Sideboard);
        _alice.Zones.Sideboard.AddCard(lurrus);

        // Pre-pay {3} into Alice's pool.
        _alice.AddManaToPool(ManaCost.Parse("{3}"));
        _alice.ManaPool.Total.Should().Be(3);
        _alice.CompanionUsedThisGame.Should().BeFalse();

        await _flow.CastCompanionAsync(_alice, lurrus, MainPhaseCtx(_alice));

        // {3} consumed, card moved sideboard → hand, ledger latched.
        _alice.ManaPool.Total.Should().Be(0);
        lurrus.Zone.Should().Be(ZoneType.Hand);
        _alice.Sideboard.ContainsCard(lurrus).Should().BeFalse();
        _alice.Zones.Hand.ContainsCard(lurrus).Should().BeTrue();
        _alice.CompanionUsedThisGame.Should().BeTrue();
    }

    [Fact]
    public async Task CastCompanion_SecondAttemptSameGame_Rejected()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);
        lurrus.SetZone(ZoneType.Sideboard);
        _alice.Zones.Sideboard.AddCard(lurrus);
        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        await _flow.CastCompanionAsync(_alice, lurrus, MainPhaseCtx(_alice));
        _alice.CompanionUsedThisGame.Should().BeTrue();

        // Even after re-registering another would-be companion in the
        // sideboard, the once-per-game ledger blocks a second cast.
        var second = LurrusOfTheDreamDenFactory.Create(_alice);
        second.SetZone(ZoneType.Sideboard);
        _alice.Zones.Sideboard.AddCard(second);
        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        Func<Task> act = () => _flow.CastCompanionAsync(_alice, second, MainPhaseCtx(_alice));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*once-per-game*");

        // Second card stayed in the sideboard; {3} still in the pool.
        second.Zone.Should().Be(ZoneType.Sideboard);
        _alice.ManaPool.Total.Should().Be(3);
    }

    [Fact]
    public async Task CastCompanion_SorcerySpeedEnforced_NotMainPhaseRejected()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);
        lurrus.SetZone(ZoneType.Sideboard);
        _alice.Zones.Sideboard.AddCard(lurrus);
        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        // Wrong phase — declare attackers is not main.
        var combatCtx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.DeclareAttackers, _stack);

        Func<Task> act = () => _flow.CastCompanionAsync(_alice, lurrus, combatCtx);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sorcery-speed*");

        // Pool + zone untouched.
        _alice.ManaPool.Total.Should().Be(3);
        lurrus.Zone.Should().Be(ZoneType.Sideboard);
        _alice.CompanionUsedThisGame.Should().BeFalse();
    }

    [Fact]
    public async Task CastCompanion_OnOpponentsTurn_Rejected()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);
        lurrus.SetZone(ZoneType.Sideboard);
        _alice.Zones.Sideboard.AddCard(lurrus);
        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        // Bob is the active player; Alice trying to invoke companion.
        var bobsCtx = new GameContext(_alice, new[] { _alice, _bob },
            _bob, 1, PhaseStateType.Main, _stack);

        Func<Task> act = () => _flow.CastCompanionAsync(_alice, lurrus, bobsCtx);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sorcery-speed*");
    }

    [Fact]
    public async Task CastCompanion_CardNotInSideboard_Rejected()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);
        // Placed in hand directly — wrong starting zone.
        lurrus.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(lurrus);
        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        Func<Task> act = () =>
            _flow.CastCompanionAsync(_alice, lurrus, MainPhaseCtx(_alice));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Sideboard*");
        _alice.CompanionUsedThisGame.Should().BeFalse();
    }

    [Fact]
    public async Task CastCompanion_CannotPayThreeTax_Rejected()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);
        lurrus.SetZone(ZoneType.Sideboard);
        _alice.Zones.Sideboard.AddCard(lurrus);
        // Only two mana — insufficient for {3}.
        _alice.AddManaToPool(ManaCost.Parse("{2}"));

        Func<Task> act = () =>
            _flow.CastCompanionAsync(_alice, lurrus, MainPhaseCtx(_alice));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*{3}*");

        // Pool untouched (atomic).
        _alice.ManaPool.Total.Should().Be(2);
        lurrus.Zone.Should().Be(ZoneType.Sideboard);
        _alice.CompanionUsedThisGame.Should().BeFalse();
    }

    // ── Lurrus end-to-end ──────────────────────────────────────────────

    [Fact]
    public async Task Lurrus_FullPipeline_RegisterCastFromOutsideAndCastNormally()
    {
        // Wire up a full Game so we exercise RegisterCompanion + the
        // ledger together with the spell-cast flow.
        var game = new Majik.Core.Domain.Aggregates.Game(_bus);
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        var alice = game.GetPlayer("Alice")!;

        var lurrus = LurrusOfTheDreamDenFactory.Create(alice, _bus);
        game.RegisterCompanion(alice, lurrus);

        lurrus.Zone.Should().Be(ZoneType.Sideboard);
        alice.Sideboard.ContainsCard(lurrus).Should().BeTrue();

        // Pay the {3} tax + printed {W}{B} for the cast (total {3}{W}{B}).
        alice.AddManaToPool(ManaCost.Parse("{3}{W}{B}"));

        var ctx = new GameContext(alice, new[] { alice, game.GetPlayer("Bob")! },
            alice, 1, PhaseStateType.Main, _stack);

        // Step 1 — Companion tax + sideboard → hand.
        await _flow.CastCompanionAsync(alice, lurrus, ctx);
        lurrus.Zone.Should().Be(ZoneType.Hand);
        alice.CompanionUsedThisGame.Should().BeTrue();
        alice.ManaPool.Total.Should().Be(2); // {W}{B} left

        // Step 2 — cast normally with printed {W}{B}.
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        // Build a battlefield-landing SpellDefinition. Lurrus's vanilla
        // resolve effect is "permanent enters the battlefield" which is
        // the StackResolver's default for permanent spells — supplying an
        // empty effect list lets the resolver route the card to its
        // printed-type default destination (battlefield) without us
        // re-deriving Lurrus's body here.
        var spell = await _flow.CastAsync(
            alice, lurrus,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        lurrus.Zone.Should().Be(ZoneType.Stack);

        // Resolve via the StackResolver so the permanent lands on the
        // battlefield with its printed P/T + Lifelink intact.
        _resolver.ResolveTop(_stack);

        lurrus.Zone.Should().Be(ZoneType.Battlefield);
        alice.Zones.Battlefield.ContainsCard(lurrus).Should().BeTrue();
        lurrus.BasePower.Should().Be(3);
        lurrus.BaseToughness.Should().Be(2);
        lurrus.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Lifelink");
    }
}
