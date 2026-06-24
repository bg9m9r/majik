using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WerefoxBodyguardFactory"/>.
///
/// Werefox Bodyguard (Bloomburrow, Creature — Elf Fox Knight {1}{W}{W} 2/2).
///   "Flash
///    When this creature enters, exile up to one other target non-Fox creature
///    until this creature leaves the battlefield.
///    {1}{W}, Sacrifice this creature: You gain 2 life."
///   (oracle verified against Scryfall 2026-06-24)
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: Elf Fox Knight 2/2 at {1}{W}{W} with the printed Flash keyword
///   (non-vanilla cost / P-T / subtypes).
/// - Two abilities: the ETB exile-until-leaves trigger (CR 603.6a / 701.21) and
///   the {1}{W}+Sacrifice activated lifegain ability (CR 602.1 / 119.3).
/// - ETB exiles the targeted non-Fox creature (the Banisher-Priest shape).
/// - ETB rejects a Fox creature at resolution (the printed "non-Fox" filter,
///   CR 608.2b) — the card's unique target restriction.
/// - ETB rejects targeting Werefox Bodyguard itself ("other", CR 608.2b).
/// - ETB is "up to one" — the optional 0..1 target shape (CR 115.1b).
/// - LTB returns the exiled creature under its owner's control (CR 110.2).
/// - The activated ability sacrifices for 2 life (CR 119.3) and carries the
///   printed Sacrifice cost.
///
/// <see cref="NamedCardFactory"/> dispatch + well-formedness are asserted for
/// every implemented card by <c>CardFactoryContractTests</c> — not re-tested
/// here.
/// </summary>
[Trait("Color", "W")]
public class WerefoxBodyguardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void WerefoxBodyguard_Identity()
    {
        var c = WerefoxBodyguardFactory.Create(_alice);

        c.Name.Should().Be("Werefox Bodyguard");
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Fox).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // CR 702.8 — Flash, materialised from the JSON keywords array.
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash", "printed Flash keyword");

        // ETB exile trigger + LTB return trigger.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile-until-leaves trigger + LTB return trigger");
        c.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the {1}{W}, Sacrifice: gain 2 life ability");
    }

    [Fact]
    public void WerefoxBodyguard_Etb_ExilesNonFoxCreature()
    {
        var werefox = WerefoxBodyguardFactory.Create(_alice);
        werefox.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(werefox);

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob);
        goyf.SetController(_bob);
        goyf.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goyf);

        var etb = EtbTrigger(werefox);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { goyf } });
        etb.Resolve();

        goyf.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted non-Fox creature (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goyf);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goyf);
    }

    [Fact]
    public void WerefoxBodyguard_Etb_RejectsFoxCreature()
    {
        var werefox = WerefoxBodyguardFactory.Create(_alice);
        werefox.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(werefox);

        // A Fox creature — the printed "non-Fox" filter must skip it (CR 608.2b).
        var foxFamiliar = new Creature("Filigree Familiar", "{3}", 2, 2,
            subtypes: new[] { CardSubtype.Fox });
        foxFamiliar.SetOwner(_bob);
        foxFamiliar.SetController(_bob);
        foxFamiliar.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(foxFamiliar);

        var etb = EtbTrigger(werefox);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { foxFamiliar } });
        etb.Resolve();

        foxFamiliar.Zone.Should().Be(ZoneType.Battlefield,
            "Fox creatures are skipped by the printed 'non-Fox' filter (CR 608.2b)");
    }

    [Fact]
    public void WerefoxBodyguard_Etb_RejectsSelf()
    {
        var werefox = WerefoxBodyguardFactory.Create(_alice);
        werefox.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(werefox);

        var etb = EtbTrigger(werefox);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { werefox } });
        etb.Resolve();

        werefox.Zone.Should().Be(ZoneType.Battlefield,
            "the ETB targets 'up to one OTHER' creature — it cannot exile itself (CR 608.2b)");
    }

    [Fact]
    public void WerefoxBodyguard_Etb_IsOptional()
    {
        var werefox = WerefoxBodyguardFactory.Create(_alice);

        var etb = EtbTrigger(werefox);
        etb.TargetRequests.Should().ContainSingle();
        etb.TargetRequests[0].MinTargets.Should().Be(0,
            "'up to one … target' is an optional 0..1 target (CR 115.1b)");
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void WerefoxBodyguard_Ltb_ReturnsExiledCreature()
    {
        var werefox = WerefoxBodyguardFactory.Create(_alice);
        werefox.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(werefox);

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob);
        goyf.SetController(_bob);
        goyf.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goyf);

        var etb = EtbTrigger(werefox);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { goyf } });
        etb.Resolve();
        goyf.Zone.Should().Be(ZoneType.Exile);

        var ltb = werefox.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        ltb.Resolve();

        goyf.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled creature to the battlefield");
        goyf.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
    }

    [Fact]
    public void WerefoxBodyguard_SacrificeAbility_GainsTwoLife()
    {
        var werefox = WerefoxBodyguardFactory.Create(_alice);
        var activated = werefox.Abilities.OfType<ActivatedAbility>().Single();

        // {1}{W}, Sacrifice this creature: You gain 2 life. (CR 602.1)
        activated.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the printed Sacrifice cost");

        foreach (var effect in activated.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(22,
            "the sacrifice ability gains its controller 2 life (CR 119.3)");
    }

    private static TriggeredAbility EtbTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
}
