using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Snuff Out (Mercadian Masques / reprints, {3}{B}, Instant).
///
/// Oracle text: "If you control a Swamp, you may pay 4 life rather than pay
///               this spell's mana cost.
///               Destroy target nonblack creature. It can't be regenerated."
///
/// Covers:
///   - Card identity (Instant, {3}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a nonblack creature (CR 701.7).
///   - Black creature target → no-op at resolution (CR 105 + CR 608.2b).
///   - Off-battlefield target → no-op (CR 608.2b).
///   - Pay-4-life alt cost: legal only with a Swamp + >= 4 life (CR 118.9 / 119.4).
///   - End-to-end cast via pay-4-life: no mana, 4 life paid, target destroyed.
/// </summary>
[Trait("Color", "B")]
public class SnuffOutFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SnuffOut_IsInstant_AtCost3B()
    {
        var card = SnuffOutFactory.Create(_alice);

        card.Name.Should().Be("Snuff Out");
        card.ManaCost.Should().Be("{3}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SnuffOut()
    {
        var card = NamedCardFactory.Create("Snuff Out", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Snuff Out");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a nonblack creature
    // -----------------------------------------------------------------------

    [Fact]
    public void SnuffOut_DestroysNonblackCreature()
    {
        // Green creature — nonblack: legal target.
        var tarmogoyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        Resolve(tarmogoyf);

        tarmogoyf.Zone.Should().Be(ZoneType.Graveyard,
            "Snuff Out destroys a nonblack creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(tarmogoyf);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(tarmogoyf);
    }

    [Fact]
    public void SnuffOut_DestroysArtifactCreature_StillNonblack()
    {
        // Snuff Out (unlike Terror) has no nonartifact rider: a colorless
        // artifact creature is nonblack and a legal target.
        var myr = NewControlledArtifactCreature(_bob, "Myr Battlesphere", "{7}");

        Resolve(myr);

        myr.Zone.Should().Be(ZoneType.Graveyard,
            "Snuff Out only filters on colour — an artifact creature is nonblack and destroyable");
    }

    // -----------------------------------------------------------------------
    // Resolution — black creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void SnuffOut_BlackCreature_NotDestroyed()
    {
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Battlefield,
            "Snuff Out cannot destroy a black creature (CR 105 nonblack filter)");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(imp);
    }

    [Fact]
    public void SnuffOut_MulticolorCreatureWithBlackPip_NotDestroyed()
    {
        // BR creature — has a {B} pip, so it counts as black (CR 105.2a).
        var demon = NewControlledCreature(_bob, "Kolaghan Demon", "{B}{R}");

        Resolve(demon);

        demon.Zone.Should().Be(ZoneType.Battlefield,
            "A creature with a {B} pip is black (CR 105.2a) and immune to Snuff Out");
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void SnuffOut_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        // CR 608.2b — illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Pay-4-life alternative cost (CR 118.9 / 119.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void PayLifeAltCost_LegalWhenControllingSwamp_AndEnoughLife()
    {
        var snuff = SnuffOutFactory.Create(_alice);
        AddSwamp(_alice);

        var cost = new PayLifeIfControlSwampAlternativeCost(SnuffOutFactory.AlternativeLifeCost);

        cost.CanCastFor(snuff, _alice).Should().BeTrue(
            "Alice controls a Swamp and has >= 4 life (CR 118.9)");
        cost.AlternativeManaCost.Should().Be(ManaCost.Zero, "the life payment is the entire cost");
    }

    [Fact]
    public void PayLifeAltCost_IllegalWithoutSwamp()
    {
        var snuff = SnuffOutFactory.Create(_alice);
        // No Swamp on the battlefield.

        var cost = new PayLifeIfControlSwampAlternativeCost(SnuffOutFactory.AlternativeLifeCost);

        cost.CanCastFor(snuff, _alice).Should().BeFalse(
            "the pay-life alt cost requires controlling a Swamp (CR 118.9)");
    }

    [Fact]
    public void PayLifeAltCost_IllegalWhenLifeBelowFour()
    {
        var lowLife = new Player("Carol", 3);
        var snuff = SnuffOutFactory.Create(lowLife);
        AddSwamp(lowLife);

        var cost = new PayLifeIfControlSwampAlternativeCost(SnuffOutFactory.AlternativeLifeCost);

        cost.CanCastFor(snuff, lowLife).Should().BeFalse(
            "you can't pay 4 life with only 3 (CR 119.4)");
    }

    // -----------------------------------------------------------------------
    // End-to-end cast via pay-4-life
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CastViaPayLife_NoMana_PaysFourLife_DestroysTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var flow = new SpellCastFlow(stack, zones, bus);
        var resolver = new StackResolver(bus, zones);

        var snuff = SnuffOutFactory.Create(_alice);
        snuff.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snuff);
        AddSwamp(_alice);

        var goyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");
        var startingLife = _alice.LifeTotal;

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)goyf });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);

        var altCost = new PayLifeIfControlSwampAlternativeCost(SnuffOutFactory.AlternativeLifeCost);

        await flow.CastAsync(
            _alice, snuff,
            SnuffOutFactory.BuildDefinition(o => o),
            agent, ctx,
            alternativeCost: altCost);

        resolver.ResolveTop(stack);

        // Target destroyed (CR 701.7).
        goyf.Zone.Should().Be(ZoneType.Graveyard);
        // 4 life paid, no mana (CR 118.9).
        _alice.LifeTotal.Should().Be(startingLife - SnuffOutFactory.AlternativeLifeCost);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = SnuffOutFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature NewControlledArtifactCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 4, 7);
        c.AddCardType(CardType.Artifact);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Land AddSwamp(Player owner)
    {
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(owner);
        swamp.SetController(owner);
        swamp.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(swamp);
        return swamp;
    }
}
