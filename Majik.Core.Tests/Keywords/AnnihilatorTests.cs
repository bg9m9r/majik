using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules.Sba;
using Majik.Core.Rules.Sba.Checks;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Unit tests for <see cref="AnnihilatorFactory"/>.
///
/// CR 702.86 — "Annihilator N" means "Whenever this creature attacks,
/// defending player sacrifices N permanents." CR 508.1f — the attack
/// declaration is what fires the trigger. CR 702.86b — multiple
/// instances trigger separately.
///
/// Covers:
///   - Self-attack fires; non-self attack doesn't fire (condition
///     correctness — the per-attacker filter).
///   - Resolution sacrifices N permanents controlled by the defender,
///     consulting <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>
///     when an agent selector is supplied.
///   - Short battlefield (defender has fewer than N permanents) →
///     sacrifices all of them, no exception.
///   - Multiple Annihilator creatures attacking → each fires
///     independently (CR 702.86b).
///   - Token sacrificed via the trigger → reaches the graveyard via
///     <see cref="Primitives.Fx.Sacrifice"/>, then CR 110.7 / 704.5d
///     SBA removes it from the graveyard.
/// </summary>
public class AnnihilatorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Bear(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature MakeAnnihilator(string name, Player owner, int n, int p = 5, int t = 5)
    {
        var c = new Creature(name, "{8}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Self-attack condition
    // -----------------------------------------------------------------------

    [Fact]
    public void Annihilator_FiresOnSelfAttack()
    {
        var attacker = MakeAnnihilator("Crusher", _alice, 2);
        var trigger = AnnihilatorFactory.Build(attacker, n: 2);

        var ev = new CreatureAttacksEvent(attacker, _bob);
        trigger.Condition.Matches(ev, trigger).Should().BeTrue();
    }

    [Fact]
    public void Annihilator_DoesNotFireOnOtherAttacker()
    {
        var attacker = MakeAnnihilator("Crusher", _alice, 2);
        var bystander = Bear("Bear", _alice);
        var trigger = AnnihilatorFactory.Build(attacker, n: 2);

        var ev = new CreatureAttacksEvent(bystander, _bob);
        trigger.Condition.Matches(ev, trigger).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolution — defender sacrifices N permanents
    // -----------------------------------------------------------------------

    [Fact]
    public void Annihilator_DefenderSacrificesN_AgentPicks()
    {
        var attacker = MakeAnnihilator("Crusher", _alice, 2);

        // Bob controls four permanents; agent picks the last two by name.
        var b1 = Bear("Bear1", _bob);
        var b2 = Bear("Bear2", _bob);
        var b3 = Bear("Bear3", _bob);
        var b4 = Bear("Bear4", _bob);

        // Scripted agent that prefers the *last* candidate each call —
        // proves the agent's picks drive the sacrifice rather than the
        // deterministic first-candidate fallback.
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueFromBattlefield(cands => cands[cands.Count - 1]);
        bobAgent.QueueFromBattlefield(cands => cands[cands.Count - 1]);

        var trigger = AnnihilatorFactory.Build(
            attacker, n: 2, agentSelector: p => p == _bob ? bobAgent : null);

        trigger.Condition.Matches(new CreatureAttacksEvent(attacker, _bob), trigger)
            .Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        // Agent picked b4 (turn 1), then b3 (turn 2) — both moved to
        // graveyard via Fx.Sacrifice. b1, b2 remain.
        b4.Zone.Should().Be(ZoneType.Graveyard);
        b3.Zone.Should().Be(ZoneType.Graveyard);
        b1.Zone.Should().Be(ZoneType.Battlefield);
        b2.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().HaveCount(2);
        _bob.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { b3, b4 });
    }

    [Fact]
    public void Annihilator_DeterministicFallback_PicksFirstN_WhenNoAgent()
    {
        var attacker = MakeAnnihilator("Crusher", _alice, 2);

        var b1 = Bear("Bear1", _bob);
        var b2 = Bear("Bear2", _bob);
        var b3 = Bear("Bear3", _bob);

        // No agent selector — should fall through to first-N picks
        // (legacy pre-agent posture).
        var trigger = AnnihilatorFactory.Build(attacker, n: 2);

        trigger.Condition.Matches(new CreatureAttacksEvent(attacker, _bob), trigger)
            .Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        b1.Zone.Should().Be(ZoneType.Graveyard);
        b2.Zone.Should().Be(ZoneType.Graveyard);
        b3.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Short battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Annihilator_FewerPermanentsThanN_SacrificesAll()
    {
        var attacker = MakeAnnihilator("Crusher", _alice, 6);

        var b1 = Bear("Bear1", _bob);
        var b2 = Bear("Bear2", _bob);

        // Annihilator 6 but Bob only has 2 permanents — sacrifices both
        // and stops cleanly (no exception, no underflow).
        var trigger = AnnihilatorFactory.Build(attacker, n: 6);
        trigger.Condition.Matches(new CreatureAttacksEvent(attacker, _bob), trigger)
            .Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        b1.Zone.Should().Be(ZoneType.Graveyard);
        b2.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Annihilator_NoPermanents_NoOp()
    {
        var attacker = MakeAnnihilator("Crusher", _alice, 3);

        var trigger = AnnihilatorFactory.Build(attacker, n: 3);
        trigger.Condition.Matches(new CreatureAttacksEvent(attacker, _bob), trigger)
            .Should().BeTrue();

        // Defender has no permanents — effect resolves to a no-op.
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Multiple Annihilator creatures attacking — each fires independently
    // (CR 702.86b).
    // -----------------------------------------------------------------------

    [Fact]
    public void Annihilator_MultipleAttackers_FireIndependently()
    {
        var attacker1 = MakeAnnihilator("Crusher", _alice, 1);
        var attacker2 = MakeAnnihilator("Pathrazer", _alice, 1);

        // Bob has 4 permanents.
        var b1 = Bear("Bear1", _bob);
        var b2 = Bear("Bear2", _bob);
        var b3 = Bear("Bear3", _bob);
        var b4 = Bear("Bear4", _bob);

        var trig1 = AnnihilatorFactory.Build(attacker1, n: 1);
        var trig2 = AnnihilatorFactory.Build(attacker2, n: 1);

        // Each attacker's condition fires only on its own attack event.
        trig1.Condition.Matches(new CreatureAttacksEvent(attacker1, _bob), trig1)
            .Should().BeTrue();
        trig1.Condition.Matches(new CreatureAttacksEvent(attacker2, _bob), trig1)
            .Should().BeFalse();
        trig2.Condition.Matches(new CreatureAttacksEvent(attacker2, _bob), trig2)
            .Should().BeTrue();

        foreach (var e in trig1.Effects) e.Execute();
        foreach (var e in trig2.Effects) e.Execute();

        // Two sacrifices total — one per attacker. Deterministic
        // fallback picks the first permanent each time; after the
        // first sacrifice the next "first permanent" shifts.
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
        _bob.Zones.Battlefield.GetCards().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Token sacrificed — CR 110.7 / 704.5d cease-to-exist
    // -----------------------------------------------------------------------

    [Fact]
    public void Annihilator_TokenSacrificed_CeasesToExist_AfterSba()
    {
        var attacker = MakeAnnihilator("Crusher", _alice, 1);

        // Bob controls one token. Use the canonical TokenFactory shape
        // so IsToken is set (CR 111.x). The Eldrazi Spawn token is a
        // convenient existing factory.
        var token = TokenFactory.CreateEldraziSpawn(_bob);
        // The token factory parks it on Bob's battlefield with IsToken=true.
        token.IsToken.Should().BeTrue();
        token.Zone.Should().Be(ZoneType.Battlefield);

        var trigger = AnnihilatorFactory.Build(attacker, n: 1);
        trigger.Condition.Matches(new CreatureAttacksEvent(attacker, _bob), trigger)
            .Should().BeTrue();
        foreach (var e in trigger.Effects) e.Execute();

        // Step 1: Fx.Sacrifice moved the token to Bob's graveyard.
        token.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(token);

        // Step 2: CR 110.7 / 704.5d SBA — the token ceases to exist.
        var sba = new TokensCeaseToExistCheck();
        var ctx = new SbaContext(
            players: new[] { _alice, _bob },
            cards: new ICard[] { attacker, token },
            eventBus: null,
            zoneService: null,
            triggerManager: null,
            replacements: null);
        var anyExecuted = sba.Execute(ctx);
        anyExecuted.Should().BeTrue();

        _bob.Zones.Graveyard.GetCards().Should().NotContain(token);
    }
}
