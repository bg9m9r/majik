using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
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
/// Tests for Igneous Inspiration (Dominaria United, {2}{R}, Sorcery).
///
/// Oracle:
///   Kicker {3}
///   Domain — Igneous Inspiration deals N damage to any target, where N is
///   one plus the number of basic land types among lands you control.
///   If this spell was kicked, exile the top card of your library. Until
///   the end of your next turn, you may play that card.
///
/// Coverage:
///   - Identity (Sorcery, {2}{R}, owner/controller) + NamedCardFactory dispatch.
///   - Domain damage: N = 1 + distinct basic land types controlled.
///     Verified at 1 basic (= 2 damage) and 5 distinct basics (= 6 damage).
///   - Not-kicked branch: no library card exiled, no runtime exile-cast grant.
///   - Kicked branch: top of library moves to exile and receives a runtime
///     exile-cast grant (Card.RuntimeExileCastGrant).
/// </summary>
public class IgneousInspirationTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly ContinuousEffectsService _effects = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public IgneousInspirationTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void IgneousInspiration_Identity()
    {
        var c = IgneousInspirationFactory.Create(_alice);

        c.Name.Should().Be("Igneous Inspiration");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_IgneousInspiration()
    {
        var card = NamedCardFactory.Create("Igneous Inspiration", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Igneous Inspiration");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
    }

    // -----------------------------------------------------------------------
    // Domain damage (CR 702.16) — N = 1 + distinct basic land types
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotKicked_OneBasicLandControlled_Deals2Damage()
    {
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);

        var bobStart = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob, wasKicked: false);

        _bob.LifeTotal.Should().Be(bobStart - 2,
            "N = 1 + Domain(=1 Mountain) = 2");
    }

    [Fact]
    public async Task NotKicked_FiveDistinctBasics_Deals6Damage()
    {
        PutBasicOnBattlefield(_alice, CardSubtype.Plains);
        PutBasicOnBattlefield(_alice, CardSubtype.Island);
        PutBasicOnBattlefield(_alice, CardSubtype.Swamp);
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);
        PutBasicOnBattlefield(_alice, CardSubtype.Forest);

        var bobStart = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob, wasKicked: false);

        _bob.LifeTotal.Should().Be(bobStart - 6,
            "N = 1 + Domain(=5 distinct basic types) = 6");
    }

    // -----------------------------------------------------------------------
    // Kicker rider — exile top of library + runtime exile-cast grant
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotKicked_DoesNotExileLibrary()
    {
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);
        SeedLibrary(_alice, "Top1");

        await CastAndResolveTargeting(_bob, wasKicked: false);

        _alice.Zones.Exile.GetCards().Should().BeEmpty(
            "no kicker → no exile rider (CR 702.33b — the conditional " +
            "tail clause fires only when 'this spell was kicked')");
        _alice.Zones.Library.GetCards().Select(c => c.Name).Should()
            .Contain("Top1", "Top1 stays on top of the library");
    }

    [Fact]
    public async Task Kicked_ExilesTopOfLibrary_AndGrantsRuntimeExileCast()
    {
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);
        SeedLibrary(_alice, "Top1", "Top2");

        var bobStart = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob, wasKicked: true);

        // Damage still scales with Domain (CR 702.16) — kicker doesn't
        // change the printed damage clause, it only ADDS the exile rider.
        _bob.LifeTotal.Should().Be(bobStart - 2,
            "N = 1 + Domain still applies on the kicked branch");

        var exiled = _alice.Zones.Exile.GetCards().ToList();
        exiled.Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Top1", "kicker exiles the top library card");

        var exiledCard = (Card)exiled[0];
        exiledCard.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "kicker stamps a runtime exile-cast grant so 'you may play " +
            "that card' is honoured via ExileCastAlternativeCost");
        exiledCard.RuntimeExileCastCost.Should().NotBeNull();
        exiledCard.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void PutBasicOnBattlefield(Player controller, CardSubtype basic)
    {
        var name = basic.ToString();
        var land = new Land(
            name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { basic });
        land.SetOwner(controller);
        land.SetController(controller);
        land.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(land);
        _zones.MoveCard(land, ZoneType.Library, ZoneType.Battlefield, controller);
    }

    private static void SeedLibrary(Player player, params string[] names)
    {
        foreach (var n in names)
        {
            var c = new Card(n, "");
            c.SetOwner(player);
            c.SetController(player);
            c.SetZone(ZoneType.Library);
            player.Zones.Library.AddCard(c);
        }
    }

    /// <summary>
    /// Cast Igneous Inspiration from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors Burst Lightning's
    /// kicker harness — direct cast/resolve, no priority loop; the kicked
    /// branch layers <see cref="KickerAdditionalCost"/> via the cast flow.
    /// </summary>
    private async Task CastAndResolveTargeting(object target, bool wasKicked)
    {
        var ii = IgneousInspirationFactory.Create(_alice);
        ii.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ii);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        IReadOnlyList<IAdditionalCost>? additional = null;
        if (wasKicked)
        {
            _alice.AddManaToPool(ManaCost.Parse("{3}"));
            additional = new[] { IgneousInspirationFactory.BuildAdditionalCost(ii) };
        }

        var spell = await _flow.CastAsync(
            _alice, ii,
            IgneousInspirationFactory.BuildSpellDefinition(
                ii, _alice, _effects, t => t, _bus),
            agent, ctx,
            additionalCosts: additional);

        ii.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();
    }
}
