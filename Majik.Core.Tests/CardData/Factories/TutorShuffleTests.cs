using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 701.20a — verifies the four remaining tutor / library-reorder
/// factories from MECHANIC_DEPS cluster #2 ("Library shuffle") now route
/// the shuffle through <see cref="LibraryShuffle.ShuffleLibrary"/> and
/// publish a <see cref="LibraryShuffledEvent"/> via the
/// <see cref="EventBusRegistry"/>.
///
/// Covered:
///   - <see cref="GoblinEngineerFactory"/> ETB tutor → shuffles.
///   - <see cref="StoneforgeMysticFactory"/> ETB tutor → shuffles.
///   - <see cref="TrinketMageFactory"/> ETB tutor → shuffles.
///   - <see cref="PonderFactory"/> reorder → shuffles only when the
///     controller's agent answers <c>yes</c> to the
///     <see cref="IPlayerAgent.ChooseYesNoAsync"/> +
///     <see cref="BotIntent.LibraryReorder"/> prompt; declines otherwise.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class TutorShuffleTests : IDisposable
{
    // Per-test Player so the static registries are keyed against an
    // instance that no other test class touches. Avoids blanket-Clear()
    // calls that race against AgentRegistryTests / LibraryShuffleTests
    // under xunit's class-parallel default.
    private readonly Player _alice = new("Alice", 20);

    public TutorShuffleTests()
    {
        // Best-effort default RNG so the helper's Get(...) lookup
        // always returns something deterministic, even before the per-
        // player override is registered.
        GameRandomRegistry.SetDefault(new GameRandom(seed: 0));
    }

    public void Dispose()
    {
        // Only tear down state keyed against our private player. No
        // blanket Clear() — other test classes register agents / event
        // buses against their own Player instances and would race with
        // our teardown otherwise.
    }

    /// <summary>Subscribe a fresh <see cref="EventBus"/> for the player so
    /// the test can capture the <see cref="LibraryShuffledEvent"/>.</summary>
    private static (List<LibraryShuffledEvent> events, EventBus bus) AttachShuffleCapture(Player player)
    {
        var bus = new EventBus();
        var captured = new List<LibraryShuffledEvent>();
        bus.Subscribe<LibraryShuffledEvent>(e => captured.Add(e));
        EventBusRegistry.Set(player, bus);
        GameRandomRegistry.Set(player, new GameRandom(seed: 1));
        return (captured, bus);
    }

    // -----------------------------------------------------------------------
    // GoblinEngineer ETB tutor → shuffles
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinEngineer_EtbTutor_ShufflesLibrary_AfterSearch()
    {
        var (captured, _) = AttachShuffleCapture(_alice);

        // Seed a non-artifact + an artifact so the search has a candidate.
        var bait = new Card("Bait", "");
        bait.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        var doodad = new Artifact("Doodad", "1");
        doodad.SetOwner(_alice);
        _alice.Zones.Library.AddCard(doodad);
        doodad.SetZone(ZoneType.Library);

        var engineer = GoblinEngineerFactory.Create(_alice);
        var etb = engineer.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        doodad.Zone.Should().Be(ZoneType.Graveyard, "ETB tutor moves the artifact library → graveyard");
        captured.Should().ContainSingle("CR 701.20a — exactly one shuffle per tutor resolve");
        captured[0].Player.Should().BeSameAs(_alice);
        captured[0].Reason.Should().Be("goblin-engineer");
    }

    // -----------------------------------------------------------------------
    // StoneforgeMystic ETB tutor → shuffles
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_EtbTutor_ShufflesLibrary_AfterSearch()
    {
        var (captured, _) = AttachShuffleCapture(_alice);

        var sword = new Artifact("Sword of Test", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sword.SetOwner(_alice);
        _alice.Zones.Library.AddCard(sword);
        sword.SetZone(ZoneType.Library);

        var mystic = StoneforgeMysticFactory.Create(_alice);
        var etb = mystic.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        sword.Zone.Should().Be(ZoneType.Hand, "ETB tutor moves the Equipment library → hand");
        captured.Should().ContainSingle();
        captured[0].Reason.Should().Be("stoneforge-mystic");
    }

    // -----------------------------------------------------------------------
    // TrinketMage ETB tutor → shuffles
    // -----------------------------------------------------------------------

    [Fact]
    public void TrinketMage_EtbTutor_ShufflesLibrary_AfterSearch()
    {
        var (captured, _) = AttachShuffleCapture(_alice);

        var bauble = new Artifact("Mishra's Bauble", "0");
        bauble.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bauble);
        bauble.SetZone(ZoneType.Library);

        var mage = TrinketMageFactory.Create(_alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bauble.Zone.Should().Be(ZoneType.Hand, "ETB tutor moves the low-cost artifact library → hand");
        captured.Should().ContainSingle();
        captured[0].Reason.Should().Be("trinket-mage");
    }

    // -----------------------------------------------------------------------
    // Ponder — "may shuffle" rider gated on the agent's yes/no
    // -----------------------------------------------------------------------

    [Fact]
    public void Ponder_Resolve_AgentSaysYes_ShufflesLibrary()
    {
        var (captured, _) = AttachShuffleCapture(_alice);

        // Seed enough cards that the peek has something to work with and
        // the trailing draw doesn't flag draw-from-empty.
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");
        SeedLibraryCard(_alice, "D");

        var agent = new ScriptedAgent();
        // Reorder is the identity (keep peeked order); ToBottom must be
        // empty since Ponder puts all peeked cards back on top.
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b, c }));
        // Queue the "yes, shuffle" answer for the LibraryReorder prompt.
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var effect = PonderFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        captured.Should().ContainSingle(
            "agent said yes → CR 701.20 shuffle resolves through LibraryShuffle");
        captured[0].Reason.Should().Be("ponder");
        captured[0].Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ponder_Resolve_AgentSaysNo_DoesNotShuffle()
    {
        var (captured, _) = AttachShuffleCapture(_alice);

        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");
        SeedLibraryCard(_alice, "D");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b, c }));
        // Decline the "may shuffle" rider — Ponder's reorder should leave
        // the (possibly reordered) top three on top.
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var effect = PonderFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        captured.Should().BeEmpty(
            "agent declined the LibraryReorder prompt → no shuffle is performed");
    }

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
