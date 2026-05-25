using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DiscipleOfTheVaultFactory"/>
/// (Mirrodin, {B}).
///
/// Creature — Human Cleric 1/1. Oracle text:
///   "Whenever an artifact is put into a graveyard from the battlefield,
///    target opponent loses 1 life."
///
/// Covers:
///   - Identity (Creature — Human Cleric, {B}, 1/1, owner/controller).
///   - NamedCardFactory dispatch returns a Creature with the trigger attached.
///   - Trigger condition matches Battlefield → Graveyard for an Artifact;
///     rejects non-artifact movement, rejects Hand → Graveyard.
///   - Trigger condition matches an Artifact Creature dying (dual-type
///     Vault Skirge dying still fires the trigger).
///   - Effect: target opponent (chosen via ChosenTargets) loses 1 life;
///     no-op when target is the controller (legality recheck).
///   - TargetRequest shape: 1..1 "target opponent".
/// </summary>
public class DiscipleOfTheVaultFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Disciple_Identity()
    {
        var c = DiscipleOfTheVaultFactory.Create(_alice);

        c.Name.Should().Be("Disciple of the Vault");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Disciple_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Disciple of the Vault", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Disciple of the Vault");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void Disciple_HasOneTriggeredAbility_WithTargetOpponentRequest()
    {
        var c = DiscipleOfTheVaultFactory.Create(_alice);
        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1, "exactly one artifact-dies trigger");
        var tgt = triggers[0].TargetRequests.Should().ContainSingle().Subject;
        tgt.MinTargets.Should().Be(1);
        tgt.MaxTargets.Should().Be(1);
        tgt.Description.Should().Contain("opponent", "request reads \"target opponent\"");
    }

    // -------------------------------------------------------------------------
    // Trigger condition shape
    // -------------------------------------------------------------------------

    [Fact]
    public void Trigger_Fires_OnArtifactBattlefieldToGraveyard()
    {
        var disciple = DiscipleOfTheVaultFactory.Create(_alice);
        var trigger = disciple.Abilities.OfType<TriggeredAbility>().Single();

        var artifact = new Artifact("Random Artifact", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var evt = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue();
    }

    [Fact]
    public void Trigger_Fires_OnArtifactCreatureBattlefieldToGraveyard()
    {
        // Vault Skirge is an Artifact Creature; when it dies, Disciple
        // still triggers — printed text is unqualified by "non-creature".
        var disciple = DiscipleOfTheVaultFactory.Create(_alice);
        var trigger = disciple.Abilities.OfType<TriggeredAbility>().Single();

        var skirge = VaultSkirgeFactory.Create(_alice);

        var evt = new CardMovedEvent(skirge, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue(
            "an Artifact Creature dying still satisfies \"an artifact is put into a graveyard\"");
    }

    [Fact]
    public void Trigger_DoesNotFire_OnNonArtifactDying()
    {
        var disciple = DiscipleOfTheVaultFactory.Create(_alice);
        var trigger = disciple.Abilities.OfType<TriggeredAbility>().Single();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var evt = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "non-artifact movements do not trigger Disciple");
    }

    [Fact]
    public void Trigger_DoesNotFire_OnArtifactFromHandToGraveyard()
    {
        // CR 700.4 — "put into a graveyard from the battlefield"; hand
        // → graveyard (discard) does not trigger.
        var disciple = DiscipleOfTheVaultFactory.Create(_alice);
        var trigger = disciple.Abilities.OfType<TriggeredAbility>().Single();

        var artifact = new Artifact("Discarded Artifact", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var evt = new CardMovedEvent(artifact, ZoneType.Hand, ZoneType.Graveyard);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "discarding an artifact does not satisfy \"from the battlefield\"");
    }

    // -------------------------------------------------------------------------
    // Effect resolution
    // -------------------------------------------------------------------------

    [Fact]
    public void Effect_TargetOpponentLosesOneLife()
    {
        var disciple = DiscipleOfTheVaultFactory.Create(_alice);
        var trigger = disciple.Abilities.OfType<TriggeredAbility>().Single();

        // Set the chosen target — Bob (Alice's opponent).
        trigger.SetChosenTargets(new[] { new[] { (object)_bob } });

        foreach (var eff in trigger.Effects) eff.Execute();

        _bob.LifeTotal.Should().Be(19, "20 - 1 life from Disciple's drain");
        _alice.LifeTotal.Should().Be(20, "Alice is the controller, not the target");
    }

    [Fact]
    public void Effect_NoOp_WhenChosenTargetIsController()
    {
        // CR 608.2b — printed text says "target opponent". If the
        // resolver fails the legality recheck, the drain is a no-op.
        // We model the defensive ReferenceEquals filter inside the
        // effect closure (see DiscipleOfTheVaultFactory comment).
        var disciple = DiscipleOfTheVaultFactory.Create(_alice);
        var trigger = disciple.Abilities.OfType<TriggeredAbility>().Single();

        trigger.SetChosenTargets(new[] { new[] { (object)_alice } });

        foreach (var eff in trigger.Effects) eff.Execute();

        _alice.LifeTotal.Should().Be(20, "drain skipped — chosen target is controller");
    }

    [Fact]
    public void Effect_NoOp_WhenNoChosenTarget()
    {
        // Defensive guard — if the stack-resolve path failed to attach
        // a target (illegal-target rewind, CR 608.2b), the effect runs
        // as a no-op.
        var disciple = DiscipleOfTheVaultFactory.Create(_alice);
        var trigger = disciple.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var eff in trigger.Effects) eff.Execute();

        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }
}
