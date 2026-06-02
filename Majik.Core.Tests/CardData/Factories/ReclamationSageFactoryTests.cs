using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ReclamationSageFactory"/> — Creature — Elf Shaman
/// {2}{G} 2/1 (Magic 2015 / Modern Horizons 3) with a single ETB trigger:
///   "When Reclamation Sage enters, you may destroy target artifact or
///    enchantment."
///
/// Covers:
///   - Card identity (Creature, {2}{G}, 2/1, Elf + Shaman subtypes,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single ETB <see cref="TriggeredAbility"/> shape: 1..1
///     "target artifact or enchantment" request, battlefield active zone.
///   - Resolve: agent-set artifact target → destroyed.
///   - Resolve: agent-set enchantment target → destroyed.
///   - Resolve: agent-set creature target (illegal pick) → no destroy.
///   - Resolve: target left the battlefield → no destroy (CR 608.2b).
///   - Resolve: no agent target + no legal candidate → clean no-op.
///   - Resolve: no agent target + legal candidate on own battlefield →
///     deterministic fallback destroys it (single-arg dispatcher posture).
/// </summary>
[Trait("Color", "G")]
public class ReclamationSageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ReclamationSage_Identity_Creature_ElfShaman_2_1_At2G()
    {
        var sage = ReclamationSageFactory.Create(_alice);

        sage.Name.Should().Be("Reclamation Sage");
        sage.ManaCost.Should().Be("{2}{G}");
        sage.HasType(CardType.Creature).Should().BeTrue();
        sage.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        sage.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        sage.BasePower.Should().Be(2);
        sage.BaseToughness.Should().Be(1);
        sage.Owner.Should().BeSameAs(_alice);
        sage.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void ReclamationSage_HasSingleEtbTrigger_WithOneArtifactOrEnchantmentTarget()
    {
        var sage = ReclamationSageFactory.Create(_alice);

        var triggers = sage.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should()
            .Contain("artifact").And.Contain("enchantment");

        // ETB lives on the battlefield (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_AgentSetArtifactTarget_DestroysIt()
    {
        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var sage = ReclamationSageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);

        var etb = sage.Abilities.OfType<TriggeredAbility>().Single();
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
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var sage = ReclamationSageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);

        var etb = sage.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aura } });
        foreach (var effect in etb.Effects) effect.Execute();

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void Resolve_AgentSetCreatureTarget_DestroyNoOp()
    {
        // Creature is not artifact or enchantment — resolution-time gate
        // makes the destroy a no-op (CR 608.2b).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var sage = ReclamationSageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);

        var etb = sage.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_TargetLeftBattlefield_DestroyNoOp()
    {
        var trinket = new Artifact("Trinket", "{1}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var sage = ReclamationSageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);

        var etb = sage.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Trinket leaves the battlefield between trigger pick and
        // resolution.
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(trinket);
    }

    [Fact]
    public void Resolve_NoTarget_NoCandidate_IsCleanNoOp()
    {
        // No artifact/enchantment anywhere + no agent-set target. The
        // resolve body should not throw and should leave Alice's
        // graveyard empty.
        var sage = ReclamationSageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);

        var etb = sage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };
        act.Should().NotThrow();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoTarget_OwnArtifactOnBattlefield_FallbackDestroysIt()
    {
        // No agent set ChosenTargets. The deterministic fallback should
        // pick the first legal artifact / enchantment on the controller's
        // battlefield (single-arg dispatcher posture, mirrors Eternal
        // Witness's first-card fallback).
        var ownArtifact = new Artifact("Alice's Trinket", "{1}");
        ownArtifact.SetOwner(_alice);
        ownArtifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownArtifact);
        ownArtifact.SetZone(ZoneType.Battlefield);

        var sage = ReclamationSageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);

        var etb = sage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        ownArtifact.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(ownArtifact);
    }
}
