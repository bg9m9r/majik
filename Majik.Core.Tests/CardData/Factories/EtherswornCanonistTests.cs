using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Ethersworn Canonist (Alara Reborn, {1}{W}).
///
/// Oracle (verified against Scryfall):
///   "Each player who has cast a nonartifact spell this turn can't cast
///    additional nonartifact spells."
///
/// Coverage:
///   * Identity: Artifact Creature — Human Cleric {1}{W} 2/2.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * First nonartifact spell OK; a second nonartifact spell is blocked
///     (CR 605/616 / 601.3).
///   * An ARTIFACT spell is still castable after a nonartifact spell — and
///     casting it does NOT itself trip the restriction.
///   * Symmetric (CR 109.5): each player is restricted independently after
///     their own first nonartifact cast.
///   * Per-turn reset (CR 514.2): the restriction clears at turn start.
///   * Restriction lifts when the Canonist leaves the battlefield.
///   * Single-arg dispatch path registers no rail.
/// </summary>
[Trait("Color", "W")]
public class EtherswornCanonistTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;
    private readonly ActionValidator _validator = new();

    public EtherswornCanonistTests()
    {
        _zones = new ZoneService(_bus, _replacements);
        CastingRestrictions.Clear();
    }

    public void Dispose() => CastingRestrictions.Clear();

    private IReadOnlyList<Player> AllPlayers() => new[] { _alice, _bob };

    private static Creature Nonartifact() => new("Grizzly Bears", "{1}{G}", 2, 2);
    private static Artifact ArtifactSpell() => new("Sol Ring", "{1}");

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var canonist = EtherswornCanonistFactory.Create(_alice);

        canonist.Name.Should().Be("Ethersworn Canonist");
        canonist.HasType(CardType.Creature).Should().BeTrue();
        canonist.HasType(CardType.Artifact).Should().BeTrue();
        canonist.HasSubtype(CardSubtype.Human).Should().BeTrue();
        canonist.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        canonist.ManaCost.Should().Be("{1}{W}");
        canonist.ManaCostValue.Generic.Should().Be(1);
        canonist.ManaCostValue.White.Should().Be(1);
        canonist.Power.Should().Be(2);
        canonist.Toughness.Should().Be(2);
        canonist.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Dispatch_ByName_ProducesCanonist()
    {
        var card = NamedCardFactory.Create("Ethersworn Canonist", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Ethersworn Canonist");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Battlefield helper
    // -----------------------------------------------------------------------

    private Creature CanonistOnBattlefield()
    {
        var canonist = EtherswornCanonistFactory.Create(_alice, _bus, AllPlayers);
        _alice.Zones.Library.AddCard(canonist);
        canonist.SetZone(ZoneType.Library);
        _zones.MoveCard(canonist, ZoneType.Library, ZoneType.Battlefield);
        return canonist;
    }

    // -----------------------------------------------------------------------
    // Nonartifact restriction (CR 605/616 / 601.3 / 109.5 / 514.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void FirstNonartifactSpell_OK_SecondBlocked()
    {
        CanonistOnBattlefield();

        var first = new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true);
        _validator.ValidateAction(first).IsValid.Should().BeTrue(
            "the first nonartifact spell of the turn is allowed");

        // Simulate SpellCastFlow recording the nonartifact cast.
        CastingRestrictions.RecordNonartifactSpellCast(_bob);

        var second = new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true);
        var result = _validator.ValidateAction(second);
        result.IsValid.Should().BeFalse(
            "a second nonartifact spell is blocked once a nonartifact spell has been cast (CR 605/616)");
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void ArtifactSpell_StillCastable_AfterNonartifactSpell()
    {
        CanonistOnBattlefield();

        // Bob casts a nonartifact spell first.
        CastingRestrictions.RecordNonartifactSpellCast(_bob);

        // An artifact spell is unaffected by the restriction (CR 605/616).
        var artifact = new CastSpellAction(ArtifactSpell(), _bob, sorcerySpeedAvailable: true);
        _validator.ValidateAction(artifact).IsValid.Should().BeTrue(
            "Ethersworn Canonist restricts only NONARTIFACT spells; artifact spells stay castable");
    }

    [Fact]
    public void ArtifactSpell_DoesNotTripRestriction()
    {
        CanonistOnBattlefield();

        // Casting an artifact spell does not increment the nonartifact counter
        // (SpellCastFlow only records nonartifact casts), so a subsequent
        // nonartifact spell is still legal.
        CastingRestrictions.HasCastNonartifactSpellThisTurn(_bob).Should().BeFalse();

        var nonartifact = new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true);
        _validator.ValidateAction(nonartifact).IsValid.Should().BeTrue(
            "having cast only an artifact spell, a nonartifact spell is still legal");
    }

    [Fact]
    public void Restriction_IsSymmetric_PerPlayer()
    {
        CanonistOnBattlefield();

        // Alice casts a nonartifact spell — she becomes restricted.
        CastingRestrictions.RecordNonartifactSpellCast(_alice);

        var aliceSecond = new CastSpellAction(Nonartifact(), _alice, sorcerySpeedAvailable: true);
        _validator.ValidateAction(aliceSecond).IsValid.Should().BeFalse(
            "Alice is restricted after her own first nonartifact spell (CR 109.5 — symmetric)");

        var bobFirst = new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true);
        _validator.ValidateAction(bobFirst).IsValid.Should().BeTrue(
            "Bob has not cast a nonartifact spell yet — he is unrestricted");
    }

    [Fact]
    public void Restriction_ResetsAtTurnStart()
    {
        CanonistOnBattlefield();

        CastingRestrictions.RecordNonartifactSpellCast(_bob);
        _validator.ValidateAction(
            new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true))
            .IsValid.Should().BeFalse();

        // New turn — the "this turn" tally refreshes (CR 514.2).
        _bus.Publish(new TurnStartedEvent(_bob, 2));

        _validator.ValidateAction(
            new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true))
            .IsValid.Should().BeTrue(
                "the nonartifact restriction clears at turn start");
    }

    [Fact]
    public void Restriction_LiftsWhenCanonistLeavesBattlefield()
    {
        var canonist = CanonistOnBattlefield();

        CastingRestrictions.RecordNonartifactSpellCast(_bob);
        _validator.ValidateAction(
            new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true))
            .IsValid.Should().BeFalse();

        // Canonist dies — the static stops applying even though Bob already
        // cast a nonartifact spell this turn.
        _zones.MoveCard(canonist, ZoneType.Battlefield, ZoneType.Graveyard);

        _validator.ValidateAction(
            new CastSpellAction(Nonartifact(), _bob, sorcerySpeedAvailable: true))
            .IsValid.Should().BeTrue(
                "the restriction lifts when Ethersworn Canonist leaves the battlefield");
    }

    [Fact]
    public void SingleArgPath_RegistersNoRail()
    {
        EtherswornCanonistFactory.Create(_alice);

        CastingRestrictions.RecordNonartifactSpellCast(_alice);
        _validator.ValidateAction(
            new CastSpellAction(Nonartifact(), _alice, sorcerySpeedAvailable: true))
            .IsValid.Should().BeTrue(
                "no restriction is registered on the single-arg path");
    }
}
