using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="OmenOfTheSeaFactory"/>.
///
/// Omen of the Sea (Theros Beyond Death, {1}{U}, Enchantment):
///   "Flash
///    When this enchantment enters, scry 2, then draw a card.
///    {2}{U}, Sacrifice this enchantment: Scry 2."
///
/// Covers:
///   - Card identity (name, Enchantment type, {1}{U} mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Flash keyword marker (CR 702.8).
///   - Ability shape: one ETB <see cref="TriggeredAbility"/> + one
///     <see cref="ActivatedAbility"/> ({2}{U} + sacrifice → scry 2).
///   - ETB trigger: default scry (no agent) bottoms both peeked cards, then
///     draws the new top card.
///   - ETB trigger: empty library flags draw-from-empty without throwing.
///   - Activated ability: scrys 2 (agent keeps both on top) and sacrifices
///     this enchantment to its owner's graveyard.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class OmenOfTheSeaTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void OmenOfTheSea_HasExpectedShape()
    {
        var card = OmenOfTheSeaFactory.Create(_alice);

        card.Name.Should().Be("Omen of the Sea");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OmenOfTheSea()
    {
        var card = NamedCardFactory.Create("Omen of the Sea", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Omen of the Sea");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OmenOfTheSea_HasFlashKeyword()
    {
        var card = OmenOfTheSeaFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash", "Omen of the Sea has Flash (CR 702.8)");
    }

    [Fact]
    public void OmenOfTheSea_HasOneEtbTrigger_AndOneActivatedAbility()
    {
        var card = OmenOfTheSeaFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ActivatedAbility_HasManaCost_AndSacrificeSelf()
    {
        var card = OmenOfTheSeaFactory.Create(_alice);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        // ManaCostCost.Description renders the symbol-stripped form ("2U").
        ability.Costs.OfType<ManaCostCost>()
            .Should().Contain(c => c.Description.Replace("{", "").Replace("}", "") == "2U",
                "the sac mode costs {2}{U}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the sac mode sacrifices this enchantment");
    }

    [Fact]
    public void EtbTrigger_DefaultScry_BottomsBoth_ThenDraws()
    {
        // Library: [a, b, c, d]. No agent → default bottoms peeked a,b.
        // New top = c, drawn into hand; library becomes [d, a, b].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var card = OmenOfTheSeaFactory.Create(_alice);
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { c });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { d, a, b });
        c.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void EtbTrigger_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        var card = OmenOfTheSeaFactory.Create(_alice);
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void ActivatedAbility_Scry2_KeepsBothOnTop_AndSacrificesSelf()
    {
        // Library: [a, b, c]. Agent keeps both peeked cards on top in order;
        // library order is preserved and NO card is drawn (sac mode is scry-only).
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b }));
        AgentRegistry.Set(_alice, agent);

        var card = OmenOfTheSeaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Scry-only: library order preserved, hand still empty.
        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c });
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        // Sacrificed: enchantment moved Battlefield → Graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
