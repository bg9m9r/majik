using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GisasBiddingFactory"/>.
///
/// Oracle text: "Create two 2/2 black Zombie creature tokens. Madness {2}{B}"
/// ({2}{B}{B} Sorcery)
///
/// Madness (CR 702.35) is intrinsic — handled by MadnessCatalog + the
/// Fx.DiscardCard funnel — so it is NOT exercised here. These tests cover only
/// the unique non-madness body: identity + the token-creating spell effect.
///
/// Covers:
/// - Card identity (Sorcery, {2}{B}{B}, black, CMC 4, owner/controller).
/// - SpellDefinition shape — no modes, no X, no target requests.
/// - Resolve: controller's battlefield gains exactly two 2/2 black Zombie tokens.
/// </summary>
[Trait("Color", "B")]
public class GisasBiddingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GisasBidding_HasSorceryShape_Black_AtCost2BB()
    {
        var card = GisasBiddingFactory.Create(_alice);

        card.Name.Should().Be("Gisa's Bidding");
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(4);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GisasBidding_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var def = GisasBiddingFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — token creation
    // -----------------------------------------------------------------------

    [Fact]
    public void GisasBidding_Resolve_CreatesTwoZombieTokensOnCastersBattlefield()
    {
        // Battlefield starts empty.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        var effects = GisasBiddingFactory.BuildResolveEffect(_alice);
        effects.Should().HaveCount(1, "one atomic effect produces both tokens");
        effects.Single().Execute();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(2, "Gisa's Bidding creates exactly two tokens");
    }

    [Fact]
    public void GisasBidding_Resolve_EachToken_IsTwoPowerTwoToughness_BlackZombie()
    {
        var effects = GisasBiddingFactory.BuildResolveEffect(_alice);
        effects.Single().Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(2);

        foreach (var token in tokens)
        {
            token.Name.Should().Be("Zombie");
            token.IsToken.Should().BeTrue("CR 111 — Gisa's Bidding creates tokens");
            token.BasePower.Should().Be(2);
            token.BaseToughness.Should().Be(2);
            token.HasSubtype(CardSubtype.Zombie).Should().BeTrue(
                "CR 111.4 — token carries the Zombie creature subtype");
            token.Controller.Should().BeSameAs(_alice);
            CardColors.GetColors(token).Should().Contain(ManaColor.Black,
                "CR 111.4 — the token is explicitly black");
        }
    }
}
