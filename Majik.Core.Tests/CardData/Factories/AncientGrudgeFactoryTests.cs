using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Ancient Grudge (Time Spiral, {1}{R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target artifact.
///    Flashback {G} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Card shape comes from the embedded JSON (<c>ancient-grudge.json</c>) via
/// <see cref="CardDefinitionLoader"/> + <see cref="CardDefinitionFactory"/>;
/// the resolve-time "destroy target artifact" body and the printed
/// Flashback {G} alt-cost are built by the factory (mirrors
/// <see cref="ShatterFactory"/> for the destroy mode and
/// <see cref="PastInFlamesFactory"/> for the printed-flashback alt-cost).
///
/// Covers:
///   - Card shape + dispatch ({1}{R}, Red, Instant).
///   - SpellDefinition shape: one 1..1 "target artifact" request.
///   - Destroys a target artifact → graveyard (CR 701.7).
///   - No-op against a non-artifact target (CR 608.2b).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
///   - Printed Flashback {G} alt-cost helper produces a usable cost.
/// </summary>
[Trait("Color", "R")]
public class AncientGrudgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ───────────────────────────────────────────────────

    [Fact]
    public void AncientGrudge_HasInstantShape_Red_AtCost1R()
    {
        var card = AncientGrudgeFactory.Create(_alice);

        card.Name.Should().Be("Ancient Grudge");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{R} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NameAndCosts_AreScryfallExact()
    {
        AncientGrudgeFactory.CardName.Should().Be("Ancient Grudge");
        AncientGrudgeFactory.PrintedManaCost.Should().Be("{1}{R}");
        AncientGrudgeFactory.FlashbackManaCost.Should().Be("{G}");
    }
    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_ExposesSingleArtifactTargetRequest()
    {
        var def = AncientGrudgeFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
    }

    // ── Destroy target artifact ───────────────────────────────────────────────

    [Fact]
    public void DestroysTargetArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        ResolveAgainst(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Ancient Grudge destroys the target artifact (CR 701.7)");
    }

    [Fact]
    public void TargetCreature_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        ResolveAgainst(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Ancient Grudge destroys artifacts only, not creatures (CR 608.2b)");
    }

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        ResolveAgainst(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // ── Printed Flashback {G} alt-cost ────────────────────────────────────────

    [Fact]
    public void GetFlashbackAlternativeCost_IsCastableFromOwnGraveyard()
    {
        var grudge = AncientGrudgeFactory.Create(_alice);
        grudge.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(grudge);

        var alt = AncientGrudgeFactory.GetFlashbackAlternativeCost();

        alt.CanCastFor(grudge, _alice).Should().BeTrue(
            "Ancient Grudge in its own graveyard is castable via the printed Flashback {G} alt-cost");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ResolveAgainst(Permanent target)
    {
        var def = AncientGrudgeFactory.BuildDefinition(o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private Artifact NewControlledArtifact(Player owner, string name, string cost)
    {
        var card = new Artifact(name, cost);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private Creature NewControlledCreature(Player owner, string name, string cost, int p, int t)
    {
        var card = new Creature(name, cost, p, t);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
