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
/// Tests for <see cref="VillageRitesFactory"/> — Instant {B}.
///
/// "As an additional cost to cast this spell, sacrifice a creature.
///  Draw two cards."
///
/// Covers:
///   - Identity (Instant, {B}, black, owner / controller) + NamedCardFactory
///     dispatch (built from the embedded JSON definition).
///   - SpellDefinition shape: <see cref="SacrificeACreatureAdditionalCost"/>
///     additional cost (CR 601.2f), no modes, no X, no target requests.
///   - Resolve: caster draws two cards (CR 121.1); no token is created.
///   - Resolve: empty library mid-draw → draws what's available, SBA loss
///     flag set (CR 704.5b).
///   - Cost: sacrifices a creature when available; CanPay false with none
///     (CR 601.2f).
/// </summary>
public class VillageRitesTests
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
        var card = VillageRitesFactory.Create(_alice);

        card.Name.Should().Be("Village Rites");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VillageRites_IsBlack()
    {
        var card = VillageRitesFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Black,
            "the {B} pip makes it black");
        colors.Should().NotContain(Majik.Core.ValueObjects.ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VillageRites()
    {
        var card = NamedCardFactory.Create("Village Rites", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Village Rites");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacCreatureCost_NoTargets()
    {
        var def = VillageRitesFactory.BuildSpellDefinition(_alice);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeACreatureAdditionalCost>(
                "Village Rites prints 'As an additional cost to cast this spell, sacrifice a creature.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Village Rites has no targets");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw two cards (no token)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCards_NoToken()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3");

        foreach (var e in VillageRitesFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Village Rites creates no token — it only draws two cards");
    }

    [Fact]
    public void Resolve_EmptyLibraryMidDraw_FlagsSbaLoss()
    {
        var only = SeedLibraryCard(_alice, "Only");

        foreach (var e in VillageRitesFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // Cost: sacrifices a creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_SacrificesCreature_WhenAvailable()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);

        var cost = new SacrificeACreatureAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Sacrificed.Should().BeSameAs(bear);
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoCreature()
    {
        // A land on the battlefield is not a creature, so the cost is unpayable.
        var land = new Land("Swamp");
        PutOnBattlefield(_alice, land);

        var cost = new SacrificeACreatureAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse(
            "no creature is controlled — the additional cost can't be paid (CR 601.2f)");
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
