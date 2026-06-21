using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 113.6 / 601.3 — from-zone cast-restriction enforcement on the LIVE
/// production cast path (<see cref="SpellCastFlow.CastAsync"/>).
///
/// The from-zone validator gates (Drannith Magistrate's cast-from-hand-only,
/// Grafdigger's Cage's global graveyard/library block, and a card-baked
/// <see cref="Card.RestrictedCastZones"/> Hogaak-style block) already existed
/// on <see cref="ActionValidator"/>, but nothing on the production cast
/// pipeline read the spell's origin zone and enforced them — so in a real
/// game the restriction no-opped on the from-zone axis (the
/// <c>cast-from-zone-provenance-stamping</c> deferral).
///
/// These tests drive <see cref="SpellCastFlow.CastAsync"/> directly — the
/// single entry every cast path funnels through (hand, flashback/jump-start
/// from graveyard, cascade/suspend/foretell from exile, Bolas top-of-library)
/// — and assert the gate fires off the spell's live origin zone.
///
/// They dispose-clean the static <see cref="CastingRestrictions"/> registry
/// to prevent cross-test leakage.
/// </summary>
public class SpellCastFlowFromZoneGateTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowFromZoneGateTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        CastingRestrictions.Clear();
    }

    public void Dispose() => CastingRestrictions.Clear();

    // -----------------------------------------------------------------------
    // CR 601.3 — Grafdigger's Cage global cast-from-zone block
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GrafdiggersCage_BlocksFlashbackFromGraveyard_OnLiveCastPath()
    {
        // Grafdigger's Cage is on the battlefield: "Players can't cast spells
        // from graveyards or libraries." Bob tries to flash back a spell from
        // his graveyard via the live cast path — it must be rejected.
        CastingRestrictions.AddGlobalCastZoneBlock(new object(), ZoneType.Graveyard);
        CastingRestrictions.AddGlobalCastZoneBlock(new object(), ZoneType.Library);

        var looting = new Sorcery("Faithless Looting", "R") { Owner = _bob, Zone = ZoneType.Graveyard };
        _bob.Zones.Graveyard.AddCard(looting);
        var ctx = new GameContext(_bob, new[] { _alice, _bob }, _bob, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var act = async () => await _flow.CastAsync(_bob, looting,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*Graveyard*");
        _stack.Count.Should().Be(0);
        looting.Zone.Should().Be(ZoneType.Graveyard, "the rejected cast leaves the card in its graveyard");
    }

    [Fact]
    public async Task GrafdiggersCage_AllowsHandCast_OnLiveCastPath()
    {
        // The block is on graveyards/libraries only — a hand cast is unaffected.
        CastingRestrictions.AddGlobalCastZoneBlock(new object(), ZoneType.Graveyard);
        CastingRestrictions.AddGlobalCastZoneBlock(new object(), ZoneType.Library);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(bolt);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        _stack.Count.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // CR 113.6 — Drannith Magistrate cast-from-hand-only player restriction
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DrannithMagistrate_BlocksOpponentCastFromExile_OnLiveCastPath()
    {
        // Drannith Magistrate restricts each opponent to casting only from hand.
        // Bob (the restricted opponent) tries to cast from exile (cascade /
        // suspend / foretell) via the live cast path — rejected.
        CastingRestrictions.AddCastFromHandOnlyRestriction(new object(), _bob);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob, Zone = ZoneType.Exile };
        _bob.Zones.Exile.AddCard(bolt);
        var ctx = new GameContext(_bob, new[] { _alice, _bob }, _bob, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var act = async () => await _flow.CastAsync(_bob, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*Exile*");
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task DrannithMagistrate_DoesNotBlockHandCast_OnLiveCastPath()
    {
        // The whole point: a restricted opponent's HAND cast still resolves.
        CastingRestrictions.AddCastFromHandOnlyRestriction(new object(), _bob);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bolt);
        var ctx = new GameContext(_bob, new[] { _alice, _bob }, _bob, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(_bob, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        _stack.Count.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // CR 601.2a / 117.6 — card-baked RestrictedCastZones (Hogaak-style)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CardBakedRestrictedCastZone_BlocksHandCast_OnLiveCastPath()
    {
        // Hogaak — "You can't spend mana to cast this spell." is modelled via a
        // RestrictedCastZones marker; a card that bakes a Hand block must reject
        // a hand cast on the live path. (Generic exercise of the card-baked axis.)
        var hogaak = new Creature("Hogaak, Arisen Necropolis", "{0}", 8, 8) { Owner = _alice, Zone = ZoneType.Hand };
        hogaak.AddRestrictedCastZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(hogaak);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var act = async () => await _flow.CastAsync(_alice, hogaak,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*can't be cast from Hand*");
        _stack.Count.Should().Be(0);
    }
}
