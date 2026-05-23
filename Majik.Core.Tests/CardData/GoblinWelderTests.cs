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
}
