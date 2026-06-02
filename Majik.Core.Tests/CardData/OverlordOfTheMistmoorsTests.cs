using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="OverlordOfTheMistmoorsFactory"/> — Overlord of the
/// Mistmoors (Duskmourn: House of Horror, {5}{W}{W}). Enchantment Creature —
/// Avatar Horror 6/6.
///
/// Covers:
///   - Card shape (name, types Creature + Enchantment, Avatar + Horror
///     subtypes, {5}{W}{W}, 6/6).
///   - Impending 4 marker keyword (mechanic deferred; marker present).
///   - Two enters-or-attacks triggered abilities (ETB + attack).
///   - NamedCardFactory dispatch.
///   - Trigger body: creates two 2/1 white Insect creature tokens with
///     flying, on the battlefield.
///   - The token's shape: 2/1 Creature, Insect subtype, white, a token,
///     with Flying.
/// </summary>
public class OverlordOfTheMistmoorsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Overlord_IsEnchantmentCreature_AvatarHorror_SixSix()
    {
        var c = OverlordOfTheMistmoorsFactory.Create(_alice);

        c.Name.Should().Be("Overlord of the Mistmoors");
        c.ManaCost.Should().Be("{5}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Overlord_HasImpendingMarker_WithCount4()
    {
        var c = OverlordOfTheMistmoorsFactory.Create(_alice);

        var impending = c.Abilities.OfType<KeywordAbility>()
            .SingleOrDefault(k => k.Keyword == "Impending");
        impending.Should().NotBeNull();
        impending!.Arg.Should().Be(4);
    }

    [Fact]
    public void Overlord_HasTwoTriggers_EntersAndAttacks()
    {
        var c = OverlordOfTheMistmoorsFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Overlord prints one ability that triggers on enters OR attacks "
            + "— modelled as two TriggeredAbility instances sharing an effect.");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Overlord()
    {
        var card = NamedCardFactory.Create("Overlord of the Mistmoors", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Overlord of the Mistmoors");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Impending");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Insect token shape
    // -----------------------------------------------------------------------

    [Fact]
    public void InsectTokens_AreTwo_TwoOneWhiteFlyingInsects()
    {
        var tokens = OverlordOfTheMistmoorsFactory.CreateInsectTokens(_alice);

        tokens.Should().HaveCount(2);
        foreach (var token in tokens)
        {
            token.Name.Should().Be("Insect");
            token.HasType(CardType.Creature).Should().BeTrue();
            token.HasSubtype(CardSubtype.Insect).Should().BeTrue();
            token.IsToken.Should().BeTrue();
            token.BasePower.Should().Be(2);
            token.BaseToughness.Should().Be(1);
            token.Zone.Should().Be(ZoneType.Battlefield);

            // White (CR 111.4) — stamped explicitly, not derived from a cost.
            CardColors.GetColors(token).Should().BeEquivalentTo(new[] { ManaColor.White });

            // Flying (CR 702.9) granted via the Keywords list.
            token.Abilities.OfType<KeywordAbility>()
                .Select(k => k.Keyword).Should().Contain("Flying");

            token.Owner.Should().BeSameAs(_alice);
            token.Controller.Should().BeSameAs(_alice);
        }
    }

    // -----------------------------------------------------------------------
    // Trigger body
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_CreatesTwoInsectTokensOnBattlefield()
    {
        var overlord = OverlordOfTheMistmoorsFactory.Create(_alice);
        ResolveFirstTrigger(overlord);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Insect").Should().Be(2);
    }

    [Fact]
    public void Trigger_ResolvingTwice_CreatesFourTokens()
    {
        var overlord = OverlordOfTheMistmoorsFactory.Create(_alice);
        ResolveFirstTrigger(overlord);
        ResolveFirstTrigger(overlord);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Insect").Should().Be(4);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ResolveFirstTrigger(Creature overlord)
    {
        var trigger = overlord.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in trigger.Effects)
            eff.Execute();
    }
}
