using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bloodbraid Elf (Alara Reborn / Modern Horizons 2, {2}{R}{G},
/// Creature — Elf Berserker 3/2). Mirrors Shardless Agent's posture —
/// identity + dispatch + cascade trigger + cascade discovery — plus a
/// Haste keyword check (Bloodbraid carries printed Haste, Shardless Agent
/// does not).
/// </summary>
public class BloodbraidElfTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeCostBody()
    {
        var card = BloodbraidElfFactory.Create(_alice);

        card.Name.Should().Be("Bloodbraid Elf");
        card.ManaCost.Should().Be("{2}{R}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Berserker).Should().BeTrue();

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.BasePower.Should().Be(3);
        creature.BaseToughness.Should().Be(2);
        creature.ManaCostValue.TotalValue.Should().Be(4);
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BloodbraidElf()
    {
        var card = NamedCardFactory.Create("Bloodbraid Elf", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bloodbraid Elf");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}{G}");
    }

    [Fact]
    public void Card_HasHasteKeyword()
    {
        var card = BloodbraidElfFactory.Create(_alice);

        // CR 702.10 — Haste keyword marker. The combat reader looks it up
        // through CombatAbilities.HasHaste, which inspects the card's
        // KeywordAbility list.
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste",
                "Bloodbraid Elf prints Haste (CR 702.10).");

        var creature = (Creature)card;
        CombatAbilities.HasHaste(creature).Should().BeTrue(
            "the haste-bypass combat reader honours the printed keyword.");
    }

    [Fact]
    public void Card_HasCascadeTriggeredAbility()
    {
        var card = BloodbraidElfFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Bloodbraid Elf prints one triggered ability — Cascade.");
    }

    [Fact]
    public void CascadeTrigger_OnCast_InvokesCascadeAction_WithSourceMV4()
    {
        // Library setup: Mountain (land, bottomed) then Lightning Bolt
        // (MV 1, eligible — strictly less than 4).
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);

        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = BloodbraidElfFactory.Create(
            _alice,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var cascadeTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in cascadeTrigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(bolt);
        captured.Exiled.Should().HaveCount(2);
        captured.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(mountain);

        bolt.Zone.Should().Be(ZoneType.Exile);
        mountain.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void CascadeTrigger_MV4_HitsMV3_BelowSourceCascadeBoundary()
    {
        // Sanity — the MV-3 ceiling Bloodbraid cascades into. Shardless Agent
        // (MV 3) is strictly less than Bloodbraid's MV 4 → eligible.
        var shardless = NamedCardFactory.Create("Shardless Agent", _alice);
        _alice.Zones.Library.AddCard(shardless);
        shardless.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = BloodbraidElfFactory.Create(
            _alice,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var cascadeTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in cascadeTrigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(shardless,
            "Shardless Agent (MV 3) is strictly less than Bloodbraid's MV 4.");
    }

    [Fact]
    public void CascadeDiscovery_DefaultProbeRecognizes_BloodbraidElf()
    {
        var card = BloodbraidElfFactory.Create(_alice);

        CascadeAltCostProbe.DefaultIsCascadeCard(card).Should().BeTrue(
            "Bloodbraid Elf is registered in the cascade ship list so the "
            + "bot's bidding heuristic sees it as a cascade card.");
    }
}
