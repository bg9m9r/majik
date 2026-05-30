using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="DeadlyDisputeFactory"/> — Instant {1}{B}.
///
/// "As an additional cost to cast this spell, sacrifice an artifact or
///  creature. Draw two cards and create a Treasure token."
///
/// Covers:
///   - Identity (Instant, {1}{B}, black, owner / controller) +
///     NamedCardFactory dispatch (built from the embedded JSON definition).
///   - SpellDefinition shape:
///     <see cref="SacrificeAnArtifactOrCreatureAdditionalCost"/> additional
///     cost (CR 601.2f), no modes, no X, no target requests.
///   - Resolve: caster draws two cards (CR 121.1) AND creates one Treasure
///     token (CR 111.10).
///   - Resolve: empty library mid-draw → draws what's available, SBA loss
///     flag set (CR 704.5b); the Treasure is still created.
///   - Cost: sacrifices an artifact when available; sacrifices a creature
///     when no artifact; CanPay false only with neither (CR 601.2f / 117.1).
/// </summary>
public class DeadlyDisputeTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Permanent card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = DeadlyDisputeFactory.Create(_alice);

        card.Name.Should().Be("Deadly Dispute");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DeadlyDispute_IsBlack()
    {
        var card = DeadlyDisputeFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Black,
            "the {B} pip makes it black");
        colors.Should().NotContain(Majik.Core.ValueObjects.ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DeadlyDispute()
    {
        var card = NamedCardFactory.Create("Deadly Dispute", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Deadly Dispute");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacArtifactOrCreatureCost_NoTargets()
    {
        var def = DeadlyDisputeFactory.BuildSpellDefinition(_alice);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeAnArtifactOrCreatureAdditionalCost>(
                "Deadly Dispute prints 'As an additional cost to cast this spell, sacrifice an artifact or creature.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Deadly Dispute has no targets");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw two cards + create a Treasure
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCards_AndCreatesTreasure()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3");

        foreach (var e in DeadlyDisputeFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Should().ContainSingle(p => p.HasSubtype(CardSubtype.Treasure),
                "Deadly Dispute creates one Treasure token (CR 111.10)");
    }

    [Fact]
    public void Resolve_EmptyLibraryMidDraw_StillCreatesTreasure_FlagsSbaLoss()
    {
        var only = SeedLibraryCard(_alice, "Only");

        foreach (var e in DeadlyDisputeFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");

        _alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Should().ContainSingle(p => p.HasSubtype(CardSubtype.Treasure),
                "the Treasure is still created even when the draw runs the library dry");
    }

    // -----------------------------------------------------------------------
    // Cost: sacrifices an artifact or a creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_SacrificesArtifact_WhenAvailable()
    {
        var rock = new Artifact("Mind Stone", "{2}");
        PutOnBattlefield(_alice, rock);

        var cost = new SacrificeAnArtifactOrCreatureAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Sacrificed.Should().BeSameAs(rock);
        rock.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_SacrificesCreature_WhenNoArtifact()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);

        var cost = new SacrificeAnArtifactOrCreatureAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Sacrificed.Should().BeSameAs(bear,
            "no artifact to sacrifice — a creature is the legal sacrifice");
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoArtifactAndNoCreature()
    {
        // A land on the battlefield is neither an artifact nor a creature, so
        // the cost is unpayable.
        var land = new Land("Swamp");
        PutOnBattlefield(_alice, land);

        var cost = new SacrificeAnArtifactOrCreatureAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse(
            "neither an artifact nor a creature is controlled (CR 117.1)");
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
