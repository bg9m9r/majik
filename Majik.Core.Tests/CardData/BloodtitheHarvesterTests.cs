using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="BloodtitheHarvesterFactory"/> — Creature {1}{B/R}
/// (Innistrad: Crimson Vow).
///
/// Oracle:
///   "When Bloodtithe Harvester enters, create a Blood token.
///    {1}, Sacrifice a Blood token: Bloodtithe Harvester deals 2 damage
///    to any target."
///
/// Covers:
/// - Identity (Vampire 3/2 at {1}{B/R}).
/// - NamedCardFactory dispatch.
/// - Two abilities (ETB trigger + activated damage).
/// - ETB trigger fires and spawns one Blood token under controller.
/// - Activated ability shape: {1} mana cost + 1-of-1 any-target request.
/// - Activated ability resolution: sacs a Blood, deals 2 damage to the
///   chosen player target.
/// - Activated ability resolution: deals damage even with no Blood
///   (v1 inline-sac is best-effort; ability still pumps damage so the
///   activation isn't outright lost — documented gap).
/// </summary>
public class BloodtitheHarvesterTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Bloodtithe_Identity_Vampire_3_2_AtCost1BR()
    {
        var b = BloodtitheHarvesterFactory.Create(_alice);

        b.Name.Should().Be("Bloodtithe Harvester");
        b.ManaCost.Should().Be("{1}{B/R}");
        b.HasType(CardType.Creature).Should().BeTrue();
        b.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        b.BasePower.Should().Be(3);
        b.BaseToughness.Should().Be(2);
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Bloodtithe_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Bloodtithe Harvester", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Bloodtithe Harvester");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
    }

    [Fact]
    public void Bloodtithe_HasOneTriggeredAndOneActivatedAbility()
    {
        var b = BloodtitheHarvesterFactory.Create(_alice);

        b.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB Blood-token trigger");
        b.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {1}, sac-a-Blood damage activated ability");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — creates Blood
    // -----------------------------------------------------------------------

    [Fact]
    public void Bloodtithe_ETB_CreatesOneBloodTokenUnderController()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var b = BloodtitheHarvesterFactory.Create(_alice, zones, bus, triggers);

        // ETB via zone service — Hand → Battlefield publishes a
        // CardMovedEvent that the OnEnterBattlefieldSelf trigger picks up.
        _alice.Zones.Hand.AddCard(b);
        b.SetZone(ZoneType.Hand);
        zones.MoveCardTo(b, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var bloods = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.HasSubtype(CardSubtype.Blood))
            .ToList();
        bloods.Should().HaveCount(1, "ETB creates exactly one Blood token");
        bloods[0].Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Bloodtithe_DamageAbility_HasOneManaCost_AndOneAnyTarget()
    {
        var b = BloodtitheHarvesterFactory.Create(_alice);

        var dmg = b.Abilities.OfType<ActivatedAbility>().Single();

        dmg.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Cost.Generic == 1,
                "the activated mana cost is one generic mana");

        dmg.TargetRequests.Should().HaveCount(1);
        dmg.TargetRequests[0].MinTargets.Should().Be(1);
        dmg.TargetRequests[0].MaxTargets.Should().Be(1);
        dmg.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Bloodtithe_Activate_WithBloodAvailable_SacsBlood_AndDealsTwoToPlayer()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var b = BloodtitheHarvesterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(b);
        b.SetZone(ZoneType.Battlefield);

        // Seed a Blood token onto Alice's battlefield.
        var blood = Majik.Core.Tokens.TokenFactory.CreateBlood(_alice, zones);
        _alice.Zones.Battlefield.GetCards().Should().Contain(blood);

        var dmg = b.Abilities.OfType<ActivatedAbility>().Single();
        // Pick Bob as the any-target.
        dmg.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        // Resolve the effect directly (cost-payment side of {1} is owned
        // by the activator; we exercise the resolve body's inline Blood-
        // sac + damage halves).
        foreach (var fx in dmg.Effects) fx.Execute();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(blood,
            "the inline sac moved Blood to the graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(blood);

        _bob.LifeTotal.Should().Be(18, "2 damage to Bob");
    }

    [Fact]
    public void Bloodtithe_Activate_NoBloodAvailable_DamageStillResolves()
    {
        // v1 inline-sac is best-effort: no Blood → no sac, but the damage
        // half still resolves. Documented gap on the factory (no subtype-
        // sac cost primitive yet; activator is expected to gate on Blood
        // availability themselves).
        var b = BloodtitheHarvesterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(b);
        b.SetZone(ZoneType.Battlefield);

        var dmg = b.Abilities.OfType<ActivatedAbility>().Single();
        dmg.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var fx in dmg.Effects) fx.Execute();

        _bob.LifeTotal.Should().Be(18,
            "damage half still resolves (best-effort sac per documented gap)");
    }
}
