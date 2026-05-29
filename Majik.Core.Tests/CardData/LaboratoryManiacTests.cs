using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LaboratoryManiacFactory"/>.
///
/// Card: Laboratory Maniac — Creature — Human Wizard {2}{U} 2/2 (Innistrad).
///   "If you would draw a card while your library has no cards in it,
///    you win the game instead."
///
/// Covers:
///   - Identity: name, mana cost, type, power/toughness, subtypes,
///     owner/controller, MV 3.
///   - NamedCardFactory dispatch (dispatcher shape test).
///   - Shape-only Create(Player) does not register a replacement.
///   - Win path: controller with empty library who would draw a card
///     triggers the replacement — opponents marked lost, draw cancelled,
///     no MarkTriedToDrawFromEmptyLibrary flag (CR 704.5b suppressed).
///   - Normal draw path: library non-empty, replacement does not apply,
///     draw resolves normally.
///   - LTB: once Laboratory Maniac leaves the battlefield (zone != Battlefield)
///     the replacement self-gates out — normal empty-library loss resumes.
///   - Multiple opponents: all are marked lost on win.
///   - Controller is not marked lost on own win.
///   - No opponents supplied (shape path): replacement still cancels the
///     draw, no loss flag stamped, no crash.
/// </summary>
public class LaboratoryManiacTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _charlie = new("Charlie", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void LaboratoryManiac_Identity()
    {
        var card = LaboratoryManiacFactory.Create(_alice);

        card.Name.Should().Be("Laboratory Maniac");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LaboratoryManiac_PowerToughness_2_2()
    {
        var card = LaboratoryManiacFactory.Create(_alice);

        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
    }

    [Fact]
    public void LaboratoryManiac_SubtypesAreHumanWizard()
    {
        var card = LaboratoryManiacFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Human);
        card.Subtypes.Should().Contain(CardSubtype.Wizard);
    }

    [Fact]
    public void LaboratoryManiac_ManaValue_IsThree()
    {
        var card = LaboratoryManiacFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(3,
            "{2}{U} = 2 generic + 1 blue = MV 3");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LaboratoryManiac()
    {
        var card = NamedCardFactory.Create("Laboratory Maniac", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Laboratory Maniac");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{U}");
    }

    // -----------------------------------------------------------------------
    // Shape-only path — no replacement registered
    // -----------------------------------------------------------------------

    [Fact]
    public void ShapeOnly_NoReplacementBus_EmptyLibraryDraw_StampsLossFlag()
    {
        // Create(Player) shape-only — no bus attached to alice.
        var card = LaboratoryManiacFactory.Create(_alice);
        PutOnBattlefield(_alice, card);

        // Library is empty; alice has no replacement bus.
        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().BeEmpty("no card to draw");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "shape-only path: no replacement registered, loss flag stamped normally");
    }

    // -----------------------------------------------------------------------
    // Win path — empty library draw replaced by win
    // -----------------------------------------------------------------------

    [Fact]
    public void EmptyLibraryDraw_ControllerWins_OpponentsMarkedLost()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var card = LaboratoryManiacFactory.Create(_alice, bus, new[] { _bob });
        PutOnBattlefield(_alice, card);

        // Library is empty.
        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().BeEmpty("replacement cancelled the draw");
        _bob.HasLost.Should().BeTrue("opponent loses when Laboratory Maniac fires");
        _alice.HasLost.Should().BeFalse("controller is not marked lost");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "CR 704.5b loss flag suppressed — replacement consumed the draw");
    }

    [Fact]
    public void EmptyLibraryDraw_MultipleOpponents_AllMarkedLost()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var card = LaboratoryManiacFactory.Create(_alice, bus, new[] { _bob, _charlie });
        PutOnBattlefield(_alice, card);

        Fx.DrawCards(_alice, 1);

        _bob.HasLost.Should().BeTrue();
        _charlie.HasLost.Should().BeTrue();
        _alice.HasLost.Should().BeFalse();
    }

    [Fact]
    public void EmptyLibraryDraw_NoOpponents_ReplacementCancelsDraw_NoLossFlag()
    {
        // Shape path with bus but no opponents — win is unobservable but
        // the replacement must still cancel the draw.
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var card = LaboratoryManiacFactory.Create(_alice, bus, opponents: null);
        PutOnBattlefield(_alice, card);

        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().BeEmpty("replacement cancelled the draw");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "even with no opponents, the draw is cancelled, not the loss");
    }

    // -----------------------------------------------------------------------
    // Normal draw path — library non-empty
    // -----------------------------------------------------------------------

    [Fact]
    public void NonEmptyLibrary_DrawResolvesNormally_ReplacementDoesNotApply()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var card = LaboratoryManiacFactory.Create(_alice, bus, new[] { _bob });
        PutOnBattlefield(_alice, card);

        FillLibrary(_alice, 3);

        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().HaveCount(1, "library non-empty: normal draw path");
        _bob.HasLost.Should().BeFalse("win not triggered on normal draw");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void NonEmptyLibrary_DrawMultiple_OnlyLastDrawTriggersReplacementWhenLibraryDepleted()
    {
        // Draw 3 from a 2-card library: 2 normal draws, then 1 empty-lib
        // draw that fires the Lab Maniac replacement.
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var card = LaboratoryManiacFactory.Create(_alice, bus, new[] { _bob });
        PutOnBattlefield(_alice, card);

        FillLibrary(_alice, 2);

        var drawn = Fx.DrawCards(_alice, 3);

        drawn.Should().HaveCount(2, "only 2 cards available in library");
        _bob.HasLost.Should().BeTrue(
            "third draw hit empty library with Lab Maniac on battlefield");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "replacement consumed the empty-library draw instead of stamping loss");
    }

    // -----------------------------------------------------------------------
    // LTB — Maniac not on battlefield, replacement self-gates out
    // -----------------------------------------------------------------------

    [Fact]
    public void LaboratoryManiac_NotOnBattlefield_EmptyLibraryDraw_StampsLossNormally()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var card = LaboratoryManiacFactory.Create(_alice, bus, new[] { _bob });

        // Simulate LTB: card moves to graveyard, NOT on battlefield.
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        // Empty library draw. Replacement self-gates on zone check.
        Fx.DrawCards(_alice, 1);

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "Lab Maniac not on battlefield: normal CR 704.5b loss flag fires");
        _bob.HasLost.Should().BeFalse("win not triggered when Maniac is off battlefield");
    }

    [Fact]
    public void LaboratoryManiac_InHand_EmptyLibraryDraw_StampsLossNormally()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var card = LaboratoryManiacFactory.Create(_alice, bus, new[] { _bob });

        // Card is in hand — not yet played.
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        Fx.DrawCards(_alice, 1);

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "Lab Maniac in hand (not on battlefield): CR 704.5b fires normally");
        _bob.HasLost.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PutOnBattlefield(Player player, Creature card)
    {
        player.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static void FillLibrary(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Lib-{i}", "{0}");
            c.SetOwner(player);
            player.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
