using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Assassin's Trophy (Guilds of Ravnica, {B}{G}, Instant) and
/// Beast Within (New Phyrexia, {2}{G}, Instant).
///
/// Covers:
///   Assassin's Trophy —
///     - Card identity (Instant, {B}{G}, owner/controller).
///     - NamedCardFactory dispatch.
///     - Destroys opponent's permanent; that player gets a basic land
///       untapped onto the battlefield.
///     - Resolve-time opponent check: if the target is controlled by the
///       caster, the spell does nothing (CR 608.2b).
///
///   Beast Within —
///     - Card identity (Instant, {2}{G}, owner/controller).
///     - NamedCardFactory dispatch.
///     - Destroys target permanent; its controller gets a 3/3 Beast token.
///     - Own permanent is a legal target (any permanent, not
///       "opponent controls").
/// </summary>
public class AssassinsTrophyTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public AssassinsTrophyTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow  = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Assassin's Trophy — card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AssassinsTrophy_IsInstant_AtCostBG()
    {
        var card = AssassinsTrophyFactory.Create(_alice);

        card.Name.Should().Be("Assassin's Trophy");
        card.ManaCost.Should().Be("{B}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AssassinsTrophy()
    {
        var card = NamedCardFactory.Create("Assassin's Trophy", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Assassin's Trophy");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Assassin's Trophy — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AssassinsTrophy_DestroysOpponentPermanent_OpponentGetsBasicLand()
    {
        // Bob controls a Grizzly Bears on the battlefield.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _bob);

        // Bob has a Mountain in his library.
        var mountain = NamedCardFactory.Create("Mountain", _bob);
        mountain.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(mountain);

        // Alice casts Assassin's Trophy targeting Bob's bears.
        await CastAndResolve_AssassinsTrophy(bears);

        // Bears destroyed (moved to Bob's graveyard).
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);

        // Bob's Mountain tutored onto the battlefield untapped.
        mountain.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(mountain);
        _bob.Zones.Library.GetCards().Should().NotContain(mountain);
        mountain.Controller.Should().BeSameAs(_bob);
        // Land should NOT be tapped — Assassin's Trophy says "puts it onto
        // the battlefield" with no tapped qualifier.
        ((Permanent)mountain).IsTapped.Should().BeFalse();
    }

    [Fact]
    public async Task AssassinsTrophy_NoOpWhenTargetControlledByCaster()
    {
        // Alice controls the creature herself — the "opponent controls"
        // check at resolution should cause the whole spell to fizzle.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _alice);

        await CastAndResolve_AssassinsTrophy(bears);

        // Creature should remain on the battlefield — spell was a no-op.
        bears.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(bears);
    }

    // -----------------------------------------------------------------------
    // Beast Within — card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BeastWithin_IsInstant_AtCost2G()
    {
        var card = BeastWithinFactory.Create(_alice);

        card.Name.Should().Be("Beast Within");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BeastWithin()
    {
        var card = NamedCardFactory.Create("Beast Within", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Beast Within");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Beast Within — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BeastWithin_DestroysPermanent_ItsControllerGets3_3BeastToken()
    {
        // Bob controls a Grizzly Bears.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _bob);

        await CastAndResolve_BeastWithin(bears);

        // Bears destroyed.
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);

        // Bob receives a 3/3 Beast token.
        var token = _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.IsToken && c.HasSubtype(CardSubtype.Beast));
        token.Should().NotBeNull("Bob should have a Beast token");
        token!.Power.Should().Be(3);
        token.Toughness.Should().Be(3);
        token.Owner.Should().BeSameAs(_bob);
        token.Controller.Should().BeSameAs(_bob);
    }

    [Fact]
    public async Task BeastWithin_OwnPermanentIsLegalTarget()
    {
        // Alice targets her own creature — Beast Within says "target
        // permanent" with no "opponent controls" restriction.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _alice);

        await CastAndResolve_BeastWithin(bears);

        // Alice's bears destroyed.
        bears.Zone.Should().Be(ZoneType.Graveyard);

        // Alice receives a 3/3 Beast token.
        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.IsToken && c.HasSubtype(CardSubtype.Beast));
        token.Should().NotBeNull("Alice should have a Beast token");
        token!.Power.Should().Be(3);
        token.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature MakeCreature(string name, string cost, int power, int toughness, Player controller)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    private async Task CastAndResolve_AssassinsTrophy(object target)
    {
        var card = AssassinsTrophyFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            AssassinsTrophyFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        card.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();
    }

    private async Task CastAndResolve_BeastWithin(object target)
    {
        var card = BeastWithinFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            BeastWithinFactory.BuildSpellDefinition(t => t),
            agent, ctx);

        card.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();
    }
}
