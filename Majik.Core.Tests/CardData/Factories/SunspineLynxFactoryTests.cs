using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SunspineLynxFactory"/> (Outlaws of Thunder
/// Junction, {2}{R}{R}).
///
/// Creature — Elemental Cat 5/4. Oracle text (verified against Scryfall):
///   "Players can't gain life.
///    Damage can't be prevented.
///    When this creature enters, it deals damage to each player equal to the
///    number of nonbasic lands that player controls."
///
/// Covers:
///   - Identity / shape ({2}{R}{R}, 5/4, Elemental + Cat).
///   - "Players can't gain life" replacement zeros every GainLife while the
///     bus is attached (CR 119.6 / 614).
///   - The ETB deals damage to each player = their nonbasic land count
///     (CR 603.6a) — basics excluded, asymmetric per player, zero-count
///     players untouched.
/// </summary>
[Trait("Color", "R")]
public class SunspineLynxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext LiveContext(Majik.Core.Stack.Stack stack) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, stack);

    private static Land Basic(string name, Player owner)
    {
        var subtype = name switch
        {
            "Mountain" => CardSubtype.Mountain,
            "Forest" => CardSubtype.Forest,
            _ => CardSubtype.Island,
        };
        var land = new Land(name, supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }

    private static Land Nonbasic(string name, Player owner)
    {
        var land = new Land(name);
        land.SetOwner(owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }

    // -------------------------------------------------------------------------
    // Identity / shape
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_HasCreatureShape_TwoRR_FiveFourElementalCat()
    {
        var lynx = SunspineLynxFactory.Create(_alice);

        lynx.Should().BeOfType<Creature>();
        lynx.Name.Should().Be("Sunspine Lynx");
        lynx.ManaCost.Should().Be("{2}{R}{R}");
        lynx.HasType(CardType.Creature).Should().BeTrue();
        lynx.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        lynx.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        lynx.BasePower.Should().Be(5);
        lynx.BaseToughness.Should().Be(4);
        lynx.Owner.Should().BeSameAs(_alice);
        lynx.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------------
    // "Players can't gain life" replacement
    // -------------------------------------------------------------------------

    [Fact]
    public void LifeGainReplacement_BlocksGainLifeOnEveryPlayer()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);
        _bob.AttachReplacementBus(bus);

        SunspineLynxFactory.Create(_alice, triggers: null, replacements: bus);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        _alice.GainLife(5);
        _bob.GainLife(7);

        _alice.LifeTotal.Should().Be(aliceLifeBefore, "gain rewritten to zero");
        _bob.LifeTotal.Should().Be(bobLifeBefore, "symmetric — Bob's gain zeros too");
    }

    [Fact]
    public void LifeGainReplacement_OmittedWhenNoBus_GainsNormally()
    {
        SunspineLynxFactory.Create(_alice);

        var aliceLifeBefore = _alice.LifeTotal;
        _alice.GainLife(5);

        _alice.LifeTotal.Should().Be(aliceLifeBefore + 5);
    }

    // -------------------------------------------------------------------------
    // ETB damage-to-each-player = nonbasic land count
    // -------------------------------------------------------------------------

    [Fact]
    public async System.Threading.Tasks.Task Etb_DealsDamageToEachPlayer_EqualToNonbasicLands()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice: 2 nonbasic + 1 basic = 2 damage. Bob: 1 nonbasic + 2 basics
        // = 1 damage. Basics never count (CR 205.4 — Basic supertype).
        Nonbasic("Steam Vents", _alice);
        Nonbasic("Sacred Foundry", _alice);
        Basic("Mountain", _alice);

        Nonbasic("Blood Crypt", _bob);
        Basic("Mountain", _bob);
        Basic("Forest", _bob);

        var lynx = SunspineLynxFactory.Create(_alice, triggers, replacements: null);
        lynx.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lynx);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        // The Lynx entering fires its own ETB.
        bus.Publish(new CardMovedEvent(lynx, ZoneType.Hand, ZoneType.Battlefield));
        triggers.PendingCount.Should().Be(1, "the Lynx's ETB fires on self-entry");

        var trigger = lynx.Abilities.OfType<TriggeredAbility>().Single();
        await trigger.ResolveAsync(agent: null, LiveContext(stack));

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 2,
            "Alice controls 2 nonbasic lands");
        _bob.LifeTotal.Should().Be(bobLifeBefore - 1,
            "Bob controls 1 nonbasic land");
    }

    [Fact]
    public async System.Threading.Tasks.Task Etb_ZeroNonbasicLands_PlayerTakesNoDamage()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice has a nonbasic; Bob has only basics → Bob takes 0.
        Nonbasic("Steam Vents", _alice);
        Basic("Island", _bob);
        Basic("Mountain", _bob);

        var lynx = SunspineLynxFactory.Create(_alice, triggers, replacements: null);
        lynx.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lynx);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        bus.Publish(new CardMovedEvent(lynx, ZoneType.Hand, ZoneType.Battlefield));
        var trigger = lynx.Abilities.OfType<TriggeredAbility>().Single();
        await trigger.ResolveAsync(agent: null, LiveContext(stack));

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1, "Alice has 1 nonbasic");
        _bob.LifeTotal.Should().Be(bobLifeBefore, "Bob controls only basics → 0 damage");
    }
}
