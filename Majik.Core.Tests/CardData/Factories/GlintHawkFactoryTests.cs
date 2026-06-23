using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlintHawkFactory"/>.
///
/// Glint Hawk — Creature — Bird {W} 2/2.
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, sacrifice it unless you return an artifact
///    you control to its owner's hand."
///
/// Covers (UNIQUE behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness):
/// - Identity (name, type, P/T 2/2, Bird subtype, mana cost {W}, White colour).
/// - Flying keyword marker (CR 702.9).
/// - Exactly one ETB triggered ability with one OPTIONAL (0..1) artifact
///   target request.
/// - ETB resolution: returning an artifact you control satisfies "unless" →
///   the artifact goes to its owner's hand and Glint Hawk is NOT sacrificed.
/// - ETB resolution: no artifact returned → Glint Hawk is sacrificed (CR
///   701.16).
/// - CR 608.2b: chosen artifact already off the battlefield → unless cost not
///   paid → Glint Hawk is sacrificed.
/// </summary>
[Trait("Color", "W")]
public class GlintHawkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintHawk_Identity()
    {
        var c = GlintHawkFactory.Create(_alice);

        c.Name.Should().Be("Glint Hawk");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue("Glint Hawk is a Bird");
        c.ManaCost.Should().Be("{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GlintHawk_Colors_ContainsWhiteOnly()
    {
        var c = GlintHawkFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White, "Glint Hawk costs {W}");
        colors.Should().HaveCount(1, "Glint Hawk is exactly White");
    }

    // -----------------------------------------------------------------------
    // Flying (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintHawk_HasFlying()
    {
        var c = GlintHawkFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue(
            "Glint Hawk has the Flying keyword (CR 702.9)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintHawk_HasExactlyOneTriggeredAbility()
    {
        var c = GlintHawkFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB sacrifice-unless-return trigger on Glint Hawk");
    }

    [Fact]
    public void GlintHawk_EtbTrigger_HasOneOptionalArtifactTargetRequest()
    {
        var c = GlintHawkFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1,
            "exactly one 'an artifact you control' request");

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0,
            "returning an artifact is OPTIONAL — 'unless you return' lets you decline");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("artifact",
            "the return is restricted to an artifact you control");
        req.Intent.Should().Be(BotIntent.Bounce);

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB trigger functions only from the battlefield");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — return an artifact satisfies "unless" (no sacrifice)
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintHawk_EtbEffect_ReturningArtifact_BouncesIt_AndKeepsHawk()
    {
        var alice = new Player("Alice", 20);

        var artifact = new Artifact("Bauble", "{1}");
        artifact.SetOwner(alice);
        artifact.SetController(alice);
        alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var hawk = GlintHawkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(hawk);
        hawk.SetZone(ZoneType.Battlefield);

        var etb = hawk.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { artifact },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        artifact.Zone.Should().Be(ZoneType.Hand,
            "returning the artifact bounces it to its owner's hand (CR 701.10)");
        alice.Zones.Hand.GetCards().Should().Contain(artifact);

        hawk.Zone.Should().Be(ZoneType.Battlefield,
            "the 'unless' cost was paid — Glint Hawk is NOT sacrificed");
        alice.Zones.Battlefield.GetCards().Should().Contain(hawk);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — declining sacrifices Glint Hawk
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintHawk_EtbEffect_NoArtifactReturned_SacrificesHawk()
    {
        var alice = new Player("Alice", 20);

        var hawk = GlintHawkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(hawk);
        hawk.SetZone(ZoneType.Battlefield);

        var etb = hawk.Abilities.OfType<TriggeredAbility>().Single();
        // ChosenTargets left empty — the controller returned no artifact.

        foreach (var effect in etb.Effects) effect.Execute();

        hawk.Zone.Should().Be(ZoneType.Graveyard,
            "no artifact returned → Glint Hawk is sacrificed (CR 701.16)");
        alice.Zones.Graveyard.GetCards().Should().Contain(hawk);
        alice.Zones.Battlefield.GetCards().Should().NotContain(hawk);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — chosen artifact already gone (CR 608.2b) → sacrifice
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintHawk_EtbEffect_ChosenArtifactAlreadyLeft_SacrificesHawk()
    {
        // CR 608.2b — if the chosen artifact is no longer on the battlefield at
        // resolution, the "unless" cost is not paid, so Glint Hawk is sacrificed.
        var alice = new Player("Alice", 20);

        var artifact = new Artifact("Bauble", "{1}");
        artifact.SetOwner(alice);
        artifact.SetController(alice);
        alice.Zones.Graveyard.AddCard(artifact);
        artifact.SetZone(ZoneType.Graveyard); // already gone at resolution time

        var hawk = GlintHawkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(hawk);
        hawk.SetZone(ZoneType.Battlefield);

        var etb = hawk.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { artifact },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        hawk.Zone.Should().Be(ZoneType.Graveyard,
            "illegal artifact target → unless cost unpaid → Glint Hawk sacrificed");
        artifact.Zone.Should().Be(ZoneType.Graveyard,
            "the already-gone artifact stays where it was");
    }
}
