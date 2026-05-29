using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for An Offer You Can't Refuse (Streets of New Capenna, {U}).
/// Oracle: "Counter target noncreature spell. Its controller creates two
/// Treasure tokens. (They're artifacts with "{T}, Sacrifice this token: Add
/// one mana of any color.")"
///
/// Coverage:
///   * Card identity ({U} Instant, blue, dispatch by name).
///   * SpellDefinition shape (1 target noncreature spell request).
///   * Counter a noncreature spell → lands in graveyard (CR 701.5) AND its
///     controller gets two Treasure tokens (CR 111.10).
///   * Target is a creature spell → no-op at resolution: not countered, no
///     Treasures minted (CR 608.2b).
/// </summary>
public class AnOfferYouCantRefuseTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AnOfferYouCantRefuseTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var offer = AnOfferYouCantRefuseFactory.Create(_alice);

        offer.Name.Should().Be("An Offer You Can't Refuse");
        offer.HasType(CardType.Instant).Should().BeTrue();
        offer.ManaCost.Should().Be("{U}");
        CardColors.GetColors(offer).Should().Contain(ManaColor.Blue,
            "An Offer You Can't Refuse has blue in its cost {U}");
        offer.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsOfferShape()
    {
        var dispatched = NamedCardFactory.Create("An Offer You Can't Refuse", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("An Offer You Can't Refuse");
        dispatched.ManaCost.Should().Be("{U}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetNoncreatureSpellRequest()
    {
        var def = AnOfferYouCantRefuseFactory.BuildSpellDefinition(o => o, null, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("noncreature");
    }

    // -----------------------------------------------------------------------
    // Counter a noncreature spell + mint two Treasures for its controller
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersNoncreatureSpell_AndGivesControllerTwoTreasures()
    {
        var offer = AnOfferYouCantRefuseFactory.Create(_alice);
        offer.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(offer);

        // Bob casts a noncreature spell (Lightning Bolt {R}).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, offer,
            AnOfferYouCantRefuseFactory.BuildSpellDefinition(o => o, _stack, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            "An Offer You Can't Refuse counters the noncreature spell (CR 701.5)");

        // CR 111.10 — the countered spell's CONTROLLER (Bob) gets two Treasures.
        var bobTreasures = _bob.Zones.Battlefield.GetCards()
            .Where(c => c.Name == "Treasure")
            .ToList();
        bobTreasures.Should().HaveCount(2,
            "the countered spell's controller creates two Treasure tokens");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "the Treasures go to the countered spell's controller, not the Offer's caster");
    }

    // -----------------------------------------------------------------------
    // Creature spell — no-op (CR 608.2b): no counter, no Treasures
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounterCreatureSpell_NoTreasures()
    {
        var offer = AnOfferYouCantRefuseFactory.Create(_alice);
        offer.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(offer);

        // Bob casts a creature spell (Grizzly Bears {1}{G}).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, offer,
            AnOfferYouCantRefuseFactory.BuildSpellDefinition(o => o, _stack, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — creature spell is an illegal target at resolution.
        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            "An Offer You Can't Refuse does not counter creature spells");
        _bob.Zones.Battlefield.GetCards().Where(c => c.Name == "Treasure")
            .Should().BeEmpty("no counter means no Treasures (CR 608.2b)");
    }
}
