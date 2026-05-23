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
/// Unit tests for <see cref="GoblinEngineerFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated ability structural shape (mana cost + tap).
/// - ETB trigger: tutor first artifact card from library → graveyard
///   (NOT hand — distinguishes from Trinket Mage / Goblin Matron).
/// - ETB no-op when no artifact card exists in library.
/// - Activated ability resolution: sacrifice an artifact + reanimate
///   target artifact card from graveyard.
/// - Activated ability no-op when no graveyard artifact exists.
/// </summary>
public class GoblinEngineerTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinEngineer_Identity()
    {
        var c = GoblinEngineerFactory.Create(_alice);

        c.Name.Should().Be("Goblin Engineer");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue("Goblin Engineer is a Goblin");
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue("Goblin Engineer is an Artificer");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{1}{R}");
    }

    [Fact]
    public void GoblinEngineer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Engineer", _alice);

        c.Should().BeOfType<Creature>("Goblin Engineer is a Creature");
        c.Name.Should().Be("Goblin Engineer");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinEngineer_ActivatedAbility_HasManaAndTapCosts()
    {
        var eng = GoblinEngineerFactory.Create(_alice);

        var ability = eng.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activated ability has the {R} mana cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the activated ability has the {T} tap-self cost");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — artifact → graveyard (NOT hand)
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinEngineer_EtbTrigger_TutorsArtifactCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var eng = GoblinEngineerFactory.Create(alice);

        // Seed the library with an artifact, a non-artifact (instant), and
        // another artifact. The deterministic v1 picker takes the first
        // artifact card it encounters.
        var lightningBolt = new Instant("Lightning Bolt", "{R}");
        lightningBolt.SetOwner(alice);
        alice.Zones.Library.AddCard(lightningBolt);
        lightningBolt.SetZone(ZoneType.Library);

        var firstArtifact = new Artifact("Sol Ring", "{1}");
        firstArtifact.SetOwner(alice);
        alice.Zones.Library.AddCard(firstArtifact);
        firstArtifact.SetZone(ZoneType.Library);

        var secondArtifact = new Artifact("Sensei's Divining Top", "{1}");
        secondArtifact.SetOwner(alice);
        alice.Zones.Library.AddCard(secondArtifact);
        secondArtifact.SetZone(ZoneType.Library);

        // Drive the ETB trigger's effect directly (the trigger is attached
        // for shape but not registered with a TriggerManager in this path).
        var etbTrigger = eng.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etbTrigger.Effects) eff.Execute();

        firstArtifact.Zone.Should().Be(ZoneType.Graveyard,
            "Goblin Engineer's ETB tutor sends the picked artifact to the graveyard, NOT to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(firstArtifact);
        alice.Zones.Library.GetCards().Should().NotContain(firstArtifact);

        // Card was NOT routed to hand — distinguishes from Trinket Mage / Goblin Matron.
        alice.Zones.Hand.GetCards().Should().NotContain(firstArtifact);

        // Untouched: the instant remained in the library, and the second
        // artifact was not also moved (only one card is tutored).
        lightningBolt.Zone.Should().Be(ZoneType.Library);
        secondArtifact.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void GoblinEngineer_EtbTrigger_NoArtifactInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var eng = GoblinEngineerFactory.Create(alice);

        // Library is non-empty but contains no artifact cards.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var etbTrigger = eng.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etbTrigger.Effects) eff.Execute();

        // No mutation — the bolt stays in the library and nothing reaches the graveyard.
        bolt.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Activated ability — sac an artifact + reanimate
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinEngineer_ActivatedAbility_SacsArtifact_ReanimatesGraveyardArtifact()
    {
        var alice = new Player("Alice", 20);
        var eng = GoblinEngineerFactory.Create(alice);

        // Engineer is on the battlefield as the source of the activation.
        alice.Zones.Battlefield.AddCard(eng);
        eng.SetZone(ZoneType.Battlefield);

        // Another artifact under Alice's control to satisfy the
        // "Sacrifice an artifact" cost (resolved in the effect body).
        var sacFodder = new Artifact("Bottle Gnomes", "{3}");
        sacFodder.SetOwner(alice);
        sacFodder.SetController(alice);
        alice.Zones.Battlefield.AddCard(sacFodder);
        sacFodder.SetZone(ZoneType.Battlefield);

        // Artifact card in Alice's graveyard to reanimate.
        var reanimateTarget = new Artifact("Wurmcoil Engine", "{6}");
        reanimateTarget.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(reanimateTarget);
        reanimateTarget.SetZone(ZoneType.Graveyard);

        // Drive the activated ability's effect directly (cost payment is
        // shape-only; the body performs the sacrifice and reanimate).
        var ability = eng.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects) eff.Execute();

        // Sacrificed artifact moved Battlefield → Graveyard (CR 701.16).
        sacFodder.Zone.Should().Be(ZoneType.Graveyard,
            "Goblin Engineer's activated cost sacrifices an artifact — it moves to its owner's graveyard");
        alice.Zones.Graveyard.GetCards().Should().Contain(sacFodder);
        alice.Zones.Battlefield.GetCards().Should().NotContain(sacFodder);

        // Reanimated graveyard artifact moved Graveyard → Battlefield (CR 608).
        reanimateTarget.Zone.Should().Be(ZoneType.Battlefield,
            "the graveyard artifact is reanimated to the battlefield");
        alice.Zones.Battlefield.GetCards().Should().Contain(reanimateTarget);
        alice.Zones.Graveyard.GetCards().Should().NotContain(reanimateTarget);
        reanimateTarget.Controller.Should().BeSameAs(alice,
            "the reanimated artifact enters under its owner's control (CR 110.2)");

        // Engineer itself was not sacrificed — it's a creature, not an artifact.
        eng.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void GoblinEngineer_ActivatedAbility_NoGraveyardArtifact_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var eng = GoblinEngineerFactory.Create(alice);

        alice.Zones.Battlefield.AddCard(eng);
        eng.SetZone(ZoneType.Battlefield);

        // An artifact to sacrifice exists, but no artifact card in the
        // graveyard to reanimate. The body should not commit a partial
        // resolution.
        var sacFodder = new Artifact("Mox Diamond", "{0}");
        sacFodder.SetOwner(alice);
        sacFodder.SetController(alice);
        alice.Zones.Battlefield.AddCard(sacFodder);
        sacFodder.SetZone(ZoneType.Battlefield);

        var ability = eng.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects) eff.Execute();

        // No mutation — the sacrifice fodder remains on the battlefield
        // because there was nothing to reanimate (CR 117.x — no-op).
        sacFodder.Zone.Should().Be(ZoneType.Battlefield);
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
