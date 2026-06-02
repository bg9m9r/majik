using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HallowedMoonlightFactory"/>.
///
/// Card: Hallowed Moonlight — Instant {1}{W} (Magic Origins).
/// Oracle text (verified against Scryfall):
///   "Until end of turn, if a creature would enter and it wasn't cast,
///    exile it instead.
///    Draw a card."
///
/// Shape combines three primitives that already ship:
///   - The Containment Priest exile-non-cast-creature-ETB predicate
///     (<see cref="ContainmentPriestExileReplacementEffect"/> — CR 614).
///   - The Anger of the Gods EOT-expirable, spell-registered replacement
///     wrapper (<see cref="AngerOfTheGodsFactory"/> — CR 514.2 cleanup drop).
///   - The Deadly Dispute cantrip (<see cref="Fx.DrawCards"/> — CR 121.1).
///
/// Covers:
///   - Identity (name, type, mana cost, white, owner/controller) +
///     NamedCardFactory dispatch (built from the embedded JSON definition).
///   - Resolve registers an exile-instead replacement that rewrites a
///     non-cast creature's enter-the-battlefield intent to exile.
///   - Cast creatures (WasCast = true) are unaffected.
///   - Non-creatures are unaffected.
///   - The replacement is EOT-expirable — the cleanup sweep drops it.
///   - Resolve draws one card (cantrip).
///   - Resolve with a null bus still draws (rider half skipped quietly).
/// </summary>
public class HallowedMoonlightTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = HallowedMoonlightFactory.Create(_alice);

        card.Name.Should().Be("Hallowed Moonlight");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HallowedMoonlight_IsWhite()
    {
        var card = HallowedMoonlightFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.White,
            "the {W} pip makes it white");
        colors.Should().NotContain(Majik.Core.ValueObjects.ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HallowedMoonlight()
    {
        var card = NamedCardFactory.Create("Hallowed Moonlight", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Hallowed Moonlight");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile-instead replacement
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_RegistersReplacement_ExilingNonCastCreatureEnters()
    {
        var bus = new ReplacementBus();
        SeedLibraryCard(_alice, "Top");

        foreach (var e in HallowedMoonlightFactory.BuildResolveEffect(_alice, bus))
            e.Execute();

        // A non-cast creature would enter the battlefield (reanimation,
        // blink, Sneak Attack, token, etc.) — exiled instead (CR 614).
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            "Hallowed Moonlight exiles non-cast creatures that would enter");
    }

    [Fact]
    public void Resolve_DoesNotExile_CastCreatures()
    {
        var bus = new ReplacementBus();
        SeedLibraryCard(_alice, "Top");

        foreach (var e in HallowedMoonlightFactory.BuildResolveEffect(_alice, bus))
            e.Execute();

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        // WasCast = true — this creature was cast normally; unaffected.
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            WasCast: true);

        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Battlefield,
            "cast creatures are unaffected by Hallowed Moonlight");
    }

    [Fact]
    public void Resolve_DoesNotExile_NonCreatures()
    {
        var bus = new ReplacementBus();
        SeedLibraryCard(_alice, "Top");

        foreach (var e in HallowedMoonlightFactory.BuildResolveEffect(_alice, bus))
            e.Execute();

        var artifact = new Artifact("Mox Opal", "{0}");
        var intent = new ZoneMoveIntent(
            Card: artifact,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Battlefield,
            "non-creature cards are not affected by Hallowed Moonlight");
    }

    [Fact]
    public void Resolve_ReplacementIsEndOfTurnExpirable()
    {
        var bus = new ReplacementBus();
        SeedLibraryCard(_alice, "Top");

        foreach (var e in HallowedMoonlightFactory.BuildResolveEffect(_alice, bus))
            e.Execute();

        // Before cleanup — a non-cast creature entering is exiled.
        var before = new ZoneMoveIntent(
            new Creature("Tarmogoyf", "{1}{G}", 0, 1),
            ZoneType.Graveyard, ZoneType.Battlefield, WasCast: false);
        bus.Apply(before)!.ToZone.Should().Be(ZoneType.Exile);

        // Cleanup sweep — CR 514.2.
        bus.ExpireEndOfTurn();

        // After cleanup — the same kind of intent passes through unchanged.
        var after = new ZoneMoveIntent(
            new Creature("Tarmogoyf", "{1}{G}", 0, 1),
            ZoneType.Graveyard, ZoneType.Battlefield, WasCast: false);
        bus.Apply(after)!.ToZone.Should().Be(ZoneType.Battlefield,
            "the EOT sweep dropped the IEndOfTurnExpirable replacement (\"until end of turn\")");
    }

    // -----------------------------------------------------------------------
    // Resolve — cantrip
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsOneCard()
    {
        var top = SeedLibraryCard(_alice, "Top1");
        SeedLibraryCard(_alice, "Top2");

        var bus = new ReplacementBus();
        foreach (var e in HallowedMoonlightFactory.BuildResolveEffect(_alice, bus))
            e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "Hallowed Moonlight draws a card (CR 121.1)");
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WithoutReplacements_StillDraws()
    {
        var top = SeedLibraryCard(_alice, "Top");

        var act = () =>
        {
            foreach (var e in HallowedMoonlightFactory.BuildResolveEffect(_alice, replacements: null))
                e.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "the cantrip still draws without a bus");
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
