using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Superior Spider-Man (Marvel's Spider-Man, {2}{U}{B}).
///
/// Covers ONLY the card's unique behaviour (Mind Swap enters-as-copy from a
/// graveyard + the 4/4 / Spider-Human-Hero / exile riders) plus a single
/// identity assert. NamedCardFactory dispatch + well-formedness are covered
/// automatically by CardFactoryContractTests for every implemented card.
/// </summary>
[Trait("Color", "M")]
public class SuperiorSpiderManFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SuperiorSpiderMan_Identity_LegendaryCreature_SpiderHumanHero_4_4_At2UB()
    {
        var ssm = SuperiorSpiderManFactory.Create(_alice);

        ssm.Name.Should().Be("Superior Spider-Man");
        ssm.ManaCost.Should().Be("{2}{U}{B}");
        ssm.HasType(CardType.Creature).Should().BeTrue();
        ssm.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ssm.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        ssm.HasSubtype(CardSubtype.Human).Should().BeTrue();
        ssm.HasSubtype(CardSubtype.Hero).Should().BeTrue();
        ssm.BasePower.Should().Be(4);
        ssm.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void MindSwap_EntersAsCopyOfGraveyardCreature_Is4_4_AndExilesTheCard()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // A vanilla 2/2 Bear sitting in Alice's graveyard as the copy source.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var ssm = SuperiorSpiderManFactory.Create(_alice, replacements: bus, effects: effects);
        ssm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ssm);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(ssm, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CR 706.3 — "he's a 4/4": the Layer-7b BecomesPTEffect overrides the
        // copied 2/2, so Superior Spider-Man is a 4/4 (not the Bear's 2/2).
        ssm.Power.Should().Be(4, "the '4/4' rider overrides the copied Bear's 2/2");
        ssm.Toughness.Should().Be(4);
        ssm.Zone.Should().Be(ZoneType.Battlefield);

        // CR 706.3 — "his name is Superior Spider-Man" (CopyEffect does not
        // mirror name, so the printed name survives the copy).
        ssm.Name.Should().Be("Superior Spider-Man");

        // "Spider Human Hero in addition to its other types".
        ssm.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        ssm.HasSubtype(CardSubtype.Human).Should().BeTrue();
        ssm.HasSubtype(CardSubtype.Hero).Should().BeTrue();
        var computed = effects.Compute(ssm);
        computed.Subtypes.Should().Contain(new[]
        {
            CardSubtype.Spider, CardSubtype.Human, CardSubtype.Hero,
        });

        // "When you do, exile that card." — the Bear is moved graveyard → exile.
        bear.Zone.Should().Be(ZoneType.Exile, "Mind Swap exiles the copied creature card");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void MindSwap_NoGraveyardCreature_EntersAsPrintedVanilla4_4_NoExile()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // Empty graveyard → "you may" auto-declines (no candidate). Unlike the
        // printed-0/0 Clone family, Superior Spider-Man is intrinsically a 4/4,
        // so he simply enters as a vanilla 4/4 (no SBA death — CR 704.5f N/A).
        var ssm = SuperiorSpiderManFactory.Create(_alice, replacements: bus, effects: effects);
        ssm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ssm);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(ssm, ZoneType.Hand, ZoneType.Battlefield, _alice);

        ssm.Power.Should().Be(4, "no copy source → printed vanilla 4/4");
        ssm.Toughness.Should().Be(4);
        ssm.Zone.Should().Be(ZoneType.Battlefield);
        ssm.HasSubtype(CardSubtype.Hero).Should().BeTrue();
        _alice.Zones.Exile.GetCards().Should().BeEmpty("nothing was copied → nothing exiled");
    }
}
