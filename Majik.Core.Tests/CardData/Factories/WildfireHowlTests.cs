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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Wildfire Howl (Tarkir: Dragonstorm, {1}{R}{R}).
///
/// Printed oracle (verified against Scryfall 2026-06-24):
///   "Gift a card (You may promise an opponent a gift as you cast this spell.
///    If you do, they draw a card before its other effects.)
///    Wildfire Howl deals 2 damage to each creature. If the gift was promised,
///    instead Wildfire Howl deals 1 damage to any target and 2 damage to each
///    creature."
///
/// Unique behaviour under test (the gift-conditional sweep + any-target rider):
///   * No gift → base mode: untargeted; deals 2 to EVERY creature (both
///     players), no any-target damage.
///   * Gift promised → upgraded mode: the recipient draws a card at cast time
///     (the "Gift a card" clause, CR 701.59), AND the spell deals 1 damage to
///     the chosen any-target (here, the opponent's face — CR 115.3) PLUS the
///     2-to-each-creature sweep.
///   * Identity assert (non-vanilla mana cost): Sorcery, {1}{R}{R}, red, mv 3.
/// </summary>
[Trait("Color", "R")]
public class WildfireHowlTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public WildfireHowlTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasSorceryShape_Red_ThreeManaValue()
    {
        var card = WildfireHowlFactory.Create(_alice);

        card.Name.Should().Be("Wildfire Howl");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCost.Should().Be("{1}{R}{R}");
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{R}{R} = mana value 3");
    }

    [Fact]
    public async Task BaseMode_NoGift_NoTarget_SweepsEachCreatureForTwo()
    {
        var card = WildfireHowlFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Both players control creatures — "each creature" reaches all of them.
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobGiant = NewCreatureOnBattlefield(_bob, "Hill Giant", "{3}{R}", 3, 3);

        var agent = new ScriptedAgent();
        // No gift promised — ScriptedAgent declines the optional gift, so the
        // 0..1 "any target" request gathers no candidates. The optional target
        // request still prompts (MinTargets 0), and the agent returns an empty
        // pick — no target is collected (CR 601.2c — base Wildfire Howl is
        // untargeted).
        agent.QueueTargets(System.Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            WildfireHowlFactory.BuildDefinition(_alice, card),
            agent, ctx,
            alternativeCost: null);

        spell.GiftRecipient.Should().BeNull();
        card.HasGiftPromised.Should().BeFalse();

        _resolver.ResolveTop(_stack);

        // CR 109.5 — 2 damage to each creature, regardless of controller.
        aliceBear.Damage.Should().Be(2);
        bobGiant.Damage.Should().Be(2);
        aliceBear.IsDead().Should().BeTrue("2 on a 2/2 is lethal");
        bobGiant.IsDead().Should().BeFalse("2 on a 3/3 is survivable");

        // No life loss to either player in base mode (untargeted).
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task GiftPromised_DrawsCard_DealsOneToAnyTarget_AndSweeps()
    {
        var card = WildfireHowlFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Give Bob a library card so the gift draw has something to pull.
        var libCard = new Instant("Opt", "{U}") { Owner = _bob, Controller = _bob };
        libCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libCard);
        var bobHandBefore = _bob.Zones.Hand.GetCards().Count();

        var bobGiant = NewCreatureOnBattlefield(_bob, "Hill Giant", "{3}{R}", 3, 3);

        var agent = new ScriptedAgent();
        agent.QueueGiftRecipient(_bob);
        // Gift mode upgrades the spell to also deal 1 to any target — aim at
        // Bob's face (CR 115.3 — a player is a legal "any target").
        agent.QueueTargets(new[] { (object)_bob });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            WildfireHowlFactory.BuildDefinition(_alice, card),
            agent, ctx,
            alternativeCost: null);

        spell.GiftRecipient.Should().BeSameAs(_bob);
        card.HasGiftPromised.Should().BeTrue();

        // CR 701.59 — "they draw a card" at cast time (engine v1 cast-time gift
        // delivery). Bob drew the gift card before resolution.
        _bob.Zones.Hand.GetCards().Count().Should().Be(bobHandBefore + 1);
        _bob.Zones.Hand.GetCards().Should().Contain(libCard);

        _resolver.ResolveTop(_stack);

        // Gift mode: 1 damage to the chosen any-target (Bob's face) ...
        _bob.LifeTotal.Should().Be(19, because: "gift mode deals 1 damage to any target (CR 115.3)");
        // ... PLUS the 2-to-each-creature sweep.
        bobGiant.Damage.Should().Be(2, because: "the sweep still fires in gift mode");
    }

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
