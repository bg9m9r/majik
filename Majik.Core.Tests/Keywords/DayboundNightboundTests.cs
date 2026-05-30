using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Xunit;

/// <summary>
/// Unit tests for <see cref="DayboundNightbound"/> — the daybound/nightbound
/// keyword transform logic (CR 702.145). Tested in isolation: a permanent
/// carries a Daybound (front face) or Nightbound (back face) marker plus an
/// <see cref="MdfcState"/>; the helper transforms it on day/night changes
/// (CR 702.145c/f), on entry (CR 702.145b), and reports whether a permanent
/// makes it day/night (CR 702.145d/g).
/// </summary>
public class DayboundNightboundTests
{
    private static Player NewPlayer() => new("Alice", 20);

    /// <summary>
    /// A minimal "Werewolf" transform card: front face daybound, back face
    /// nightbound, with an MdfcState so transform is observable. Starts on
    /// the front face.
    /// </summary>
    private static Creature NewWerewolf(Player owner)
    {
        var c = new Creature("Day Wolf", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Werewolf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.MdfcState = new MdfcState("Day Wolf", "Night Wolf");
        c.AddAbility(new KeywordAbility("Daybound", c, owner));
        c.AddAbility(new KeywordAbility("Nightbound", c, owner));
        return c;
    }

    // ---------------------------------------------------------------
    // Marker detection.
    // ---------------------------------------------------------------

    [Fact]
    public void DetectsDayboundAndNightboundMarkers()
    {
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.HasDaybound(c).Should().BeTrue();
        DayboundNightbound.HasNightbound(c).Should().BeTrue();
    }

    [Fact]
    public void PlainCreatureHasNoDayNightKeywords()
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);

        DayboundNightbound.HasDaybound(c).Should().BeFalse();
        DayboundNightbound.HasNightbound(c).Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // CR 702.145c — front-face daybound permanent transforms to back when
    // it becomes night.
    // ---------------------------------------------------------------

    [Fact]
    public void BecomesNight_FrontFaceDaybound_TransformsToBack()
    {
        var c = NewWerewolf(NewPlayer());
        c.MdfcState!.IsBackFace.Should().BeFalse();

        DayboundNightbound.OnDayNightChanged(new[] { c }, DayNightDesignation.Night);

        c.MdfcState!.IsBackFace.Should().BeTrue("CR 702.145c — becomes night, transform to back");
    }

    [Fact]
    public void BecomesNight_AlreadyBackFace_StaysBack()
    {
        var c = NewWerewolf(NewPlayer());
        c.MdfcState!.Transform(); // already on back

        DayboundNightbound.OnDayNightChanged(new[] { c }, DayNightDesignation.Night);

        c.MdfcState!.IsBackFace.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // CR 702.145f — back-face nightbound permanent transforms to front when
    // it becomes day.
    // ---------------------------------------------------------------

    [Fact]
    public void BecomesDay_BackFaceNightbound_TransformsToFront()
    {
        var c = NewWerewolf(NewPlayer());
        c.MdfcState!.Transform(); // on back face (Nightbound side)
        c.MdfcState!.IsBackFace.Should().BeTrue();

        DayboundNightbound.OnDayNightChanged(new[] { c }, DayNightDesignation.Day);

        c.MdfcState!.IsBackFace.Should().BeFalse("CR 702.145f — becomes day, transform to front");
    }

    [Fact]
    public void BecomesDay_AlreadyFrontFace_StaysFront()
    {
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.OnDayNightChanged(new[] { c }, DayNightDesignation.Day);

        c.MdfcState!.IsBackFace.Should().BeFalse();
    }

    [Fact]
    public void BecomesNeither_NoTransform()
    {
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.OnDayNightChanged(new[] { c }, DayNightDesignation.Neither);

        c.MdfcState!.IsBackFace.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // CR 702.145b — a daybound permanent enters transformed if it's night.
    // ---------------------------------------------------------------

    [Fact]
    public void Enters_WhenNight_DayboundEntersTransformed()
    {
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.OnEnter(c, DayNightDesignation.Night);

        c.MdfcState!.IsBackFace.Should().BeTrue("CR 702.145b — enters transformed when it's night");
    }

    [Fact]
    public void Enters_WhenDay_DayboundEntersFront()
    {
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.OnEnter(c, DayNightDesignation.Day);

        c.MdfcState!.IsBackFace.Should().BeFalse();
    }

    [Fact]
    public void Enters_WhenNeither_DayboundEntersFront()
    {
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.OnEnter(c, DayNightDesignation.Neither);

        c.MdfcState!.IsBackFace.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // CR 702.145d — any time a player controls a daybound permanent and it's
    // neither day nor night, it becomes day. CR 702.145g — a nightbound-only
    // permanent (no daybound on battlefield) makes it night.
    // ---------------------------------------------------------------

    [Fact]
    public void DayboundPermanent_MakesItDay_WhenNeither()
    {
        var c = NewWerewolf(NewPlayer()); // front face = daybound active

        DayboundNightbound.EntryDesignation(c, DayNightDesignation.Neither)
            .Should().Be(DayNightDesignation.Day, "CR 702.145d — daybound permanent makes it day");
    }

    [Fact]
    public void DayboundPermanent_DoesNotChange_WhenAlreadyDay()
    {
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.EntryDesignation(c, DayNightDesignation.Day)
            .Should().Be(DayNightDesignation.Day);
    }

    [Fact]
    public void DayboundPermanent_DoesNotChange_WhenNight()
    {
        // CR 702.145d only fires when it's NEITHER — a daybound permanent
        // does not make it day when it's already night.
        var c = NewWerewolf(NewPlayer());

        DayboundNightbound.EntryDesignation(c, DayNightDesignation.Night)
            .Should().Be(DayNightDesignation.Night);
    }
}
