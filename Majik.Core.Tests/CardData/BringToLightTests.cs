using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bring to Light (Battle for Zendikar, {2}{G}{W}{U}, Sorcery).
///
/// Oracle: "Converge — Search your library for a creature, instant, or
/// sorcery card with mana value less than or equal to the number of
/// colors of mana spent to cast this spell, exile it, then shuffle. You
/// may cast that card without paying its mana cost if five or more
/// colors of mana were spent to cast this spell."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Converge tutor with explicit selector — picks a Creature, Instant
///     or Sorcery within the cap; exiles + shuffles.
///   - Cap clamp: picks above the cap are skipped (CR 608.2b — only the
///     printed-legal predicate applies at resolution).
///   - Type filter: non-creature / non-instant / non-sorcery picks are
///     skipped.
///   - Default cap = 3 distinct colored pips ({G}, {W}, {U}) — printed
///     minimum when no provenance ledger is supplied.
///   - Legal-candidates helper exposes the same filter for bots / tests.
/// </summary>
public class BringToLightTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void BringToLight_IsSorcery_At2GWU()
    {
        var s = BringToLightFactory.Create(_alice);

        s.Name.Should().Be("Bring to Light");
        s.ManaCost.Should().Be("{2}{G}{W}{U}");
        s.HasType(CardType.Sorcery).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesBringToLight()
    {
        var card = NamedCardFactory.Create("Bring to Light", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Bring to Light");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{G}{W}{U}");
    }

    // ── Resolve — tutor + exile + shuffle ───────────────────────────────

    [Fact]
    public void Resolve_PicksCreatureWithinCap_ExilesIt()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var libBefore = _alice.Zones.Library.GetCards().Count();

        var effects = BringToLightFactory.BuildResolveEffect(
            _alice,
            colorsSpentProvider: () => 3,
            tutorSelector: (_, _) => bear);

        foreach (var fx in effects) fx.Execute();

        bear.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
        _alice.Zones.Library.GetCards().Should().NotContain(bear);
        _alice.Zones.Library.GetCards().Count().Should().Be(libBefore - 1);
    }

    [Fact]
    public void Resolve_PicksInstantWithinCap_ExilesIt()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var effects = BringToLightFactory.BuildResolveEffect(
            _alice,
            colorsSpentProvider: () => 1,
            tutorSelector: (_, _) => bolt);

        foreach (var fx in effects) fx.Execute();

        bolt.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Resolve_PicksSorceryWithinCap_ExilesIt()
    {
        var ritual = new Sorcery("Dark Ritual", "{B}");
        ritual.SetOwner(_alice);
        _alice.Zones.Library.AddCard(ritual);
        ritual.SetZone(ZoneType.Library);

        var effects = BringToLightFactory.BuildResolveEffect(
            _alice,
            colorsSpentProvider: () => 1,
            tutorSelector: (_, _) => ritual);

        foreach (var fx in effects) fx.Execute();

        ritual.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Resolve_PickOverCap_IsSkipped_NoExile()
    {
        // Cap = 2; the pick has mv = 4. CR 608.2b: illegal pick → clean
        // no-op. The card stays in the library, but shuffle still runs.
        var titan = new Creature("Primeval Titan", "{4}{G}{G}", 6, 6);
        titan.SetOwner(_alice);
        _alice.Zones.Library.AddCard(titan);
        titan.SetZone(ZoneType.Library);

        var effects = BringToLightFactory.BuildResolveEffect(
            _alice,
            colorsSpentProvider: () => 2,
            tutorSelector: (_, _) => titan);

        foreach (var fx in effects) fx.Execute();

        titan.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Exile.GetCards().Should().NotContain(titan);
    }

    [Fact]
    public void Resolve_PickOfWrongType_Land_IsSkipped()
    {
        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var effects = BringToLightFactory.BuildResolveEffect(
            _alice,
            colorsSpentProvider: () => 5,
            tutorSelector: (_, _) => forest);

        foreach (var fx in effects) fx.Execute();

        forest.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Exile.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void Resolve_NoPick_ClearLibrary_IsNoOp()
    {
        // Empty library / no legal pick → shuffle still runs cleanly.
        var effects = BringToLightFactory.BuildResolveEffect(
            _alice,
            colorsSpentProvider: () => 3,
            tutorSelector: (_, _) => null);

        var act = () =>
        {
            foreach (var fx in effects) fx.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // ── Default cap ─────────────────────────────────────────────────────

    [Fact]
    public void DefaultColorsSpent_IsThree()
    {
        BringToLightFactory.DefaultColorsSpent.Should().Be(3,
            "the printed cost has 3 distinct colored pips ({G}, {W}, {U})");
    }

    [Fact]
    public void FreeCastThreshold_IsFive()
    {
        BringToLightFactory.FreeCastThreshold.Should().Be(5);
    }

    // ── Legal-pick predicate / helper ───────────────────────────────────

    [Fact]
    public void IsLegalPick_AllowsCreatureInstantSorcery_WithinCap()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        var bolt = new Instant("Bolt", "{R}");
        var ritual = new Sorcery("Ritual", "{B}");

        BringToLightFactory.IsLegalPick(bear, cap: 2).Should().BeTrue();
        BringToLightFactory.IsLegalPick(bolt, cap: 1).Should().BeTrue();
        BringToLightFactory.IsLegalPick(ritual, cap: 1).Should().BeTrue();
    }

    [Fact]
    public void IsLegalPick_RejectsOverCap()
    {
        var titan = new Creature("Primeval Titan", "{4}{G}{G}", 6, 6);
        BringToLightFactory.IsLegalPick(titan, cap: 5).Should().BeFalse();
        BringToLightFactory.IsLegalPick(titan, cap: 6).Should().BeTrue();
    }

    [Fact]
    public void IsLegalPick_RejectsWrongType()
    {
        var artifact = new Artifact("Mind Stone", "{2}");
        var land = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        BringToLightFactory.IsLegalPick(artifact, cap: 5).Should().BeFalse();
        BringToLightFactory.IsLegalPick(land, cap: 5).Should().BeFalse();
    }

    [Fact]
    public void LegalCandidates_FiltersLibrary_ByCapAndType()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);

        var titan = new Creature("Primeval Titan", "{4}{G}{G}", 6, 6);
        titan.SetOwner(_alice);
        _alice.Zones.Library.AddCard(titan);

        var bolt = new Instant("Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);

        var stone = new Artifact("Mind Stone", "{2}");
        stone.SetOwner(_alice);
        _alice.Zones.Library.AddCard(stone);

        var picks = BringToLightFactory.LegalCandidates(_alice, cap: 3);

        picks.Should().Contain(bear);   // mv 2 ≤ 3, creature
        picks.Should().Contain(bolt);   // mv 1 ≤ 3, instant
        picks.Should().NotContain(titan); // mv 6 > 3
        picks.Should().NotContain(stone); // wrong type
    }

    // ── SpellDefinition shape ───────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasNoTargetRequests()
    {
        var def = BringToLightFactory.BuildSpellDefinition(_alice);

        def.TargetRequests.Should().BeEmpty(
            "the tutor resolves via library search, not a target");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }
}
