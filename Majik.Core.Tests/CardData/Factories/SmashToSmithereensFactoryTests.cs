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
/// Tests for Smash to Smithereens ({1}{R}, Instant).
///
/// Oracle text:
///   "Destroy target artifact. Smash to Smithereens deals 3 damage to
///   that artifact's controller."
///
/// Covers:
///   - Card shape + dispatch ({1}{R}, Red, Instant).
///   - SpellDefinition shape: single 1..1 "target artifact" request.
///   - Destroys a target artifact → graveyard (CR 701.7).
///   - Deals 3 damage to the artifact's controller after destruction (CR 608.2b).
///   - Controller is captured BEFORE destruction so the damage target is stable.
///   - No-op (no destroy, no damage) if target is not an Artifact at resolution
///     (CR 608.2b illegal-target gate).
///   - No-op (no destroy, no damage) if target left the battlefield before
///     resolution (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class SmashToSmithereensFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SmashToSmithereens_HasInstantShape_Red_AtCost1R()
    {
        var card = SmashToSmithereensFactory.Create(_alice);

        card.Name.Should().Be("Smash to Smithereens");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SpellDefinition_DeclaresSingleTargetArtifactRequest()
    {
        var def = SmashToSmithereensFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
    }

    // -----------------------------------------------------------------------
    // Destroy artifact → graveyard + 3 damage to controller
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");
        var bobLifeBefore = _bob.LifeTotal;

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Smash to Smithereens destroys target artifact (CR 701.7)");
    }

    [Fact]
    public void DestroysArtifact_DealsThreeDamageToItsController()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        _bob.LifeTotal.Should().Be(17,
            because: "Smash to Smithereens deals 3 damage to the artifact's controller");
    }

    [Fact]
    public void DestroysArtifact_NoDamageToNonController()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        // Alice (the caster) should take no damage.
        _alice.LifeTotal.Should().Be(20,
            because: "damage goes to the artifact's controller, not the spell's caster");
    }

    // -----------------------------------------------------------------------
    // No-op: wrong permanent type (non-artifact at resolution) — CR 608.2b
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetCreature_DoesNothingAndDealsNoDamage()
    {
        // A creature is not an artifact (unless it also has the Artifact type).
        // If resolution finds a non-artifact, CR 608.2b → no destroy, no damage.
        var creature = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobLifeBefore = _bob.LifeTotal;

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Smash to Smithereens targets artifact only (CR 608.2b)");
        _bob.LifeTotal.Should().Be(bobLifeBefore,
            because: "CR 608.2b — illegal target → whole spell does nothing, no damage");
    }

    // -----------------------------------------------------------------------
    // No-op: target left the battlefield before resolution — CR 608.2b
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothingAndDealsNoDamage()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
        _bob.LifeTotal.Should().Be(20,
            because: "CR 608.2b — illegal target → no damage dealt either");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(ICard target)
    {
        var def = SmashToSmithereensFactory.BuildDefinition(o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Artifact NewControlledArtifact(Player owner, string name, string cost)
    {
        var card = new Artifact(name, cost);
        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost,
        int power, int toughness)
    {
        var card = new Creature(name, cost, power, toughness);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
