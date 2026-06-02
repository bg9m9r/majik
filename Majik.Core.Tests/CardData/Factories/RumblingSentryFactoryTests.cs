using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RumblingSentryFactory"/>.
///
/// Rumbling Sentry — {3}{W}{W} Creature — Giant 3/6. Oracle text:
///   "When this creature enters, scry 1."
///
/// Covers:
/// - Identity ({3}{W}{W} Creature — Giant, 3/6, white).
/// - NO Flying keyword (Rumbling Sentry has no evasion).
/// - Mana value 5 (CR 202.3 — {3}{W}{W} = 3 + 1 + 1 = 5).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one battlefield-active ETB triggered ability (no intervening-if).
/// - ETB scry 1 default (no agent): peeked card sent to bottom.
/// - ETB scry 1 with agent keeping card on top.
/// - ETB scry 1 on empty library: no-ops cleanly (CR 701.20).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "W")]
public class RumblingSentryFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void RumblingSentry_Identity()
    {
        var c = RumblingSentryFactory.Create(_alice);

        c.Name.Should().Be("Rumbling Sentry");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue("Rumbling Sentry is a Giant");
        c.ManaCost.Should().Be("{3}{W}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RumblingSentry_IsWhite()
    {
        var c = RumblingSentryFactory.Create(_alice);
        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.White,
            "Rumbling Sentry has {W}{W} pips in its mana cost");
        colors.Should().HaveCount(1, "only one color identity");
    }

    [Fact]
    public void RumblingSentry_ManaValue_IsFive()
    {
        var c = RumblingSentryFactory.Create(_alice);
        // {3}{W}{W} = mana value 5 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(5, "CR 202.3 — {3}{W}{W} has mana value 5");
    }

    [Fact]
    public void RumblingSentry_HasNoFlyingKeyword()
    {
        var c = RumblingSentryFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Flying",
                "Rumbling Sentry does not have Flying");
    }

    // ── NamedCardFactory dispatch ─────────────────────────────────────────────
    // ── ETB triggered ability shape ───────────────────────────────────────────

    [Fact]
    public void RumblingSentry_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = RumblingSentryFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause (CR 603.4 does not apply)");
    }

    // ── ETB scry 1 — default (no agent): peeked card bottomed ────────────────

    [Fact]
    public void RumblingSentry_EtbTrigger_ScryOne_DefaultSendsTopCardToBottom()
    {
        var alice = new Player("Alice", 20);

        // Library: [a, b, c]. Scry 1 sees [a]; default sends it to bottom.
        // Final library: [b, c, a].
        var a = SeedLibraryCard(alice, "A");
        var b = SeedLibraryCard(alice, "B");
        var c = SeedLibraryCard(alice, "C");

        var sentry = RumblingSentryFactory.Create(alice);
        var etb = sentry.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, a },
            "scry 1 default sends the peeked card to bottom; rest stays in order");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "scry 1 does NOT draw — hand stays empty");
    }

    // ── ETB scry 1 — agent keeps card on top ─────────────────────────────────

    [Fact]
    public void RumblingSentry_EtbTrigger_ScryOne_AgentKeepsTopCard()
    {
        var alice = new Player("Alice", 20);

        // Library: [a, b, c]. Agent keeps [a] on top.
        // Final library: [a, b, c] — unchanged.
        var a = SeedLibraryCard(alice, "A");
        var b = SeedLibraryCard(alice, "B");
        var c = SeedLibraryCard(alice, "C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a }));
        AgentRegistry.Set(alice, agent);

        var sentry = RumblingSentryFactory.Create(alice);
        var etb = sentry.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c },
            "agent chose to keep the top card; library is unchanged");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "scry 1 does NOT draw");
    }

    // ── ETB scry 1 — empty library ────────────────────────────────────────────

    [Fact]
    public void RumblingSentry_EtbTrigger_EmptyLibrary_NoOpsCleanly()
    {
        var alice = new Player("Alice", 20);
        // Library is intentionally empty.

        var sentry = RumblingSentryFactory.Create(alice);
        var etb = sentry.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow("scry 1 on an empty library short-circuits per CR 701.20");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
        alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "scry 1 does not draw — empty library should not stamp the loss flag");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Card SeedLibraryCard(Player player, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(player);
        player.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
