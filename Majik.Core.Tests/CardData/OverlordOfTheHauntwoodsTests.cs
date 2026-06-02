using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="OverlordOfTheHauntwoodsFactory"/> — Overlord of the
/// Hauntwoods (Duskmourn: House of Horror, {3}{G}{G}). Enchantment Creature —
/// Avatar Horror 6/5.
///
/// Covers:
///   - Card shape (name, types Creature + Enchantment, Avatar + Horror
///     subtypes, {3}{G}{G}, 6/5).
///   - Impending 4 marker keyword (mechanic deferred; marker present).
///   - Two enters-or-attacks triggered abilities (ETB + attack).
///   - NamedCardFactory dispatch.
///   - Trigger body: creates a tapped colorless "Everywhere" land token that
///     is every basic land type, on the battlefield.
///   - The token's shape: Land, all five basic land subtypes, not basic,
///     colorless, a token, tapped, five mana abilities.
/// </summary>
public class OverlordOfTheHauntwoodsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Overlord_IsEnchantmentCreature_AvatarHorror_SixFive()
    {
        var c = OverlordOfTheHauntwoodsFactory.Create(_alice);

        c.Name.Should().Be("Overlord of the Hauntwoods");
        c.ManaCost.Should().Be("{3}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Overlord_HasImpendingMarker_WithCount4()
    {
        var c = OverlordOfTheHauntwoodsFactory.Create(_alice);

        var impending = c.Abilities.OfType<KeywordAbility>()
            .SingleOrDefault(k => k.Keyword == "Impending");
        impending.Should().NotBeNull();
        impending!.Arg.Should().Be(4);
    }

    [Fact]
    public void Overlord_HasTwoTriggers_EntersAndAttacks()
    {
        var c = OverlordOfTheHauntwoodsFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Overlord prints one ability that triggers on enters OR attacks "
            + "— modelled as two TriggeredAbility instances sharing an effect.");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Overlord()
    {
        var card = NamedCardFactory.Create("Overlord of the Hauntwoods", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Overlord of the Hauntwoods");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Impending");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // "Everywhere" token shape
    // -----------------------------------------------------------------------

    [Fact]
    public void EverywhereToken_IsTappedColorlessLand_EveryBasicLandType()
    {
        var token = OverlordOfTheHauntwoodsFactory.CreateEverywhereToken(_alice);

        token.Name.Should().Be("Everywhere");
        token.HasType(CardType.Land).Should().BeTrue();
        token.IsToken.Should().BeTrue();
        token.IsTapped.Should().BeTrue("the token is created tapped");

        // Every basic land type (CR 305.6).
        token.HasSubtype(CardSubtype.Plains).Should().BeTrue();
        token.HasSubtype(CardSubtype.Island).Should().BeTrue();
        token.HasSubtype(CardSubtype.Swamp).Should().BeTrue();
        token.HasSubtype(CardSubtype.Mountain).Should().BeTrue();
        token.HasSubtype(CardSubtype.Forest).Should().BeTrue();

        // It is NOT a basic land — "every basic land TYPE" only.
        token.HasSupertype(CardSupertype.Basic).Should().BeFalse();

        // Colorless (CR 111.4) despite producing five colours.
        CardColors.GetColors(token).Should().BeEmpty();

        // Five intrinsic mana abilities (one per basic land type).
        token.Abilities.OfType<ManaAbility>().Should().HaveCount(5);

        token.Owner.Should().BeSameAs(_alice);
        token.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EverywhereToken_CountsForFullDomain()
    {
        // CR 702.16 — the token contributes all five basic land types to
        // Domain because it has every basic land subtype. The token is
        // created onto the controller's battlefield, so Domain.CountTypes
        // (which scans the battlefield) sees all five.
        OverlordOfTheHauntwoodsFactory.CreateEverywhereToken(_alice);

        Majik.Core.Rules.Domain.CountTypes(_alice).Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Trigger body
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_CreatesEverywhereTokenOnBattlefield()
    {
        var overlord = OverlordOfTheHauntwoodsFactory.Create(_alice);
        ResolveFirstTrigger(overlord);

        var token = _alice.Zones.Battlefield.GetCards()
            .SingleOrDefault(c => c.Name == "Everywhere");
        token.Should().NotBeNull();
        token!.Zone.Should().Be(ZoneType.Battlefield);
        ((Land)token).IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Trigger_ResolvingTwice_CreatesTwoTokens()
    {
        var overlord = OverlordOfTheHauntwoodsFactory.Create(_alice);
        ResolveFirstTrigger(overlord);
        ResolveFirstTrigger(overlord);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Everywhere").Should().Be(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ResolveFirstTrigger(Creature overlord)
    {
        var trigger = overlord.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in trigger.Effects)
            eff.Execute();
    }
}
