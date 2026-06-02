using System.Linq;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WitchingWellFactory"/>.
///
/// Oracle (Scryfall-confirmed, Throne of Eldraine):
///   "When this artifact enters, scry 2. (Look at the top two cards of your
///    library, then put any number of them on the bottom and the rest on top
///    in any order.)
///    {3}{U}, Sacrifice this artifact: Draw two cards."
///
/// Scryfall type line: Artifact (no subtype). Mana cost {U}. Identity + both
/// abilities are loaded from <c>witching-well.json</c> via
/// <see cref="CardDefinitionFactory"/>.
///
/// Covers:
/// - Identity: Artifact type, name, {U} mana cost, owner/controller.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Witching Well".
/// - Two abilities: one ETB scry-2 <see cref="TriggeredAbility"/> + one
///   {3}{U}, Sacrifice activated ability.
/// - Activated-ability cost shape: {3}{U} mana + sacrifice-self, no targets.
/// - Activated-ability resolve: controller draws 2, the artifact is
///   sacrificed (CR 701.16).
/// - ETB scry 2 resolve: top two cards go to the bottom (no-agent default).
/// </summary>
[Trait("Color", "U")]
public class WitchingWellFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsArtifact_NamedWitchingWell_WithBlueManaCost()
    {
        var well = WitchingWellFactory.Create(_alice);

        well.HasType(CardType.Artifact).Should().BeTrue();
        well.Name.Should().Be("Witching Well");
        well.ManaCost.Should().Be("{U}");
        well.Owner.Should().BeSameAs(_alice);
        well.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WitchingWell()
    {
        var card = NamedCardFactory.Create("Witching Well", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Witching Well");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasTwoAbilities_OneTriggeredOneActivated()
    {
        var well = WitchingWellFactory.Create(_alice);

        well.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB scry-2 trigger");
        well.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {3}{U}, Sacrifice: Draw two cards activated ability");
    }

    [Fact]
    public void ActivatedAbility_HasManaAndSacrifice_AndNoTargets()
    {
        var well = WitchingWellFactory.Create(_alice);
        var ability = well.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the cost includes {3}{U} mana");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cost sacrifices the artifact (CR 701.16)");
        ability.TargetRequests.Should().BeEmpty(
            "Draw two cards has no targets");
    }

    // -----------------------------------------------------------------------
    // {3}{U}, Sacrifice: Draw two cards
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Draw_DrawsTwoCards_AndSacrificesArtifact()
    {
        var well = WitchingWellFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(well);
        well.SetZone(ZoneType.Battlefield);

        var top1 = new Card("Top1", "");
        var top2 = new Card("Top2", "");
        top1.SetOwner(_alice);
        top2.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top1);
        _alice.Zones.Library.AddCard(top2);
        top1.SetZone(ZoneType.Library);
        top2.SetZone(ZoneType.Library);

        var ability = well.Abilities.OfType<ActivatedAbility>().Single();

        // {3}{U} mana + Sacrifice are both costs (the JSON schema path pays
        // the sacrifice via the cost, not in the resolve closure). Float
        // {3}{U} so the mana pips are affordable, pay all costs, then resolve
        // the draw.
        _alice.AddManaToPool(ManaCost.Parse("3U"));
        foreach (var cost in ability.Costs)
        {
            cost.Pay(_alice);
        }
        ability.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { top1, top2 },
            "two cards drawn");
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        _alice.Zones.Graveyard.GetCards().Should().Contain(well,
            "the artifact is sacrificed as a cost");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(well);
        well.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // ETB: scry 2 (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbScryTwo_PutsTopTwoOnBottom_WithNoAgent()
    {
        // No agent registered → the default Scry decision sends all peeked
        // cards to the bottom (same fallback as CardDefinitionFactory's
        // scry_self path). With three cards on top, after scrying the top
        // two to the bottom the third (unscryed) card becomes the new top.
        var alice = new Player("Alice", 20);
        var top1 = new Card("L1", "");
        var top2 = new Card("L2", "");
        var rest = new Card("L3", "");
        foreach (var c in new[] { top1, top2, rest }) c.SetOwner(alice);
        alice.Zones.Library.AddCard(top1);
        alice.Zones.Library.AddCard(top2);
        alice.Zones.Library.AddCard(rest);

        var well = WitchingWellFactory.Create(alice);
        var trigger = well.Abilities.OfType<TriggeredAbility>().Single();

        // Execute the resolve body directly.
        trigger.Effects.Single().Execute();

        var library = alice.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(3, "scry does not change library size");
        library[0].Should().BeSameAs(rest, "the unscryed card is now on top");
        library.Should().Contain(top1).And.Contain(top2);
    }
}
