using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="OriginSpellbombFactory"/> — Artifact {1}:
///   "{1}, {T}, Sacrifice this artifact: Create a 1/1 colorless Myr artifact
///    creature token.
///    When this artifact is put into a graveyard from the battlefield, you may
///    pay {W}. If you do, draw a card."
///
/// Covers only the card's UNIQUE behaviour:
/// - Identity (Artifact, {1}) — a single _Identity assert.
/// - Activated-ability shape: {1} mana + tap + sacrifice, no targets.
/// - Token-mode resolution: a 1/1 colourless Myr artifact-creature token is
///   created and the spellbomb is sacrificed.
/// - Dies trigger: with {W} available the controller draws a card; without it,
///   no draw (CR 603.6c — "you may pay").
///
/// (NamedCardFactory dispatch + well-formedness are covered globally by
/// CardFactoryContractTests — no dispatch test here.)
/// </summary>
[Trait("Color", "C")]
public class OriginSpellbombFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void OriginSpellbomb_Identity_IsArtifact_WithOneManaCost()
    {
        var bomb = OriginSpellbombFactory.Create(_alice);

        bomb.HasType(CardType.Artifact).Should().BeTrue();
        bomb.Name.Should().Be("Origin Spellbomb");
        bomb.ManaCost.Should().Be("{1}");
        bomb.Owner.Should().BeSameAs(_alice);
        bomb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TokenAbility_HasOneGenericMana_Tap_AndSacrifice_NoTargets()
    {
        var bomb = OriginSpellbombFactory.Create(_alice);

        var ability = bomb.Abilities.OfType<ActivatedAbility>().Single();

        ability.TargetRequests.Should().BeEmpty("the token mode targets nothing");

        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "the token mode costs {1}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the token mode taps the spellbomb");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the token mode sacrifices the spellbomb");
    }

    [Fact]
    public void Activate_Token_CreatesMyrArtifactCreatureToken_AndSacrificesSpellbomb()
    {
        var bomb = OriginSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var ability = bomb.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        // Spellbomb sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);

        // A 1/1 colourless Myr artifact-creature token entered the battlefield.
        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        token.Name.Should().Be("Myr");
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.HasType(CardType.Artifact).Should().BeTrue("Myr tokens are artifact creatures");
        token.HasType(CardType.Creature).Should().BeTrue();
        token.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DiesTrigger_PaysW_DrawsACard()
    {
        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Controller has {W} available — "you may pay {W}" auto-accepts in v1.
        _alice.AddManaToPool(ManaCost.Parse("{W}"));

        var bomb = OriginSpellbombFactory.Create(_alice);

        var diesTrigger = bomb.Abilities.OfType<TriggeredAbility>().Single();
        diesTrigger.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top, "paying {W} draws a card");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DiesTrigger_NoWhiteMana_NoDraw()
    {
        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // No {W} in the pool — the optional payment can't be made, so no draw.
        var bomb = OriginSpellbombFactory.Create(_alice);

        var diesTrigger = bomb.Abilities.OfType<TriggeredAbility>().Single();
        diesTrigger.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(top);
        _alice.Zones.Library.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Library);
    }
}
