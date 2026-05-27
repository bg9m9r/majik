using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Tests for Tribal Flames (Onslaught / Modern Horizons 2, {1}{R}, Sorcery).
///
/// Covers:
///   - Card identity (Sorcery, {1}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve damage scales with controller's Domain count (CR 702.16):
///     1 basic land, 5 distinct basics, duplicates (distinct only),
///     a single dual land contributing two basic types.
///   - Blood Moon interaction — every nonbasic becomes Mountain, so
///     Domain folds back to 1 regardless of printed subtypes (CR 305.6).
/// </summary>
public class TribalFlamesTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly ContinuousEffectsService _effects = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TribalFlamesTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TribalFlames_IsSorcery_AtCost1R()
    {
        var tf = TribalFlamesFactory.Create(_alice);

        tf.Name.Should().Be("Tribal Flames");
        tf.ManaCost.Should().Be("{1}{R}");
        tf.HasType(CardType.Sorcery).Should().BeTrue();
        tf.Owner.Should().BeSameAs(_alice);
        tf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TribalFlames()
    {
        var card = NamedCardFactory.Create("Tribal Flames", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Tribal Flames");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Domain counting (CR 702.16)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TribalFlames_OneBasicLandControlled_Deals1Damage()
    {
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 1);
    }

    [Fact]
    public async Task TribalFlames_FiveDistinctBasics_Deals5Damage()
    {
        PutBasicOnBattlefield(_alice, CardSubtype.Plains);
        PutBasicOnBattlefield(_alice, CardSubtype.Island);
        PutBasicOnBattlefield(_alice, CardSubtype.Swamp);
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);
        PutBasicOnBattlefield(_alice, CardSubtype.Forest);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 5);
    }

    [Fact]
    public async Task TribalFlames_ThreeBasicsPlusDuplicates_Deals3Damage()
    {
        // Three DISTINCT basic types — duplicates collapse (CR 702.16).
        PutBasicOnBattlefield(_alice, CardSubtype.Plains);
        PutBasicOnBattlefield(_alice, CardSubtype.Plains);   // duplicate
        PutBasicOnBattlefield(_alice, CardSubtype.Island);
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain); // duplicate

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 3);
    }

    [Fact]
    public async Task TribalFlames_DualLand_CountsBothBasicTypes()
    {
        // Stomping Ground — single nonbasic land with Mountain + Forest
        // subtypes. Domain should count both basic land types from that
        // one card → 2 damage.
        var stompingGround = new Land(
            "Stomping Ground",
            supertypes: null,
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        stompingGround.SetOwner(_alice);
        stompingGround.SetController(_alice);
        stompingGround.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(stompingGround);
        _zones.MoveCard(stompingGround, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 2);
    }

    [Fact]
    public async Task TribalFlames_UnderBloodMoon_DomainCollapsesToMountainOnly()
    {
        // Five "lands" with five different printed basic types, but
        // four are nonbasic so Blood Moon retypes them to Mountain.
        // After Blood Moon: effective subtypes are {Mountain} (plus the
        // one printed basic Plains). Domain should be 2 — Mountain
        // (from the 4 retyped nonbasics) + Plains (from the basic that
        // Blood Moon doesn't touch).
        PutBasicOnBattlefield(_alice, CardSubtype.Plains);
        PutNonbasicLand(_alice, "Tropical Island", CardSubtype.Island);   // basic-island nonbasic
        PutNonbasicLand(_alice, "Bayou",           CardSubtype.Swamp);
        PutNonbasicLand(_alice, "Taiga",           CardSubtype.Mountain);
        PutNonbasicLand(_alice, "Savannah",        CardSubtype.Forest);

        // Sanity baseline: without Blood Moon, domain = 5.
        TribalFlamesFactory.CountDomain(_alice, _effects).Should().Be(5);

        // Bring Blood Moon onto the battlefield — wired through the live
        // ContinuousEffectsService + bus so its RetypeLandsStaticEffect
        // registers a Layer-4 SetSubtypesEffect on every nonbasic land.
        var bloodMoon = BloodMoonFactory.Create(_alice, _effects, _bus);
        bloodMoon.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bloodMoon);
        _zones.MoveCard(bloodMoon, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Domain post-Blood-Moon: {Plains, Mountain} = 2.
        TribalFlamesFactory.CountDomain(_alice, _effects).Should().Be(2);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 2,
            "Blood Moon (CR 305.6) collapses the 4 nonbasics to Mountain → " +
            "Domain = {Plains, Mountain} regardless of printed types");
    }

    [Fact]
    public async Task TribalFlames_NoLandsControlled_Deals0Damage()
    {
        // Empty battlefield → Domain = 0 → 0 damage (still a legal cast
        // with a legal target — the X value just resolves to zero).
        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting);
    }

    [Fact]
    public void CountDomain_PrintedSubtypeFallback_WhenEffectsServiceNull()
    {
        // No layers service → use printed subtypes directly.
        PutBasicOnBattlefield(_alice, CardSubtype.Plains);
        PutBasicOnBattlefield(_alice, CardSubtype.Forest);

        TribalFlamesFactory.CountDomain(_alice, effects: null).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Put a basic land of the given subtype onto <paramref name="controller"/>'s
    /// battlefield via the real <see cref="ZoneService"/> so any
    /// CardMovedEvent listeners (Blood Moon's RetypeLandsStaticEffect)
    /// see the move.
    /// </summary>
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

    /// <summary>
    /// Put a nonbasic land with one printed basic subtype onto
    /// <paramref name="controller"/>'s battlefield. Used to set up the
    /// Blood Moon scenario where 4 nonbasics get retyped.
    /// </summary>
    private void PutNonbasicLand(Player controller, string name, CardSubtype basic)
    {
        var land = new Land(
            name,
            supertypes: null,
            subtypes: new[] { basic });
        land.SetOwner(controller);
        land.SetController(controller);
        land.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(land);
        _zones.MoveCard(land, ZoneType.Library, ZoneType.Battlefield, controller);
    }

    /// <summary>
    /// Cast Tribal Flames from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors the
    /// UnholyHeatTests cast harness — direct cast/resolve, no priority
    /// loop.
    /// </summary>
    private async Task CastAndResolveTargeting(object target)
    {
        var tf = TribalFlamesFactory.Create(_alice);
        tf.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(tf);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, tf,
            TribalFlamesFactory.BuildSpellDefinition(_alice, _effects, t => t),
            agent, ctx);

        tf.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
