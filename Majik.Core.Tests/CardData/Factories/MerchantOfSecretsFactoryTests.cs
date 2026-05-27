using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MerchantOfSecretsFactory"/>.
///
/// Covers:
/// - Identity ({2}{U} Creature — Human Wizard, 1/1, blue).
/// - NO Flying keyword.
/// - Mana value 3 (CR 202.3).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one battlefield-active ETB triggered ability (no intervening-if).
/// - ETB draws 1 card for the controller from a stocked library.
/// - ETB on empty library stamps the loss flag (CR 704.5b) without crashing.
/// </summary>
public class MerchantOfSecretsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MerchantOfSecrets_Identity()
    {
        var c = MerchantOfSecretsFactory.Create(_alice);

        c.Name.Should().Be("Merchant of Secrets");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Merchant of Secrets is a Human");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Merchant of Secrets is a Wizard");
        c.ManaCost.Should().Be("{2}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MerchantOfSecrets_IsBlue()
    {
        var c = MerchantOfSecretsFactory.Create(_alice);
        // Color is derived from mana cost — {U} pip makes it blue.
        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Blue,
            "Merchant of Secrets has a {U} pip in its mana cost");
        colors.Should().HaveCount(1, "only one color identity");
    }

    [Fact]
    public void MerchantOfSecrets_ManaValue_IsThree()
    {
        var c = MerchantOfSecretsFactory.Create(_alice);
        // {2}{U} = mana value 3 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(3, "CR 202.3 — {2}{U} has mana value 3");
    }

    [Fact]
    public void MerchantOfSecrets_HasNoFlyingKeyword()
    {
        var c = MerchantOfSecretsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Flying",
                "Merchant of Secrets does not have Flying");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MerchantOfSecrets_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Merchant of Secrets", _alice);

        c.Should().BeOfType<Creature>("Merchant of Secrets is a Creature");
        c.Name.Should().Be("Merchant of Secrets");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{U}");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MerchantOfSecrets_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = MerchantOfSecretsFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // ETB draw effect — stocked library
    // -----------------------------------------------------------------------

    [Fact]
    public void MerchantOfSecrets_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);

        // Seed the library with three known cards.
        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        var c3 = new Card("Top3", "");
        foreach (var card in new[] { c1, c2, c3 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var merchant = MerchantOfSecretsFactory.Create(alice);
        var etb = merchant.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "ETB draws exactly 1 card (CR 121.1)");
        alice.Zones.Library.GetCards().Should().HaveCount(2,
            "one card left the top of the library");
    }

    // -----------------------------------------------------------------------
    // ETB draw effect — empty library
    // -----------------------------------------------------------------------

    [Fact]
    public void MerchantOfSecrets_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);
        // Library is intentionally empty.

        var merchant = MerchantOfSecretsFactory.Create(alice);
        var etb = merchant.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draws");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }
}
