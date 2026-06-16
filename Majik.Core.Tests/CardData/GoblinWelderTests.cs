using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

using GameContext = Majik.Core.Game.GameContext;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinWelderFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated ability shape: {T} tap cost, no mana cost.
/// - WeldResolve sac+reanimate for activator's own artifacts.
/// - WeldResolve sac+reanimate for opponent's artifacts (same-player
///   constraint — both halves belong to the same player).
/// - WeldResolve same-player constraint: rejects cross-player pairs
///   (an artifact controlled by Alice + an artifact card in Bob's
///   graveyard is not a legal pair).
/// - WeldResolve no-op when no legal pair exists.
/// </summary>
public class GoblinWelderTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWelder_Identity()
    {
        var c = GoblinWelderFactory.Create(_alice);

        c.Name.Should().Be("Goblin Welder");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue("Goblin Welder is a Goblin");
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue("Goblin Welder is an Artificer");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{R}");
    }

    [Fact]
    public void GoblinWelder_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Welder", _alice);

        c.Should().BeOfType<Creature>("Goblin Welder is a Creature");
        c.Name.Should().Be("Goblin Welder");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWelder_ActivatedAbility_HasTapCost_NoManaCost()
    {
        var welder = GoblinWelderFactory.Create(_alice);

        var ability = welder.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "the activated ability is paid with {T} (tap-self), expressed via AdditionalCost.Tap");
        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Goblin Welder's activated ability has no mana cost — only {T}");
    }

    // -----------------------------------------------------------------------
    // Resolution — sac-then-reanimate for activator's own artifacts
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWelder_WeldResolve_SacsOwnArtifact_ReanimatesOwnGraveyardArtifact()
    {
        var alice = new Player("Alice", 20);

        // Battlefield artifact under Alice's control (a small Mox-shaped
        // artifact stand-in — Goblin Welder cares only about the type).
        var battlefieldArt = new Artifact("Bottle Gnomes", "{3}");
        battlefieldArt.SetOwner(alice);
        battlefieldArt.SetController(alice);
        alice.Zones.Battlefield.AddCard(battlefieldArt);
        battlefieldArt.SetZone(ZoneType.Battlefield);

        // Artifact card in Alice's graveyard to reanimate.
        var graveyardArt = new Artifact("Memnite", "{0}");
        graveyardArt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(graveyardArt);
        graveyardArt.SetZone(ZoneType.Graveyard);

        var welded = GoblinWelderFactory.WeldResolve(new[] { alice });

        welded.Should().BeTrue("a same-player (battlefield artifact, graveyard artifact) pair exists for Alice");

        battlefieldArt.Zone.Should().Be(ZoneType.Graveyard,
            "the battlefield artifact is sacrificed — moves to its owner's graveyard (CR 701.16)");
        alice.Zones.Battlefield.GetCards().Should().NotContain(battlefieldArt);
        alice.Zones.Graveyard.GetCards().Should().Contain(battlefieldArt);

        graveyardArt.Zone.Should().Be(ZoneType.Battlefield,
            "the graveyard artifact is reanimated to the battlefield");
        alice.Zones.Graveyard.GetCards().Should().NotContain(graveyardArt);
        alice.Zones.Battlefield.GetCards().Should().Contain(graveyardArt);
        graveyardArt.Controller.Should().BeSameAs(alice,
            "the reanimated artifact enters under its owner's control (CR 110.2)");
    }

    // -----------------------------------------------------------------------
    // Resolution — sac-then-reanimate for opponent's artifacts (same player)
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWelder_WeldResolve_CanTargetOpponentsArtifactsBothBelongingToSamePlayer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob has a battlefield artifact AND an artifact card in his
        // graveyard. Alice has nothing to work with on her side. Goblin
        // Welder (activated by Alice) targets Bob's artifact + Bob's
        // graveyard artifact — both halves are the same player (Bob).
        var bobBattlefieldArt = new Artifact("Worn Powerstone", "{3}");
        bobBattlefieldArt.SetOwner(bob);
        bobBattlefieldArt.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobBattlefieldArt);
        bobBattlefieldArt.SetZone(ZoneType.Battlefield);

        var bobGraveyardArt = new Artifact("Mishra's Bauble", "{0}");
        bobGraveyardArt.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobGraveyardArt);
        bobGraveyardArt.SetZone(ZoneType.Graveyard);

        // Iterate alice first to prove the scanner correctly skips a
        // player with no eligible pair and picks the next one (Bob).
        var welded = GoblinWelderFactory.WeldResolve(new[] { alice, bob });

        welded.Should().BeTrue("Bob has a (battlefield artifact, graveyard artifact) pair");

        bobBattlefieldArt.Zone.Should().Be(ZoneType.Graveyard,
            "Bob sacrifices the battlefield artifact — it goes to Bob's graveyard");
        bob.Zones.Graveyard.GetCards().Should().Contain(bobBattlefieldArt);

        bobGraveyardArt.Zone.Should().Be(ZoneType.Battlefield,
            "Bob's graveyard artifact returns to Bob's battlefield");
        bob.Zones.Battlefield.GetCards().Should().Contain(bobGraveyardArt);
        bobGraveyardArt.Controller.Should().BeSameAs(bob,
            "the reanimated artifact enters under Bob's control (CR 110.2)");

        // Alice's zones must be untouched.
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolution — same-player constraint
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWelder_WeldResolve_RejectsCrossPlayerPair()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice controls a battlefield artifact; Bob has an artifact in
        // his graveyard. There is NO same-player pair — both halves of
        // Goblin Welder must reference the same player.
        var aliceArt = new Artifact("Howling Mine", "{2}");
        aliceArt.SetOwner(alice);
        aliceArt.SetController(alice);
        alice.Zones.Battlefield.AddCard(aliceArt);
        aliceArt.SetZone(ZoneType.Battlefield);

        var bobGyArt = new Artifact("Mishra's Bauble", "{0}");
        bobGyArt.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobGyArt);
        bobGyArt.SetZone(ZoneType.Graveyard);

        var welded = GoblinWelderFactory.WeldResolve(new[] { alice, bob });

        welded.Should().BeFalse(
            "no legal pair exists — Alice has a battlefield artifact but no graveyard artifact; "
            + "Bob has a graveyard artifact but no battlefield artifact");

        aliceArt.Zone.Should().Be(ZoneType.Battlefield,
            "no sacrifice happens without a legal pair");
        bobGyArt.Zone.Should().Be(ZoneType.Graveyard,
            "no reanimation happens without a legal pair");
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op when no legal pair exists at all
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWelder_WeldResolve_NoPair_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // No artifacts anywhere — resolution is a no-op (CR 117.x).
        var welded = GoblinWelderFactory.WeldResolve(new[] { alice });

        welded.Should().BeFalse("no battlefield artifact + no graveyard artifact → no-op");
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Agatha's Soul Cauldron re-home (CR 707.2 / 613.1f) — the imprinted
    // Goblin Welder's {T}: weld ability is granted to a counter-bearing
    // creature via ActivatedAbility.RebindTo. Two properties are proven:
    //
    //   1. The ability is RebindSafe (provenance flag preserved across the
    //      re-home) — the weld effect body captures NO authoring permanent /
    //      player; it scans ctx.Game.AllPlayers, so it is source-independent.
    //   2. The {T} activation cost re-homes onto the BEARER — the tap taps the
    //      counter-bearer, never the exiled Goblin Welder (Stage 1
    //      AdditionalCost.RebindSource).
    //   3. Resolving the re-homed ability still performs the weld off the live
    //      game context, exactly as the original (source-independence proof).
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWelder_ActivatedAbility_IsRebindSafe()
    {
        var welder = GoblinWelderFactory.Create(_alice, zoneService: null, eventBus: null);

        var ability = welder.Abilities.OfType<ActivatedAbility>().Single();

        ability.RebindSafe.Should().BeTrue(
            "the weld effect body reads its player universe off the live "
            + "ResolutionContext (ctx.Game.AllPlayers) and captures no authoring "
            + "source, so Agatha's Soul Cauldron may soundly re-home it (CR 707.2)");
    }

    [Fact]
    public void GoblinWelder_RebindTo_RehomesTapCostOntoBearer_NotExiledWelder()
    {
        var alice = new Player("Alice", 20);

        // The original Goblin Welder is exiled under Agatha's Soul Cauldron.
        var welder = GoblinWelderFactory.Create(alice, zoneService: null, eventBus: null);
        welder.SetZone(ZoneType.Exile);

        // A counter-bearing creature on the battlefield receives the grant.
        var bearer = new Creature("Bearer", "1G", 3, 3);
        bearer.SetOwner(alice);
        bearer.SetController(alice);
        bearer.SetZone(ZoneType.Battlefield);

        var original = welder.Abilities.OfType<ActivatedAbility>().Single();
        var rebound = original.RebindTo(bearer, alice);

        rebound.RebindSafe.Should().BeTrue("RebindTo preserves the provenance flag");

        var tapCost = rebound.Costs.OfType<AdditionalCost>().Single();
        tapCost.Permanent.Should().BeSameAs(bearer,
            "the {T} cost re-homes onto the bearer — Agatha taps the counter-bearer, "
            + "never the exiled Goblin Welder (Stage 1 AdditionalCost.RebindSource)");
    }

    [Fact]
    public async Task GoblinWelder_RebindTo_ReboundAbility_StillWeldsOffLiveGameContext()
    {
        // The Goblin Welder is OWNED by Bob, but is exiled under Agatha's Soul
        // Cauldron and granted to ALICE's counter-bearing creature. The weld
        // pair belongs to Alice. A source-independent body scans the live game
        // (ctx.Game.AllPlayers, which includes Alice) and welds Alice's pair; a
        // body that captured the welder's original owner (Bob) would scan Bob,
        // find nothing, and no-op — so this proves source-independence.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice has a (battlefield artifact, graveyard artifact) pair to weld.
        var battlefieldArt = new Artifact("Bottle Gnomes", "{3}");
        battlefieldArt.SetOwner(alice);
        battlefieldArt.SetController(alice);
        alice.Zones.Battlefield.AddCard(battlefieldArt);
        battlefieldArt.SetZone(ZoneType.Battlefield);

        var graveyardArt = new Artifact("Memnite", "{0}");
        graveyardArt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(graveyardArt);
        graveyardArt.SetZone(ZoneType.Graveyard);

        // The Goblin Welder itself (owned by Bob) is exiled (Agatha). The weld
        // ability is re-homed onto Alice's counter-bearing creature.
        var welder = GoblinWelderFactory.Create(bob, zoneService: null, eventBus: null);
        welder.SetZone(ZoneType.Exile);

        var bearer = new Creature("Bearer", "1G", 3, 3);
        bearer.SetOwner(alice);
        bearer.SetController(alice);
        bearer.SetZone(ZoneType.Battlefield);

        var rebound = welder.Abilities.OfType<ActivatedAbility>().Single()
            .RebindTo(bearer, alice);

        // Resolve the re-homed ability through the real effect body, threading
        // a live game context (the weld body reads players off ctx.Game).
        var game = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

        await rebound.ResolveAsync(agent: null, game: game);

        battlefieldArt.Zone.Should().Be(ZoneType.Graveyard,
            "the re-homed weld still sacrifices the battlefield artifact (source-independent)");
        graveyardArt.Zone.Should().Be(ZoneType.Battlefield,
            "the re-homed weld still reanimates the graveyard artifact off the live game context");
        alice.Zones.Battlefield.GetCards().Should().Contain(graveyardArt);
    }
}
