using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AcidicSlimeFactory"/> — Creature — Ooze {3}{G}{G} 2/2
/// (Magic 2010 / reprints) with Deathtouch and a single mandatory ETB trigger:
///   "When this creature enters, destroy target artifact, enchantment, or
///    land."
///
/// Covers:
///   - Card identity (Creature, {3}{G}{G}, 2/2, Ooze subtype, owner /
///     controller).
///   - Deathtouch keyword marker (CR 702.2).
///   - Single ETB <see cref="TriggeredAbility"/> shape: 1..1
///     "target artifact, enchantment, or land" request, battlefield active
///     zone.
///   - Resolve: agent-set artifact target → destroyed.
///   - Resolve: agent-set enchantment target → destroyed.
///   - Resolve: agent-set land target → destroyed (the extension over the
///     Reclamation Sage analogue).
///   - Resolve: agent-set creature target (illegal pick) → no destroy.
///   - Resolve: target left the battlefield → no destroy (CR 608.2b).
///   - Resolve: no agent target + legal land on own battlefield →
///     deterministic fallback destroys it (single-arg dispatcher posture).
/// </summary>
[Trait("Color", "G")]
public class AcidicSlimeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Permanent card, Player owner)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void AcidicSlime_Identity_Creature_Ooze_2_2_At3GG()
    {
        var slime = AcidicSlimeFactory.Create(_alice);

        slime.Name.Should().Be("Acidic Slime");
        slime.ManaCost.Should().Be("{3}{G}{G}");
        slime.HasType(CardType.Creature).Should().BeTrue();
        slime.HasSubtype(CardSubtype.Ooze).Should().BeTrue();
        slime.BasePower.Should().Be(2);
        slime.BaseToughness.Should().Be(2);
        slime.Owner.Should().BeSameAs(_alice);
        slime.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AcidicSlime_HasDeathtouch()
    {
        var slime = AcidicSlimeFactory.Create(_alice);

        // CR 702.2 — Deathtouch keyword marker consumed by combat.
        CombatAbilities.HasDeathtouch(slime).Should().BeTrue();
    }

    [Fact]
    public void AcidicSlime_HasSingleEtbTrigger_WithOneArtifactEnchantmentOrLandTarget()
    {
        var slime = AcidicSlimeFactory.Create(_alice);

        var triggers = slime.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should()
            .Contain("artifact").And.Contain("enchantment").And.Contain("land");

        // ETB lives on the battlefield (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_AgentSetArtifactTarget_DestroysIt()
    {
        var trinket = new Artifact("Bob's Trinket", "{2}");
        PutOnBattlefield(trinket, _bob);

        var slime = AcidicSlimeFactory.Create(_alice);
        PutOnBattlefield(slime, _alice);

        var etb = slime.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });
        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(trinket);
    }

    [Fact]
    public void Resolve_AgentSetEnchantmentTarget_DestroysIt()
    {
        var aura = new Enchantment("Bob's Aura", "{1}{W}");
        PutOnBattlefield(aura, _bob);

        var slime = AcidicSlimeFactory.Create(_alice);
        PutOnBattlefield(slime, _alice);

        var etb = slime.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aura } });
        foreach (var effect in etb.Effects) effect.Execute();

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void Resolve_AgentSetLandTarget_DestroysIt()
    {
        // The land target is the extension over the Reclamation Sage analogue.
        var land = new Land("Bob's Island");
        PutOnBattlefield(land, _bob);

        var slime = AcidicSlimeFactory.Create(_alice);
        PutOnBattlefield(slime, _alice);

        var etb = slime.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { land } });
        foreach (var effect in etb.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(land);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void Resolve_AgentSetCreatureTarget_DestroyNoOp()
    {
        // Creature is not artifact/enchantment/land — resolution-time gate
        // makes the destroy a no-op (CR 608.2b).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(bear, _bob);

        var slime = AcidicSlimeFactory.Create(_alice);
        PutOnBattlefield(slime, _alice);

        var etb = slime.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_TargetLeftBattlefield_DestroyNoOp()
    {
        var trinket = new Artifact("Trinket", "{1}");
        PutOnBattlefield(trinket, _bob);

        var slime = AcidicSlimeFactory.Create(_alice);
        PutOnBattlefield(slime, _alice);

        var etb = slime.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Trinket leaves the battlefield between trigger pick and resolution.
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(trinket);
    }

    [Fact]
    public void Resolve_NoTarget_OwnLandOnBattlefield_FallbackDestroysIt()
    {
        // No agent set ChosenTargets. The deterministic fallback should pick
        // the first legal artifact / enchantment / land on the controller's
        // battlefield (single-arg dispatcher posture).
        var ownLand = new Land("Alice's Forest");
        PutOnBattlefield(ownLand, _alice);

        var slime = AcidicSlimeFactory.Create(_alice);
        PutOnBattlefield(slime, _alice);

        var etb = slime.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        ownLand.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(ownLand);
    }
}
