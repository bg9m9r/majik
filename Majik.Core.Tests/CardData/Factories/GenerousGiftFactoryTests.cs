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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Generous Gift (Modern Horizons, {2}{W}, Instant).
///
/// Oracle text: "Destroy target permanent. Its controller creates a 3/3
/// green Elephant creature token."
///
/// Generous Gift is the white analogue of Beast Within — same
/// "destroy any permanent, that permanent's controller gets a 3/3 green
/// vanilla token" template (Elephant instead of Beast).
///
/// Covers:
///   - Card identity (Instant, {2}{W}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Destroys target permanent; its controller gets a 3/3 green
///     Elephant token.
///   - Own permanent is a legal target (any permanent, not
///     "opponent controls").
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class GenerousGiftFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public GenerousGiftFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow  = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousGift_IsInstant_AtCost2W()
    {
        var card = GenerousGiftFactory.Create(_alice);

        card.Name.Should().Be("Generous Gift");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerousGift_DestroysPermanent_ItsControllerGets3_3GreenElephantToken()
    {
        // Bob controls a Grizzly Bears.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _bob);

        await CastAndResolve_GenerousGift(bears);

        // Bears destroyed.
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);

        // Bob receives a 3/3 green Elephant token.
        var token = _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.IsToken && c.HasSubtype(CardSubtype.Elephant));
        token.Should().NotBeNull("Bob should have an Elephant token");
        token!.Power.Should().Be(3);
        token.Toughness.Should().Be(3);
        token.Owner.Should().BeSameAs(_bob);
        token.Controller.Should().BeSameAs(_bob);
        CardColors.GetColors(token).Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public async Task GenerousGift_OwnPermanentIsLegalTarget()
    {
        // Alice targets her own creature — Generous Gift says "target
        // permanent" with no "opponent controls" restriction.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _alice);

        await CastAndResolve_GenerousGift(bears);

        // Alice's bears destroyed.
        bears.Zone.Should().Be(ZoneType.Graveyard);

        // Alice receives a 3/3 Elephant token.
        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.IsToken && c.HasSubtype(CardSubtype.Elephant));
        token.Should().NotBeNull("Alice should have an Elephant token");
        token!.Power.Should().Be(3);
        token.Toughness.Should().Be(3);
    }

    [Fact]
    public async Task GenerousGift_OffBattlefieldTarget_NoOp()
    {
        // CR 608.2b — target illegal at resolution → spell does nothing.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _bob);
        // Move it off the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(bears);
        bears.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bears);

        await CastAndResolve_GenerousGift(bears);

        // No Elephant token created for anyone.
        _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.IsToken && c.HasSubtype(CardSubtype.Elephant))
            .Should().BeFalse();
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.IsToken && c.HasSubtype(CardSubtype.Elephant))
            .Should().BeFalse();
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

    private async Task CastAndResolve_GenerousGift(object target)
    {
        var card = GenerousGiftFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            GenerousGiftFactory.BuildSpellDefinition(t => t),
            agent, ctx);

        card.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();
    }
}
