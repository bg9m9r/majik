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
/// Tests for Pillage (Alliances / various reprints, {1}{R}{R}, Sorcery).
///
/// Oracle text:
///   "Destroy target artifact or land. It can't be regenerated."
///
/// CR 701.7 — Destroy. The "It can't be regenerated" rider is honoured via
/// <see cref="ZoneMoveReason.DestroyNoRegeneration"/> (indestructible
/// CR 702.12 still cancels the destroy, but any active regeneration shield
/// CR 701.15 is bypassed rather than consumed).
///
/// Covers:
///   - Card shape + dispatch ({1}{R}{R}, Red, Sorcery).
///   - SpellDefinition shape: single 1..1 "target artifact or land" request.
///   - Destroys a target artifact → graveyard (CR 701.7).
///   - Destroys a target land → graveyard (CR 701.7).
///   - No-op if target is a creature (wrong type — CR 608.2b illegal target).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
/// </summary>
public class PillageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Pillage_HasSorceryShape_Red_AtCost1RR()
    {
        var card = PillageFactory.Create(_alice);

        card.Name.Should().Be("Pillage");
        card.ManaCost.Should().Be("{1}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{R}{R} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsPillageShape()
    {
        var dispatched = NamedCardFactory.Create("Pillage", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Pillage");
        dispatched.ManaCost.Should().Be("{1}{R}{R}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetArtifactOrLandRequest()
    {
        var def = PillageFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().ContainAny("artifact", "land");
    }

    // -----------------------------------------------------------------------
    // Destroy artifact → graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Pillage destroys target artifact (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // Destroy land → graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysLand_MovesToGraveyard()
    {
        var land = NewControlledLand(_bob, "Mountain");

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Graveyard,
            because: "Pillage destroys target land (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // No-op: wrong permanent type (creature)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetCreature_DoesNothing()
    {
        // A creature is not a legal target for Pillage. If somehow resolved
        // against one (e.g. type changed after targeting), CR 608.2b → no-op.
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Pillage targets artifact or land only (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // No-op: target left the battlefield before resolution (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(ICard target)
    {
        var def = PillageFactory.BuildDefinition(o => o);

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

    private static T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private static Land NewControlledLand(Player owner, string name)
    {
        var land = new Land(name);
        land.SetOwner(owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }
}
