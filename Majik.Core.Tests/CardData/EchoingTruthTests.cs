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
/// Tests for Echoing Truth (Ravnica: City of Guilds, {1}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Return target nonland permanent and all other permanents with the
///    same name as that permanent to their owners' hands."
///
/// The bounce analogue of Maelstrom Pulse — same "target nonland permanent +
/// same-name sweep" shape, but returns to owners' hands (CR 701.10) instead of
/// destroying.
///
/// Covers:
///   - Card identity (Instant, {1}{U}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "target nonland permanent"
///     request, no modes, no variable X, BotIntent.Bounce.
///   - Resolve: bounces a creature (CR 701.10).
///   - Resolve: bounces a noncreature permanent (artifact).
///   - Resolve: bounces ALL same-name permanents across every battlefield,
///     each to ITS OWN owner's hand.
///   - Resolve: differently-named permanents are left untouched.
///   - Resolve: a land may NOT be targeted (nonland-permanent restriction).
///   - Resolve: off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class EchoingTruthTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EchoingTruth_IsInstant_AtCost1U()
    {
        var card = EchoingTruthFactory.Create(_alice);

        card.Name.Should().Be("Echoing Truth");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EchoingTruth()
    {
        var card = NamedCardFactory.Create("Echoing Truth", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Echoing Truth");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void EchoingTruth_Definition_HasSingleNonlandPermanentTarget()
    {
        var def = EchoingTruthFactory.BuildDefinition(
            new[] { _alice, _bob }, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("nonland permanent");
        tr.Intent.Should().Be(BotIntent.Bounce);
    }

    // -----------------------------------------------------------------------
    // Resolve — bounces the target permanent (any nonland type)
    // -----------------------------------------------------------------------

    [Fact]
    public void EchoingTruth_BouncesCreature_ToOwnersHand()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Hand,
            "Echoing Truth returns the targeted nonland permanent to its owner's hand (CR 701.10)");
        _bob.Zones.Hand.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void EchoingTruth_BouncesArtifact_ToOwnersHand()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Hand,
            "Echoing Truth returns any nonland permanent type, including artifacts");
        _bob.Zones.Hand.GetCards().Should().Contain(artifact);
    }

    // -----------------------------------------------------------------------
    // Resolve — the same-name sweep, each to its own owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void EchoingTruth_BouncesAllSameNamePermanents_EachToItsOwnOwnersHand()
    {
        // Two copies of the same creature owned by Bob, plus a third owned by
        // Alice, plus an unrelated permanent that must stay on the battlefield.
        var target = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var sameNameBobsOther = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var sameNameAlices = NewControlledCreature(_alice, "Goblin Guide", "{R}");
        var bystander = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        Resolve(target);

        // CR 701.10 — the target and every OTHER same-name permanent
        // (regardless of controller) are returned, each to ITS OWN owner's hand.
        target.Zone.Should().Be(ZoneType.Hand);
        sameNameBobsOther.Zone.Should().Be(ZoneType.Hand);
        sameNameAlices.Zone.Should().Be(ZoneType.Hand,
            "the same-name sweep ignores controller — even the caster's own copy is bounced");

        _bob.Zones.Hand.GetCards().Should().Contain(target);
        _bob.Zones.Hand.GetCards().Should().Contain(sameNameBobsOther);
        _alice.Zones.Hand.GetCards().Should().Contain(sameNameAlices,
            "each permanent goes to ITS OWN owner's hand (owners' hands, plural)");

        // The differently-named permanent is untouched.
        bystander.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bystander);
    }

    [Fact]
    public void EchoingTruth_LeavesDifferentlyNamedPermanents_Alone()
    {
        var target = NewControlledArtifact(_bob, "Sol Ring", "{1}");
        var other = NewControlledArtifact(_bob, "Mind Stone", "{2}");

        Resolve(target);

        target.Zone.Should().Be(ZoneType.Hand);
        other.Zone.Should().Be(ZoneType.Battlefield,
            "only permanents sharing the target's name are swept");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets
    // -----------------------------------------------------------------------

    [Fact]
    public void EchoingTruth_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged — CR 608.2b illegal target → no-op. No same-name
        // sweep happens because the target itself was illegal at resolution.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void EchoingTruth_LandTarget_DoesNothing()
    {
        // The target must be a NONLAND permanent — a land is an illegal target
        // and the spell does nothing (CR 608.2b).
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "Echoing Truth cannot target a land — illegal target, no-op (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = EchoingTruthFactory.BuildDefinition(
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
