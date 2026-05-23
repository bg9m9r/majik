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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Lightning Helix (Ravnica: City of Guilds / Modern Horizons,
/// {R}{W}, Instant).
///
/// Oracle text:
///   "Lightning Helix deals 3 damage to any target and you gain 3 life."
///
/// Covers:
///   - Card identity (Instant, {R}{W}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve against opponent → 3 damage to them + 3 life to controller.
///   - Resolve against creature → 3 damage to creature + 3 life to controller.
///   - Resolve against planeswalker → 3 loyalty removed + 3 life to controller.
/// </summary>
public class LightningHelixTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public LightningHelixTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningHelix_IsInstant_AtCostRW()
    {
        var lh = LightningHelixFactory.Create(_alice);

        lh.Name.Should().Be("Lightning Helix");
        lh.ManaCost.Should().Be("{R}{W}");
        lh.HasType(CardType.Instant).Should().BeTrue();
        lh.Owner.Should().BeSameAs(_alice);
        lh.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LightningHelix()
    {
        var card = NamedCardFactory.Create("Lightning Helix", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Lightning Helix");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — 3 damage + 3 life
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LightningHelix_TargetingOpponent_Deals3Damage_AndController_Gains3Life()
    {
        var bobStarting = _bob.LifeTotal;
        var aliceStarting = _alice.LifeTotal;

        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 3, "3 damage to the targeted player");
        _alice.LifeTotal.Should().Be(aliceStarting + 3, "controller gains 3 life");
    }

    [Fact]
    public async Task LightningHelix_TargetingCreature_Deals3Damage_AndController_Gains3Life()
    {
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var aliceStarting = _alice.LifeTotal;

        await CastAndResolveTargeting(bobBear);

        bobBear.Damage.Should().Be(3, "3 damage to the targeted creature");
        _alice.LifeTotal.Should().Be(aliceStarting + 3, "controller gains 3 life");
    }

    [Fact]
    public async Task LightningHelix_TargetingControllersPlaneswalker_RemovesLoyalty_AndController_Gains3Life()
    {
        // Damage to a planeswalker removes that much loyalty (CR 119.3 / 306.7).
        // Lifegain clause fires regardless of where the damage lands (still
        // your controller, not the planeswalker's controller).
        var pw = new Planeswalker(
            "Chandra, Torch of Defiance",
            "{2}{R}{R}",
            startingLoyalty: 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_alice);
        pw.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(pw);

        var aliceStarting = _alice.LifeTotal;

        await CastAndResolveTargeting(pw);

        pw.Loyalty.Should().Be(5 - 3, "3 loyalty removed from the targeted planeswalker");
        _alice.LifeTotal.Should().Be(aliceStarting + 3, "controller gains 3 life");
    }

    [Fact]
    public void LightningHelix_BuildSpellDefinition_DeclaresSingleAnyTargetRequest()
    {
        var def = LightningHelixFactory.BuildSpellDefinition(_alice, t => t);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Lightning Helix from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors UnholyHeatTests'
    /// cast harness — direct cast/resolve, no priority loop.
    /// </summary>
    private async Task CastAndResolveTargeting(object target)
    {
        var lh = LightningHelixFactory.Create(_alice);
        lh.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(lh);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, lh,
            LightningHelixFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        lh.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
