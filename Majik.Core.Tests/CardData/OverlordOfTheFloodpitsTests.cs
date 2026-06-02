using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="OverlordOfTheFloodpitsFactory"/> — Overlord of the
/// Floodpits (Duskmourn: House of Horror, {3}{U}{U}). Enchantment Creature —
/// Avatar Horror 5/3.
///
/// Covers:
///   - Card shape (name, types Creature + Enchantment, Avatar + Horror
///     subtypes, {3}{U}{U}, 5/3).
///   - Impending 4 marker keyword (mechanic deferred; marker present).
///   - Flying marker keyword.
///   - Two enters-or-attacks triggered abilities (ETB + attack).
///   - NamedCardFactory dispatch.
///   - Trigger body: draw two, then discard one.
///   - Empty-hand-after-draw / small-library is a clean no-op.
/// </summary>
public class OverlordOfTheFloodpitsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Overlord_IsEnchantmentCreature_AvatarHorror_FiveThree()
    {
        var c = OverlordOfTheFloodpitsFactory.Create(_alice);

        c.Name.Should().Be("Overlord of the Floodpits");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Overlord_HasImpendingMarker_WithCount4()
    {
        var c = OverlordOfTheFloodpitsFactory.Create(_alice);

        var impending = c.Abilities.OfType<KeywordAbility>()
            .SingleOrDefault(k => k.Keyword == "Impending");
        impending.Should().NotBeNull();
        impending!.Arg.Should().Be(4);
    }

    [Fact]
    public void Overlord_HasFlyingMarker()
    {
        var c = OverlordOfTheFloodpitsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flying");
    }

    [Fact]
    public void Overlord_HasTwoTriggers_EntersAndAttacks()
    {
        var c = OverlordOfTheFloodpitsFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Overlord prints one ability that triggers on enters OR attacks "
            + "— modelled as two TriggeredAbility instances sharing an effect.");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Overlord()
    {
        var card = NamedCardFactory.Create("Overlord of the Floodpits", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Overlord of the Floodpits");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain(new[] { "Impending", "Flying" });
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Trigger body: draw 2, then discard 1
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_DrawsTwo_ThenDiscardsOne()
    {
        SeedLibrary(_alice, 10);

        var overlord = OverlordOfTheFloodpitsFactory.Create(_alice);
        ResolveFirstTrigger(overlord);

        // Drew 2 (library 10 → 8), then discarded 1 of those 2.
        _alice.Zones.Library.GetCards().Should().HaveCount(8);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Trigger_EmptyLibrary_IsCleanNoOp()
    {
        // Empty library + empty hand. Resolving must not throw; there is
        // nothing to draw and nothing to discard.
        var overlord = OverlordOfTheFloodpitsFactory.Create(_alice);

        Action resolve = () => ResolveFirstTrigger(overlord);
        resolve.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ResolveFirstTrigger(Creature overlord)
    {
        var trigger = overlord.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in trigger.Effects)
            eff.Execute();
    }

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Card($"Lib-{i}", "");
            card.SetOwner(p);
            card.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(card);
        }
    }
}
