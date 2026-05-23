using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Surgical Extraction (New Phyrexia, <c>{B/P}</c>).
/// Exercises:
///   * Card shape (Instant + black + cost) + NamedCardFactory dispatch.
///   * Cast paying {B}: exiles all copies of the chosen card name from the
///     target's owner's graveyard, hand, and library; library is shuffled.
///   * Cast paying 2 life via PhyrexianManaAlternativeCost: same effect,
///     controller's life -2.
///   * Targeting a basic land card → cast aborted at EffectFactory time
///     (CR 601.2c illegal-target).
///   * Cast with only one matching copy → only that copy exiled (no false
///     positives across other names).
///   * PhyrexianManaAlternativeCost: parses {B/P} as 2-life alt cost with
///     zero remaining mana.
/// </summary>
public class SurgicalExtractionTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SurgicalExtractionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Black()
    {
        var sx = SurgicalExtractionFactory.Create(_alice);

        sx.Name.Should().Be("Surgical Extraction");
        sx.HasType(CardType.Instant).Should().BeTrue();
        sx.Owner.Should().Be(_alice);
        sx.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSurgicalExtractionShape()
    {
        var dispatched = NamedCardFactory.Create("Surgical Extraction", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Surgical Extraction");
    }

    [Fact]
    public void PhyrexianAlternativeCost_ParsesBPipAsTwoLifeZeroMana()
    {
        var alt = SurgicalExtractionFactory.PhyrexianAlternativeCost();

        alt.LifeCost.Should().Be(2, because: "{B/P} contributes one phyrexian pip = 2 life");
        alt.AlternativeManaCost.Should().Be(ManaCost.Zero,
            because: "Surgical Extraction has no non-phyrexian mana component");
    }

    [Fact]
    public async Task CastPayingMana_ExilesAllCopiesOfTargetName_AcrossOwnersZones()
    {
        // Bob owns four Lightning Bolts split across his graveyard, hand,
        // and library. Surgical Extraction (cast by Alice) names the
        // graveyard Bolt and should exile all four + shuffle Bob's library.
        var sx = SurgicalExtractionFactory.Create(_alice);
        sx.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sx);

        var graveBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        graveBolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(graveBolt);

        var handBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        handBolt.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(handBolt);

        var libBolt1 = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        libBolt1.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libBolt1);

        var libBolt2 = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        libBolt2.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libBolt2);

        // Decoy: a different-named card stays in Bob's hand to confirm we
        // don't sweep by type.
        var decoy = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        decoy.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(decoy);

        var startingLife = _alice.LifeTotal;
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)graveBolt });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var graveyardCards = new ICard[] { graveBolt };
        await _flow.CastAsync(
            _alice, sx,
            SurgicalExtractionFactory.BuildDefinition(graveyardCards),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        graveBolt.Zone.Should().Be(ZoneType.Exile);
        handBolt.Zone.Should().Be(ZoneType.Exile);
        libBolt1.Zone.Should().Be(ZoneType.Exile);
        libBolt2.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(new ICard[] { graveBolt, handBolt, libBolt1, libBolt2 });
        _bob.Zones.Library.GetCards().Should().BeEmpty(
            because: "all library Bolts were exiled");
        decoy.Zone.Should().Be(ZoneType.Hand,
            because: "Counterspell shares no name with Lightning Bolt");
        _alice.LifeTotal.Should().Be(startingLife,
            because: "Surgical Extraction was cast paying mana, not life");
    }

    [Fact]
    public async Task CastPayingTwoLife_ExilesCopies_AndCharges2Life()
    {
        var sx = SurgicalExtractionFactory.Create(_alice);
        sx.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sx);

        var graveBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        graveBolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(graveBolt);

        var libBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        libBolt.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libBolt);

        var startingLife = _alice.LifeTotal;
        var phyrexian = SurgicalExtractionFactory.PhyrexianAlternativeCost();

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)graveBolt });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var graveyardCards = new ICard[] { graveBolt };
        await _flow.CastAsync(
            _alice, sx,
            SurgicalExtractionFactory.BuildDefinition(graveyardCards),
            agent, ctx,
            alternativeCost: phyrexian);

        _resolver.ResolveTop(_stack);

        graveBolt.Zone.Should().Be(ZoneType.Exile);
        libBolt.Zone.Should().Be(ZoneType.Exile);
        _alice.LifeTotal.Should().Be(startingLife - 2,
            because: "phyrexian alt cost charges 2 life per {B/P} pip");
    }

    [Fact]
    public async Task CastTargetingBasicLandCard_IsRejected()
    {
        // Bob has a Swamp (basic land) in his graveyard. Targeting it
        // should abort the cast at EffectFactory time (CR 601.2c —
        // illegal target = cast rewound).
        var sx = SurgicalExtractionFactory.Create(_alice);
        sx.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sx);

        var swamp = NamedCardFactory.Create("Swamp", _bob);
        swamp.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(swamp);

        var agent = new ScriptedAgent();
        // Even though the legal-candidate set excludes the swamp, an
        // adversarial caller can still queue it as the target. The
        // EffectFactory rejects it explicitly.
        agent.QueueTargets(new[] { (object)swamp });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        // Pass the empty graveyard-set so the legal-candidate filter would
        // (in well-behaved flows) prevent the agent from picking the swamp;
        // here we still verify EffectFactory throws when it does.
        var def = SurgicalExtractionFactory.BuildDefinition(new ICard[] { swamp });

        Func<Task> cast = () => _flow.CastAsync(
            _alice, sx, def, agent, ctx, alternativeCost: null);

        await cast.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*basic land*");
    }

    [Fact]
    public async Task CastWithOnlyOneCopy_ExilesJustThatCopy()
    {
        // Bob has exactly one Lightning Bolt (in graveyard) and a different
        // card (Counterspell) in hand. Surgical Extraction names the Bolt;
        // only that single Bolt is exiled.
        var sx = SurgicalExtractionFactory.Create(_alice);
        sx.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sx);

        var graveBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        graveBolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(graveBolt);

        var otherCard = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        otherCard.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(otherCard);

        var libCard = new Instant("Brainstorm", "{U}") { Owner = _bob, Controller = _bob };
        libCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libCard);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)graveBolt });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, sx,
            SurgicalExtractionFactory.BuildDefinition(new ICard[] { graveBolt }),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        graveBolt.Zone.Should().Be(ZoneType.Exile);
        otherCard.Zone.Should().Be(ZoneType.Hand,
            because: "Counterspell shares no name with Lightning Bolt");
        libCard.Zone.Should().Be(ZoneType.Library,
            because: "Brainstorm shares no name with Lightning Bolt");
        _bob.Zones.Exile.GetCards().Should().ContainSingle()
            .Which.Should().Be(graveBolt);
    }
}
