using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Shardless Agent (Planechase 2012 / Modern Horizons 2,
/// {1}{G}{U}, Artifact Creature — Human Rogue 2/2).
///
/// Covers:
/// - Identity (name, type, cost, P/T, supertype-less, Artifact + Creature
///   card types, Human + Rogue subtypes).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Cascade triggered ability — exiles cards until a nonland with
///   mana value &lt; 3 is found and surfaces the result via
///   <c>onCascadeResolved</c>.
/// - Cascade discovery — <see cref="CascadeAltCostProbe.DefaultIsCascadeCard"/>
///   reports true for Shardless Agent.
/// </summary>
public class ShardlessAgentTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeCostBody()
    {
        var card = ShardlessAgentFactory.Create(_alice);

        card.Name.Should().Be("Shardless Agent");
        card.ManaCost.Should().Be("{1}{G}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue(
            "Shardless Agent is an Artifact Creature (CR 205.2a)");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.BasePower.Should().Be(2);
        creature.BaseToughness.Should().Be(2);
        creature.ManaCostValue.TotalValue.Should().Be(3);
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ShardlessAgent()
    {
        var card = NamedCardFactory.Create("Shardless Agent", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Shardless Agent");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}{U}");
    }

    [Fact]
    public void Card_HasCascadeTriggeredAbility()
    {
        var card = ShardlessAgentFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Shardless Agent prints one triggered ability — Cascade.");
    }

    [Fact]
    public void CascadeTrigger_OnCast_InvokesCascadeAction_WithSourceMV3()
    {
        // Library setup: Forest (land, bottomed) then Lightning Bolt (MV 1,
        // eligible — strictly less than 3).
        var forest = NamedCardFactory.Create("Forest", _alice);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);

        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = ShardlessAgentFactory.Create(
            _alice,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var cascadeTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in cascadeTrigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(bolt);
        captured.Exiled.Should().HaveCount(2);
        captured.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(forest);

        bolt.Zone.Should().Be(ZoneType.Exile);
        forest.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void CascadeTrigger_NoEligibleCard_StillCompletes()
    {
        // Only an over-cost spell + land in library — cascade finds no
        // eligible card (Big Spell MV 5 ≥ 3, Mountain is land).
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        var heavy = new Sorcery("Big Spell", "{5}");
        heavy.SetOwner(_alice);

        foreach (var c in new ICard[] { mountain, heavy })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        CascadeAction.CascadeResult? captured = null;
        var card = ShardlessAgentFactory.Create(
            _alice,
            triggers: null,
            willCast: null,
            onCascadeResolved: r => captured = r);

        var cascadeTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in cascadeTrigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeNull();
        captured.Bottomed.Should().HaveCount(2);
        _alice.Zones.Library.Count.Should().Be(2);
        _alice.Zones.Exile.Count.Should().Be(0);
    }

    [Fact]
    public void CascadeDiscovery_DefaultProbeRecognizes_ShardlessAgent()
    {
        var card = ShardlessAgentFactory.Create(_alice);

        CascadeAltCostProbe.DefaultIsCascadeCard(card).Should().BeTrue(
            "Shardless Agent is registered in the cascade ship list so the "
            + "bot's bidding heuristic sees it as a cascade card.");
    }
}
