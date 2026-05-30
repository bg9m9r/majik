using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="SealOfCleansingFactory"/> — Enchantment {1}{W}.
///
/// Oracle text (Scryfall, verified):
///   "Sacrifice this enchantment: Destroy target artifact or enchantment."
///
/// Chassis: a single self-sacrifice <see cref="ActivatedAbility"/> carrying a
/// 1..1 "target artifact or enchantment" <see cref="TargetRequest"/> — same
/// shape as Pyrite Spellbomb's sacrifice-self activated ability. The
/// destroy-target-artifact-or-enchantment resolution mirrors Disenchant:
/// re-check zone + type at resolution (CR 608.2b) then destroy via
/// <see cref="Majik.Core.Primitives.Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible /
/// regeneration shields are honoured.
///
/// Covers:
/// - Identity (Enchantment, {1}{W}, White, owner/controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: one <see cref="ActivatedAbility"/> with a Sacrifice cost
///   and a single 1..1 "artifact or enchantment" target request, no mana/tap.
/// - Resolution destroys a target artifact → graveyard (CR 701.7).
/// - Resolution destroys a target enchantment → graveyard (CR 701.7).
/// - The Seal itself is sacrificed (cost paid) on resolution.
/// - No-op on an illegal resolution target: wrong type (creature) / off
///   battlefield (CR 608.2b), but the Seal is still sacrificed.
/// </summary>
public class SealOfCleansingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SealOfCleansing_IsWhiteEnchantment_AtCost1W()
    {
        var seal = SealOfCleansingFactory.Create(_alice);

        seal.Name.Should().Be("Seal of Cleansing");
        seal.ManaCost.Should().Be("{1}{W}");
        seal.HasType(CardType.Enchantment).Should().BeTrue();
        CardColors.GetColors(seal).Should().Contain(ManaColor.White);
        seal.ManaCostValue.TotalValue.Should().Be(2);
        seal.Owner.Should().BeSameAs(_alice);
        seal.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SealOfCleansing()
    {
        var card = NamedCardFactory.Create("Seal of Cleansing", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Seal of Cleansing");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HasSingleSacrificeAbility_WithOneArtifactOrEnchantmentTarget()
    {
        var seal = SealOfCleansingFactory.Create(_alice);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the only cost is sacrificing the Seal");
        ability.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Tap,
                "Seal of Cleansing has no tap cost");
        ability.Costs.OfType<ManaCostCost>()
            .Should().BeEmpty("Seal of Cleansing's ability has no mana cost");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().ContainAny("artifact", "enchantment");
    }

    // -----------------------------------------------------------------------
    // Destroy target → graveyard + Seal sacrificed
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_DestroysTargetArtifact_AndSacrificesSeal()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        var seal = SealOfCleansingFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifact } });
        ability.Resolve();

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Seal of Cleansing destroys the target artifact (CR 701.7)");

        _alice.Zones.Graveyard.GetCards().Should().Contain(seal);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(seal);
        seal.Zone.Should().Be(ZoneType.Graveyard, because: "the Seal sacrifices itself");
    }

    [Fact]
    public void Activate_DestroysTargetEnchantment_MovesToGraveyard()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        var seal = SealOfCleansingFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { enchantment } });
        ability.Resolve();

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Seal of Cleansing destroys the target enchantment (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // Illegal resolution target → no destroy, Seal still sacrificed
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_TargetCreature_DoesNotDestroy_ButSealStillSacrificed()
    {
        // A creature is not a legal target; if somehow resolved against one
        // (type changed after targeting), CR 608.2b → no destroy.
        var bears = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var seal = SealOfCleansingFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bears } });
        ability.Resolve();

        bears.Zone.Should().Be(ZoneType.Battlefield,
            because: "Seal of Cleansing destroys artifacts/enchantments only (CR 608.2b)");
        seal.Zone.Should().Be(ZoneType.Graveyard,
            because: "the cost was paid, so the Seal is sacrificed regardless");
    }

    [Fact]
    public void Activate_TargetLeftBattlefield_DoesNotDestroy_ButSealStillSacrificed()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        var seal = SealOfCleansingFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifact } });
        ability.Resolve();

        // Still in graveyard (it was already there) — no exception, no double-move.
        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
        seal.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

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
        else if (typeof(T) == typeof(Enchantment))
        {
            card = (T)(ICard)new Enchantment(name, cost);
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
}
