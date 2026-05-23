using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WurmcoilEngineFactory"/>.
///
/// Covers:
/// - Identity (name, types Artifact + Creature, P/T 6/6, Phyrexian + Wurm
///   subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch (returns a Creature with the Artifact card
///   type also stamped).
/// - Deathtouch + Lifelink keyword markers (CR 702.2 + 702.15) — both
///   directly on the abilities collection and via the combat helpers.
/// - Dies trigger fires on Battlefield → Graveyard CardMovedEvent and
///   creates two 3/3 Phyrexian Wurm artifact creature tokens — one with
///   Deathtouch and one with Lifelink (CR 603.6c / 700.4).
/// </summary>
public class WurmcoilEngineTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WurmcoilEngine_Identity()
    {
        var c = WurmcoilEngineFactory.Create(_alice);

        c.Name.Should().Be("Wurmcoil Engine");
        c.HasType(CardType.Creature).Should().BeTrue("Wurmcoil Engine is a Creature");
        c.HasType(CardType.Artifact).Should().BeTrue("Wurmcoil Engine is an Artifact (CR 301.1 / 302.1)");
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue("Wurmcoil Engine is a Phyrexian");
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue("Wurmcoil Engine is a Wurm");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{6}");
    }

    [Fact]
    public void WurmcoilEngine_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Wurmcoil Engine", _alice);

        c.Should().BeOfType<Creature>("Wurmcoil Engine is a Creature shell with Artifact stamped on top");
        c.Name.Should().Be("Wurmcoil Engine");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Keywords — Deathtouch + Lifelink (CR 702.2 + 702.15)
    // -----------------------------------------------------------------------

    [Fact]
    public void WurmcoilEngine_HasDeathtouchAndLifelinkKeywords()
    {
        var c = WurmcoilEngineFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Deathtouch", "CR 702.2 — Deathtouch is a printed evergreen on Wurmcoil Engine");
        keywords.Should().Contain("Lifelink", "CR 702.15 — Lifelink is a printed evergreen on Wurmcoil Engine");

        CombatAbilities.HasDeathtouch(c).Should().BeTrue();
        CombatAbilities.HasLifelink(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Dies trigger (CR 603.6c / 700.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void WurmcoilEngine_Dies_CreatesTwoPhyrexianWurmArtifactCreatureTokens()
    {
        var alice = new Player("Alice", 20);
        var wurmcoil = WurmcoilEngineFactory.Create(alice);

        // Place Wurmcoil Engine on the battlefield, then move it to the
        // graveyard to simulate "dies" (CR 700.4).
        alice.Zones.Battlefield.AddCard(wurmcoil);
        wurmcoil.SetZone(ZoneType.Battlefield);

        // Move to graveyard (raw zone move) so the trigger's source-zone
        // guard (activeZones = {Battlefield, Graveyard}) is satisfied.
        alice.Zones.Battlefield.RemoveCard(wurmcoil);
        alice.Zones.Graveyard.AddCard(wurmcoil);
        wurmcoil.SetZone(ZoneType.Graveyard);

        var dies = wurmcoil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in dies.Effects) effect.Execute();

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Phyrexian Wurm")
            .ToList();

        tokens.Should().HaveCount(2,
            "the dies trigger creates exactly two Phyrexian Wurm tokens");
        tokens.Should().AllSatisfy(t =>
        {
            t.BasePower.Should().Be(3);
            t.BaseToughness.Should().Be(3);
            t.HasType(CardType.Creature).Should().BeTrue();
            t.HasType(CardType.Artifact).Should().BeTrue("each token is an artifact creature token");
            t.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
            t.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
            t.Controller.Should().BeSameAs(alice, "tokens enter under Wurmcoil's controller (CR 111.6)");
        });
    }

    [Fact]
    public void WurmcoilEngine_Dies_TokensSplitDeathtouchAndLifelink()
    {
        var alice = new Player("Alice", 20);
        var wurmcoil = WurmcoilEngineFactory.Create(alice);

        alice.Zones.Battlefield.AddCard(wurmcoil);
        wurmcoil.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.RemoveCard(wurmcoil);
        alice.Zones.Graveyard.AddCard(wurmcoil);
        wurmcoil.SetZone(ZoneType.Graveyard);

        var dies = wurmcoil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in dies.Effects) effect.Execute();

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Phyrexian Wurm")
            .ToList();

        tokens.Should().HaveCount(2);

        // Exactly one of the tokens has Deathtouch, exactly one has Lifelink
        // (no overlap — they are two separate tokens with one keyword each).
        tokens.Count(CombatAbilities.HasDeathtouch).Should().Be(1,
            "exactly one of the two Wurm tokens has Deathtouch");
        tokens.Count(CombatAbilities.HasLifelink).Should().Be(1,
            "exactly one of the two Wurm tokens has Lifelink");

        var deathtouchToken = tokens.Single(CombatAbilities.HasDeathtouch);
        var lifelinkToken = tokens.Single(CombatAbilities.HasLifelink);
        deathtouchToken.Should().NotBeSameAs(lifelinkToken,
            "Deathtouch and Lifelink are split across two distinct tokens");
    }

    [Fact]
    public void WurmcoilEngine_DiesTrigger_ConditionMatchesBattlefieldToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var wurmcoil = WurmcoilEngineFactory.Create(alice);

        // Place on battlefield first; the trigger's zone-guard requires
        // the source's current zone to be in activeZones at evaluation time.
        wurmcoil.SetZone(ZoneType.Battlefield);

        var dies = wurmcoil.Abilities.OfType<TriggeredAbility>().Single();

        var matchingEvent = new Majik.Core.Events.CardMovedEvent(
            wurmcoil, ZoneType.Battlefield, ZoneType.Graveyard);
        dies.IsTriggered(matchingEvent).Should().BeTrue(
            "Battlefield → Graveyard for the source matches the dies condition (CR 700.4)");

        var bounceEvent = new Majik.Core.Events.CardMovedEvent(
            wurmcoil, ZoneType.Battlefield, ZoneType.Hand);
        dies.IsTriggered(bounceEvent).Should().BeFalse(
            "Battlefield → Hand is not a death — the dies trigger must not fire");
    }
}
