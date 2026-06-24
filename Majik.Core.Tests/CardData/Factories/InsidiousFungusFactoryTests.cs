using System.Collections.Generic;
using System.Linq;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="InsidiousFungusFactory"/> — Insidious Fungus (Modern
/// Horizons 3, {G}). 1/2 Creature — Fungus. Oracle text (verified against
/// Scryfall 2026-06-24):
///   "{2}, Sacrifice this creature: Choose one —
///     • Destroy target artifact.
///     • Destroy target enchantment.
///     • Draw a card. Then you may put a land card from your hand onto the
///       battlefield tapped."
///
/// The three modes are modelled as three separate <see cref="ActivatedAbility"/>s
/// sharing the {2} + sacrifice-self cost (Goblin Cratermaker pattern). Covers
/// the card's UNIQUE behaviour: identity, the three-mode ability shape, the two
/// destroy modes (with resolution-time legality), and the draw + optional
/// land-into-play mode.
/// </summary>
[Trait("Color", "G")]
public class InsidiousFungusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ActivatedAbility ArtifactMode(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1 &&
                         a.TargetRequests[0].Description == "target artifact");

    private static ActivatedAbility EnchantmentMode(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1 &&
                         a.TargetRequests[0].Description == "target enchantment");

    private static ActivatedAbility DrawLandMode(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void InsidiousFungus_IsGreenFungus_OneTwo_ForG()
    {
        var f = InsidiousFungusFactory.Create(_alice);

        f.HasType(CardType.Creature).Should().BeTrue();
        f.Name.Should().Be("Insidious Fungus");
        f.ManaCost.Should().Be("{G}");
        f.Power.Should().Be(1);
        f.Toughness.Should().Be(2);
        f.HasSubtype(CardSubtype.Fungus).Should().BeTrue();
        f.Owner.Should().BeSameAs(_alice);
        f.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HasThreeActivatedAbilities_TwoTargetedDestroys_OneTargetlessDraw()
    {
        var f = InsidiousFungusFactory.Create(_alice);
        var activated = f.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().HaveCount(3);
        activated.Should().ContainSingle(a =>
            a.TargetRequests.Count == 1 && a.TargetRequests[0].Description == "target artifact");
        activated.Should().ContainSingle(a =>
            a.TargetRequests.Count == 1 && a.TargetRequests[0].Description == "target enchantment");
        activated.Should().ContainSingle(a => a.TargetRequests.Count == 0);
    }

    [Fact]
    public void EveryMode_SharesTwoGenericManaPlusSacrificeCostShape()
    {
        var f = InsidiousFungusFactory.Create(_alice);
        foreach (var ability in f.Abilities.OfType<ActivatedAbility>())
        {
            ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Cost.TotalValue == 2,
                "{2} generic mana payment");
            ability.Costs.OfType<AdditionalCost>()
                .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                    "self-sacrifice");
        }
    }

    // -----------------------------------------------------------------------
    // Mode A — Destroy target artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtifactMode_DestroysTargetArtifact_AndSacrificesSelf()
    {
        var f = InsidiousFungusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(f);
        f.SetZone(ZoneType.Battlefield);

        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_bob);
        solRing.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(solRing);
        solRing.SetZone(ZoneType.Battlefield);

        var mode = ArtifactMode(f);
        mode.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { solRing } });
        mode.Resolve();

        solRing.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(solRing);
        f.Zone.Should().Be(ZoneType.Graveyard, "Insidious Fungus sacrifices itself");
        _alice.Zones.Graveyard.GetCards().Should().Contain(f);
    }

    [Fact]
    public void ArtifactMode_IllegalOnEnchantment_NoDestroy_ButSelfStillSacs()
    {
        // CR 608.2b — wrong-type target → effect does nothing; the activation
        // cost was already paid, so the sacrifice still resolves.
        var f = InsidiousFungusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(f);
        f.SetZone(ZoneType.Battlefield);

        var ench = new Enchantment("Some Enchantment", "{2}");
        ench.SetOwner(_bob);
        ench.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(ench);
        ench.SetZone(ZoneType.Battlefield);

        var mode = ArtifactMode(f);
        mode.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ench } });
        mode.Resolve();

        ench.Zone.Should().Be(ZoneType.Battlefield, "an enchantment isn't a legal target for the artifact mode");
        f.Zone.Should().Be(ZoneType.Graveyard, "fungus sacrificed regardless");
    }

    // -----------------------------------------------------------------------
    // Mode B — Destroy target enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void EnchantmentMode_DestroysTargetEnchantment_AndSacrificesSelf()
    {
        var f = InsidiousFungusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(f);
        f.SetZone(ZoneType.Battlefield);

        var ench = new Enchantment("Oblivion Ring", "{2}{W}");
        ench.SetOwner(_bob);
        ench.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(ench);
        ench.SetZone(ZoneType.Battlefield);

        var mode = EnchantmentMode(f);
        mode.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ench } });
        mode.Resolve();

        ench.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(ench);
        f.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Mode C — Draw a card, then you may put a land from hand tapped
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawLandMode_DrawsACard_PutsLandTapped_AndSacrificesSelf()
    {
        var f = InsidiousFungusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(f);
        f.SetZone(ZoneType.Battlefield);

        // A card to draw + a land in hand (no agent → auto opt-in, first land).
        var drawTarget = new Creature("Topdeck", "{1}{G}", 2, 2);
        drawTarget.SetOwner(_alice);
        _alice.Zones.Library.AddCard(drawTarget);
        drawTarget.SetZone(ZoneType.Library);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var mode = DrawLandMode(f);
        mode.Resolve();

        // CR 121.1 — drew the top card.
        _alice.Zones.Hand.GetCards().Should().Contain(drawTarget);
        // CR 305.9 — land put onto the battlefield tapped (NOT a land drop).
        forest.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeTrue();
        // Self-sacrifice.
        f.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DrawLandMode_NoLandInHand_DrawsOnly_StillSacs()
    {
        var f = InsidiousFungusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(f);
        f.SetZone(ZoneType.Battlefield);

        var drawTarget = new Creature("Topdeck", "{1}{G}", 2, 2);
        drawTarget.SetOwner(_alice);
        _alice.Zones.Library.AddCard(drawTarget);
        drawTarget.SetZone(ZoneType.Library);

        var mode = DrawLandMode(f);
        mode.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(drawTarget);
        // "Then you may put a land" — no land in hand → no-op, no exception.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c => c.HasType(CardType.Land));
        f.Zone.Should().Be(ZoneType.Graveyard);
    }
}
