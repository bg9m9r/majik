using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
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

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for <see cref="MiracleAlternativeCost"/> — CR 702.94 Miracle,
/// the alternative cost a card may be cast for from HAND while it carries a
/// runtime miracle grant (stamped when it is the first card a player drew
/// this turn, CR 702.94b). Mirrors <see cref="FlashbackAlternativeCostTests"/>
/// but the legal zone is the hand and the post-resolution side-effect is a
/// one-shot clear of the runtime grant (the miracle window does not survive
/// the cast).
/// </summary>
public class MiracleAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CanCastFor_InHand_OwnedBySelf_WithGrant_Yes()
    {
        var c = new Sorcery("Terminus", "{4}{W}{W}") { Owner = _alice, Zone = ZoneType.Hand };
        c.GrantRuntimeMiracle(ManaCost.Parse("{W}"));
        var miracle = new MiracleAlternativeCost(ManaCost.Parse("{W}"));

        miracle.CanCastFor(c, _alice).Should().BeTrue();
    }

    [Fact]
    public void CanCastFor_NoGrant_No()
    {
        var c = new Sorcery("Terminus", "{4}{W}{W}") { Owner = _alice, Zone = ZoneType.Hand };
        var miracle = new MiracleAlternativeCost(ManaCost.Parse("{W}"));

        miracle.CanCastFor(c, _alice).Should().BeFalse(
            "the miracle window is open only while the runtime grant is stamped (CR 702.94b)");
    }

    [Fact]
    public void CanCastFor_NotInHand_No()
    {
        var c = new Sorcery("Terminus", "{4}{W}{W}") { Owner = _alice, Zone = ZoneType.Graveyard };
        c.GrantRuntimeMiracle(ManaCost.Parse("{W}"));
        var miracle = new MiracleAlternativeCost(ManaCost.Parse("{W}"));

        miracle.CanCastFor(c, _alice).Should().BeFalse(
            "miracle is cast from the hand (CR 702.94a)");
    }

    [Fact]
    public void CanCastFor_OwnedByOther_No()
    {
        var c = new Sorcery("Terminus", "{4}{W}{W}") { Owner = _bob, Zone = ZoneType.Hand };
        c.GrantRuntimeMiracle(ManaCost.Parse("{W}"));
        var miracle = new MiracleAlternativeCost(ManaCost.Parse("{W}"));

        miracle.CanCastFor(c, _alice).Should().BeFalse(
            "only the player who drew the card may use its miracle window");
    }

    [Fact]
    public void OnResolved_ClearsTheRuntimeMiracleGrant()
    {
        var c = new Sorcery("Terminus", "{4}{W}{W}") { Owner = _alice, Zone = ZoneType.Hand };
        c.GrantRuntimeMiracle(ManaCost.Parse("{W}"));
        var miracle = new MiracleAlternativeCost(ManaCost.Parse("{W}"));

        miracle.OnResolved(c, _alice);

        c.RuntimeMiracleCost.Should().BeNull(
            "the miracle window is one-shot — once cast (or the cast resolves) the grant clears");
    }

    [Fact]
    public void PostResolutionZone_IsNull_FollowsPrintedTypeDefault()
    {
        IAlternativeCost miracle = new MiracleAlternativeCost(ManaCost.Parse("{W}"));
        miracle.PostResolutionZone.Should().BeNull(
            "a sorcery cast for its miracle cost still goes to the graveyard (CR 608.2)");
    }

    [Fact]
    public async Task Terminus_CastFromHandViaMiracle_ResolvesSweep_LandsInGraveyard_WindowClosed()
    {
        // ── Arrange ──────────────────────────────────────────────────────────
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Terminus freshly drawn — in Alice's hand with its miracle window open
        // (the draw hook would have stamped this when it was her first draw).
        var terminus = TerminusFactory.Create(alice);
        terminus.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(terminus);
        terminus.GrantRuntimeMiracle(ManaCost.Parse(TerminusFactory.MiracleCostText));

        // A creature on the battlefield that the Terminus sweep will tuck.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(bob);
        grizzly.SetController(bob);
        grizzly.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(grizzly);

        var altCost = new MiracleAlternativeCost(ManaCost.Parse(TerminusFactory.MiracleCostText));

        // Terminus's actual resolve effect — tuck all creatures to the bottom
        // of their owners' libraries (CR 702.94 only changes the cost paid,
        // not the spell's effect).
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => TerminusFactory.BuildResolveEffect(new[] { alice, bob }));

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty); // {W} payment stubbed by the test harness

        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1,
            StepStateType.PreCombatMain, stack);

        // ── Act: cast for the miracle cost ───────────────────────────────────
        var spell = await flow.CastAsync(
            alice, terminus, def, agent, ctx,
            alternativeCost: altCost);

        terminus.Zone.Should().Be(ZoneType.Stack, "the spell is on the stack after casting");

        spell.Resolve();

        // ── Assert ───────────────────────────────────────────────────────────
        grizzly.Zone.Should().Be(ZoneType.Library,
            "Terminus tucks every creature to the bottom of its owner's library");
        bob.Zones.Library.GetCards().Should().Contain(grizzly);

        // CR 608.2 — MiracleAlternativeCost imposes NO post-resolution zone
        // override (PostResolutionZone is null), unlike Flashback's exile. The
        // spell follows the engine's default stack → graveyard disposition
        // (driven by the StackResolver in the full path); the alt-cost itself
        // does not move the card to exile.
        IAlternativeCost asInterface = altCost;
        asInterface.PostResolutionZone.Should().BeNull();
        terminus.Zone.Should().NotBe(ZoneType.Exile,
            "a sorcery cast for its miracle cost is not exiled (CR 608.2 default destination)");

        // CR 702.94 — the one-shot miracle window is closed after the cast
        // (MiracleAlternativeCost.OnResolved ran as the appended cleanup effect).
        terminus.RuntimeMiracleCost.Should().BeNull(
            "MiracleAlternativeCost.OnResolved clears the window");
    }
}
