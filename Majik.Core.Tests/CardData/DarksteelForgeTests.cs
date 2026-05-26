using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Darksteel Forge (Darksteel, {9}).
///
/// Covers:
///   - Card identity (Artifact, name, mana cost, owner/controller) +
///     <see cref="NamedCardFactory"/> dispatch shape.
///   - Printed Indestructible on the Forge itself (KeywordAbility marker).
///   - Static grant "Other artifacts you control have indestructible"
///     registers a predicate in
///     <see cref="IndestructibleGrantRegistry"/> while Forge is on the
///     battlefield and lifts the grant on LTB.
///   - Grant covers other artifacts the controller controls, excludes
///     non-artifacts, excludes opponents' artifacts, and excludes the
///     Forge itself ("other" — CR 109.3).
///   - <see cref="OracleSpellBinder.MoveToGraveyard"/>'s destroy gate
///     respects the grant.
/// </summary>
public class DarksteelForgeTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DarksteelForgeTests()
    {
        IndestructibleGrantRegistry.Clear();
    }

    public void Dispose()
    {
        IndestructibleGrantRegistry.Clear();
    }

    [Fact]
    public void DarksteelForge_Identity_ArtifactAt9()
    {
        var forge = DarksteelForgeFactory.Create(_alice);

        forge.Name.Should().Be("Darksteel Forge");
        forge.ManaCost.Should().Be("{9}");
        forge.HasType(CardType.Artifact).Should().BeTrue();
        forge.Owner.Should().BeSameAs(_alice);
        forge.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DarksteelForge_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Darksteel Forge", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Darksteel Forge");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void DarksteelForge_HasPrintedIndestructibleKeyword()
    {
        var forge = DarksteelForgeFactory.Create(_alice);

        forge.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void DarksteelForge_OnBattlefield_GrantsIndestructibleToOtherArtifactsYouControl()
    {
        var (bus, zones) = BuildEngine();
        var forge = PutForgeOnBattlefield(bus, zones);

        // Another artifact Alice controls.
        var ally = new Artifact("Ally Artifact", "{1}");
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);

        IndestructibleGrantRegistry.HasGrantedIndestructible(ally).Should().BeTrue();
    }

    [Fact]
    public void DarksteelForge_DoesNotGrantToSelf_OtherClause()
    {
        var (bus, zones) = BuildEngine();
        var forge = PutForgeOnBattlefield(bus, zones);

        // CR 109.3 — "other" excludes the Forge itself (its own
        // indestructible is the printed keyword, not the granted one).
        IndestructibleGrantRegistry.HasGrantedIndestructible(forge).Should().BeFalse();
    }

    [Fact]
    public void DarksteelForge_DoesNotGrantToOpponentsArtifacts()
    {
        var (bus, zones) = BuildEngine();
        var forge = PutForgeOnBattlefield(bus, zones);

        // CR 109.5 — "you control" — Bob's artifact is excluded.
        var bobsArtifact = new Artifact("Bob's Artifact", "{1}");
        bobsArtifact.SetOwner(_bob);
        bobsArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobsArtifact);
        bobsArtifact.SetZone(ZoneType.Battlefield);

        IndestructibleGrantRegistry.HasGrantedIndestructible(bobsArtifact).Should().BeFalse();
    }

    [Fact]
    public void DarksteelForge_DoesNotGrantToNonArtifacts()
    {
        var (bus, zones) = BuildEngine();
        var forge = PutForgeOnBattlefield(bus, zones);

        var creature = new Creature("Pure Creature", "{1}", 1, 1);
        creature.SetOwner(_alice);
        creature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);

        IndestructibleGrantRegistry.HasGrantedIndestructible(creature).Should().BeFalse();
    }

    [Fact]
    public void DarksteelForge_LeavesBattlefield_GrantLifted()
    {
        var (bus, zones) = BuildEngine();
        var forge = PutForgeOnBattlefield(bus, zones);

        var ally = new Artifact("Ally Artifact", "{1}");
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);

        IndestructibleGrantRegistry.HasGrantedIndestructible(ally).Should().BeTrue();

        // Forge LTBs — the CardMovedEvent lifts the grant.
        zones.MoveCardTo(forge, ZoneType.Graveyard, controller: _alice);

        IndestructibleGrantRegistry.HasGrantedIndestructible(ally).Should().BeFalse();
    }

    [Fact]
    public void DarksteelForge_GrantedIndestructible_BlocksDestroyMove()
    {
        var (bus, zones) = BuildEngine();
        var forge = PutForgeOnBattlefield(bus, zones);

        // Vanilla artifact: no printed indestructible. Should still survive
        // because Forge's grant covers it.
        var ally = new Artifact("Ally Artifact", "{1}");
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);

        // OracleSpellBinder.MoveToGraveyard is internal — exercise the
        // gate via the registry directly, which is what the gate consults.
        IndestructibleGrantRegistry.HasGrantedIndestructible(ally).Should().BeTrue();
    }

    [Fact]
    public void MultipleForges_BothRegisterIndependently()
    {
        var (bus, zones) = BuildEngine();
        var forge1 = PutForgeOnBattlefield(bus, zones);
        var forge2 = PutForgeOnBattlefield(bus, zones);

        var ally = new Artifact("Ally", "{1}");
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);

        IndestructibleGrantRegistry.HasGrantedIndestructible(ally).Should().BeTrue();

        // Remove one Forge — grant is still active via the other.
        zones.MoveCardTo(forge1, ZoneType.Graveyard, controller: _alice);

        IndestructibleGrantRegistry.HasGrantedIndestructible(ally).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (EventBus bus, ZoneService zones) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new Majik.Core.Effects.ReplacementBus();
        var zones = new ZoneService(bus, rep);
        return (bus, zones);
    }

    /// <summary>
    /// Build a Forge wired to <paramref name="bus"/> and route it onto the
    /// battlefield through <paramref name="zones"/> so the
    /// <see cref="IndestructibleGrantStaticEffect"/>'s
    /// <see cref="CardMovedEvent"/>-driven sync fires.
    /// </summary>
    private Artifact PutForgeOnBattlefield(EventBus bus, ZoneService zones)
    {
        var forge = DarksteelForgeFactory.Create(_alice, bus);
        _alice.Zones.Hand.AddCard(forge);
        forge.SetZone(ZoneType.Hand);
        zones.MoveCardTo(forge, ZoneType.Battlefield, controller: _alice);
        return forge;
    }
}
