using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MeteorGolemFactory"/> — Artifact Creature — Golem
/// {7} 3/3 (colorless) with a single ETB trigger:
///   "When this creature enters, destroy target nonland permanent an opponent
///    controls."
///
/// Covers (only the card's UNIQUE behaviour + one identity assert; dispatch +
/// well-formedness are covered globally by CardFactoryContractTests):
///   - Card identity (name, {7}, Artifact Creature — Golem, 3/3).
///   - ETB trigger shape (1..1 "target nonland permanent an opponent
///     controls" request, scoped to the battlefield active zone).
///   - Resolve: opponent's nonland permanent (creature) → destroyed.
///   - Resolve: opponent's artifact → destroyed (any nonland type qualifies).
///   - Resolve: opponent's planeswalker → destroyed.
///   - Resolve: own permanent (illegal pick) → clean no-op (CR 109.1).
///   - Resolve: a LAND target → clean no-op (CR 305 — Land is excluded).
///   - Resolve: target left battlefield → clean no-op (CR 608.2b).
///   - Resolve: no chosen target → clean no-op.
/// </summary>
[Trait("Color", "C")]
public class MeteorGolemFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    private Creature NewGolemOnBattlefield()
    {
        var golem = MeteorGolemFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(golem);
        golem.SetZone(ZoneType.Battlefield);
        return golem;
    }

    private static void Resolve(TriggeredAbility etb, object? target)
    {
        if (target != null)
        {
            etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        }

        foreach (var e in etb.Effects) e.Execute();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MeteorGolem_IsArtifactCreatureGolem_At7_ThreeThree()
    {
        var c = MeteorGolemFactory.Create(_alice);

        c.Name.Should().Be("Meteor Golem");
        c.ManaCost.Should().Be("{7}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MeteorGolem_Etb_HasOpponentNonlandPermanentTargetRequest()
    {
        var c = MeteorGolemFactory.Create(_alice);
        var etb = GetEtb(c);

        etb.TargetRequests.Should().ContainSingle();
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("nonland permanent").And.Contain("opponent");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys an opponent's nonland permanent of any type
    // -----------------------------------------------------------------------

    [Fact]
    public void MeteorGolem_Etb_DestroysOpponentCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var golem = NewGolemOnBattlefield();

        Resolve(GetEtb(golem), bear);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void MeteorGolem_Etb_DestroysOpponentArtifact()
    {
        // The "nonland permanent" wording (vs Chupacabra's "creature") means
        // any nonland permanent type is a legal target — including artifacts.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var golem = NewGolemOnBattlefield();

        Resolve(GetEtb(golem), artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void MeteorGolem_Etb_DestroysOpponentPlaneswalker()
    {
        var pw = new Planeswalker(
            name: "Liliana, the Last Hope",
            manaCost: "{1}{B}{B}",
            startingLoyalty: 3,
            subtypes: new[] { CardSubtype.Liliana })
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var golem = NewGolemOnBattlefield();

        Resolve(GetEtb(golem), pw);

        pw.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets → clean no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void MeteorGolem_Etb_OwnPermanentTarget_NoOp()
    {
        // Golem's controller is Alice; targeting Alice's own permanent
        // violates "an opponent controls" (CR 109.1) — resolution guard no-ops.
        var ownBear = new Creature("Friendly Bears", "{1}{G}", 2, 2);
        ownBear.SetOwner(_alice);
        ownBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownBear);
        ownBear.SetZone(ZoneType.Battlefield);

        var golem = NewGolemOnBattlefield();

        Resolve(GetEtb(golem), ownBear);

        ownBear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ownBear);
    }

    [Fact]
    public void MeteorGolem_Etb_LandTarget_NoOp()
    {
        // A land is NOT a nonland permanent (CR 305) — illegal target, no-op
        // even when controlled by an opponent.
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var golem = NewGolemOnBattlefield();

        Resolve(GetEtb(golem), land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "a land is not a nonland permanent (CR 305) — illegal target, no-op");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void MeteorGolem_Etb_TargetLeftBattlefield_NoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var golem = NewGolemOnBattlefield();
        var etb = GetEtb(golem);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        // Bear bounces between trigger and resolution (CR 608.2b).
        _bob.Zones.Battlefield.RemoveCard(bear);
        _bob.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void MeteorGolem_Etb_NoChosenTarget_NoOp()
    {
        var golem = NewGolemOnBattlefield();
        var etb = GetEtb(golem);

        Action act = () =>
        {
            foreach (var e in etb.Effects) e.Execute();
        };

        act.Should().NotThrow();
    }
}
