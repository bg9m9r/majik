using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MyrEnforcerFactory"/>.
///
/// Card: Myr Enforcer — Artifact Creature — Myr {7} 4/4 (Mirrodin).
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)"
///
/// Covers:
///   - Identity, NamedCardFactory dispatch, Affinity reduction at 0 / 4
///     / 7 / 10 artifact counts (the headline floor-to-zero "play it for
///     free at seven artifacts" case is the whole reason the card sees
///     Modern play).
/// </summary>
public class MyrEnforcerTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void MyrEnforcer_Identity()
    {
        var c = MyrEnforcerFactory.Create(_alice);

        c.Name.Should().Be("Myr Enforcer");
        c.ManaCost.Should().Be("{7}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue("Myr is the printed creature subtype");
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MyrEnforcer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Myr Enforcer", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Myr Enforcer");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
        c.Abilities.OfType<KeywordAbility>().Should().ContainSingle(k => k.Keyword == "Affinity");
    }

    [Fact]
    public void Affinity_NoArtifacts_FullSeven()
    {
        var enforcer = MyrEnforcerFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(enforcer);
        enforcer.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(enforcer, _alice);
        effective.Generic.Should().Be(7);
        effective.TotalValue.Should().Be(7);
    }

    [Fact]
    public void Affinity_FourArtifacts_GenericThree()
    {
        var enforcer = MyrEnforcerFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(enforcer);
        enforcer.SetZone(ZoneType.Hand);

        for (var i = 0; i < 4; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(enforcer, _alice);
        effective.Generic.Should().Be(3, "{7} reduced by 4 → {3}");
    }

    [Fact]
    public void Affinity_SevenArtifacts_FreeCast()
    {
        // The headline interaction: seven artifacts on the battlefield
        // pays for Myr Enforcer outright. This is THE Affinity dream.
        var enforcer = MyrEnforcerFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(enforcer);
        enforcer.SetZone(ZoneType.Hand);

        for (var i = 0; i < 7; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(enforcer, _alice);
        effective.Generic.Should().Be(0, "{7} reduced by 7 → {0} (free)");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void Affinity_TenArtifacts_FloorAtZero()
    {
        var enforcer = MyrEnforcerFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(enforcer);
        enforcer.SetZone(ZoneType.Hand);

        for (var i = 0; i < 10; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(enforcer, _alice);
        effective.Generic.Should().Be(0, "floor-at-zero (CR 117.7c) — never negative");
    }
}
