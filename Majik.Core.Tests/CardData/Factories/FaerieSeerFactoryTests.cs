using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FaerieSeerFactory"/>.
///
/// Faerie Seer (Modern Horizons 2, {U}). Creature — Faerie Wizard 1/1.
///   "Flying.
///    When this creature enters, scry 2."
///
/// Covers:
/// - Identity ({U} Creature — Faerie Wizard, 1/1, blue, mana value 1).
/// - Flying keyword marker (CR 702.9).
/// - Exactly one battlefield-active ETB triggered ability (no intervening-if).
/// - ETB Scry 2 (CR 701.20): with a scripted agent the controller sees the
///   top-2 library cards and the scry decision is applied; library count is
///   unchanged (scry only reorders) and the kept-on-top card stays on top.
/// - ETB Scry on a short / empty library does not crash (CR 701.20 — scry N
///   looks at up to N cards).
/// </summary>
[Trait("Color", "U")]
public class FaerieSeerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieSeer_Identity()
    {
        var c = FaerieSeerFactory.Create(_alice);

        c.Name.Should().Be("Faerie Seer");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue("Faerie Seer is a Faerie");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Faerie Seer is a Wizard");
        c.ManaCost.Should().Be("{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FaerieSeer_IsBlue()
    {
        var c = FaerieSeerFactory.Create(_alice);

        // Color is derived from mana cost — the {U} pip makes it blue (CR 202.2c).
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue,
            "Faerie Seer has a {U} pip in its mana cost");
        colors.Should().HaveCount(1, "only one color identity");
    }

    [Fact]
    public void FaerieSeer_ManaValue_IsOne()
    {
        var c = FaerieSeerFactory.Create(_alice);

        // {U} = mana value 1 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(1, "CR 202.3 — {U} has mana value 1");
    }

    [Fact]
    public void FaerieSeer_HasFlyingKeyword()
    {
        var c = FaerieSeerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Faerie Seer has Flying");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieSeer_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = FaerieSeerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB scry trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // ETB Scry 2 — keep on top
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieSeer_EtbTrigger_Scry2_KeepOnTop_LeavesCardOnTop()
    {
        var alice = new Player("Alice", 20);

        var cardA = new Creature("CardA", "{U}", 1, 1);
        var cardB = new Creature("CardB", "{G}", 1, 1);
        alice.Zones.Library.AddCard(cardA); // top
        alice.Zones.Library.AddCard(cardB); // second

        // Script: keep cardA on top, send cardB to bottom.
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { cardB },
            TopOrder: new[] { cardA }));
        AgentRegistry.Set(alice, agent);

        var seer = FaerieSeerFactory.Create(alice);
        var etb = seer.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var lib = alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2, "scry 2 only reorders; no card leaves the library");
        lib.First().Should().BeSameAs(cardA, "cardA was kept on top");
    }

    // -----------------------------------------------------------------------
    // ETB Scry 2 — all to bottom
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieSeer_EtbTrigger_Scry2_AllBottom_KeepsLibrarySize()
    {
        var alice = new Player("Alice", 20);

        var cardA = new Creature("CardA", "{U}", 1, 1);
        var cardB = new Creature("CardB", "{G}", 1, 1);
        alice.Zones.Library.AddCard(cardA); // top
        alice.Zones.Library.AddCard(cardB); // second

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { cardA, cardB },
            TopOrder: Array.Empty<ICard>()));
        AgentRegistry.Set(alice, agent);

        var seer = FaerieSeerFactory.Create(alice);
        var etb = seer.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var lib = alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2, "scry 2 only reorders; no card leaves the library");
        lib.Should().Contain(cardA).And.Contain(cardB,
            "both cards remain in the library after scry");
    }

    // -----------------------------------------------------------------------
    // ETB Scry on empty library — no crash
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieSeer_EtbTrigger_EmptyLibrary_NoCrash()
    {
        var alice = new Player("Alice", 20);
        // Library is intentionally empty.

        var seer = FaerieSeerFactory.Create(alice);
        var etb = seer.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow("CR 701.20 — scry N looks at up to N cards; an empty library is a no-op");
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
