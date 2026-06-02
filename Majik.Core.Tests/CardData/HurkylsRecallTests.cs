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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Hurkyl's Recall (Antiquities, {1}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "Return all artifacts target player owns to their hand."
///
/// The mass-bounce analogue of Echoing Truth — same return-to-hand routine
/// (CR 701.10), but instead of a same-name sweep around a targeted permanent
/// it sweeps EVERY artifact a TARGET PLAYER owns (the player-target shape is
/// mirrored from Mind Rot). Ownership-based, not control-based: an artifact
/// the target owns but an opponent currently controls is still returned, and
/// it goes to the target player's hand because the target IS the owner
/// (CR 109.5 / 701.10 — "return to its owner's hand").
///
/// Covers:
///   - Card identity (Instant, {1}{U}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "target player" request, no modes,
///     no variable X.
///   - Resolve: returns every artifact the target owns to their hand.
///   - Resolve: leaves nonartifact permanents (creatures, lands) untouched.
///   - Resolve: leaves artifacts owned by OTHER players untouched.
///   - Resolve: an artifact the target OWNS but an opponent CONTROLS is still
///     returned, to the target's (owner's) hand.
///   - Resolve: artifact creatures (an artifact that is also a creature) ARE
///     returned (CR 301 — they are artifacts).
///   - Resolve: illegal target (not a Player) → no-op (CR 608.2b).
/// </summary>
public class HurkylsRecallTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HurkylsRecall_IsInstant_AtCost1U()
    {
        var card = HurkylsRecallFactory.Create(_alice);

        card.Name.Should().Be("Hurkyl's Recall");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HurkylsRecall()
    {
        var card = NamedCardFactory.Create("Hurkyl's Recall", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Hurkyl's Recall");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HurkylsRecall_Definition_HasSinglePlayerTarget()
    {
        var def = HurkylsRecallFactory.BuildDefinition(
            new[] { _alice, _bob }, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("player");
    }

    // -----------------------------------------------------------------------
    // Resolve — returns every artifact the target owns
    // -----------------------------------------------------------------------

    [Fact]
    public void HurkylsRecall_ReturnsAllArtifactsTargetOwns_ToTheirHand()
    {
        var ring = NewControlledArtifact(_bob, "Sol Ring", "{1}");
        var stone = NewControlledArtifact(_bob, "Mind Stone", "{2}");

        Resolve(_bob);

        ring.Zone.Should().Be(ZoneType.Hand,
            "Hurkyl's Recall returns every artifact the target owns to their hand (CR 701.10)");
        stone.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(new ICard[] { ring, stone });
        _bob.Zones.Battlefield.GetCards().Should().NotContain(new ICard[] { ring, stone });
    }

    [Fact]
    public void HurkylsRecall_LeavesNonartifactPermanents_Alone()
    {
        var ring = NewControlledArtifact(_bob, "Sol Ring", "{1}");
        var goyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        Resolve(_bob);

        ring.Zone.Should().Be(ZoneType.Hand);
        goyf.Zone.Should().Be(ZoneType.Battlefield,
            "only artifacts are returned — creatures stay (CR 301)");
        land.Zone.Should().Be(ZoneType.Battlefield,
            "only artifacts are returned — lands stay");
    }

    [Fact]
    public void HurkylsRecall_LeavesArtifactsOwnedByOtherPlayers_Alone()
    {
        var bobsRing = NewControlledArtifact(_bob, "Sol Ring", "{1}");
        var alicesRing = NewControlledArtifact(_alice, "Sol Ring", "{1}");

        Resolve(_bob);

        bobsRing.Zone.Should().Be(ZoneType.Hand);
        alicesRing.Zone.Should().Be(ZoneType.Battlefield,
            "only artifacts the TARGET player owns are returned");
        _alice.Zones.Battlefield.GetCards().Should().Contain(alicesRing);
    }

    [Fact]
    public void HurkylsRecall_ReturnsArtifactTargetOwnsButOpponentControls_ToOwnersHand()
    {
        // Bob owns the artifact but Alice currently controls it (e.g. it was
        // stolen). Hurkyl's Recall keys off OWNERSHIP, and return-to-hand goes
        // to the OWNER's hand (CR 701.10) — so it returns to Bob's hand.
        var stolen = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(stolen);
        stolen.SetZone(ZoneType.Battlefield);

        Resolve(_bob);

        stolen.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(stolen,
            "ownership, not control, decides both the sweep and the destination hand (CR 701.10)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(stolen);
    }

    [Fact]
    public void HurkylsRecall_ReturnsArtifactCreatures()
    {
        // An artifact creature is an artifact (CR 301) and is returned.
        var golem = new Creature("Memnite", "{0}", 1, 1);
        golem.AddCardType(CardType.Artifact);
        golem.SetOwner(_bob);
        golem.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(golem);
        golem.SetZone(ZoneType.Battlefield);

        golem.HasType(CardType.Artifact).Should().BeTrue("test fixture must be an artifact creature");

        Resolve(_bob);

        golem.Zone.Should().Be(ZoneType.Hand,
            "an artifact creature is an artifact and is returned (CR 301)");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal target
    // -----------------------------------------------------------------------

    [Fact]
    public void HurkylsRecall_NonPlayerTarget_DoesNothing()
    {
        var ring = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        // Resolve with a non-Player token — CR 608.2b: illegal target, no-op.
        Resolve("not-a-player");

        ring.Zone.Should().Be(ZoneType.Battlefield,
            "a non-Player target is illegal — the spell does nothing (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = HurkylsRecallFactory.BuildDefinition(
            allPlayers: new[] { _alice, _bob },
            targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Artifact NewControlledArtifact(Player owner, string name, string cost)
    {
        var a = new Artifact(name, cost)
        {
            Owner = owner,
            Controller = owner,
        };
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}
