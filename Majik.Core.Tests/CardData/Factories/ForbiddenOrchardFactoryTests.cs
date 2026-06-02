using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ForbiddenOrchardFactory"/> — Forbidden Orchard
/// (Guildpact). Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color.
///    Whenever you tap this land for mana, target opponent creates a 1/1
///    colorless Spirit creature token."
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller, non-Basic,
///   non-Legendary).
/// - Five mana abilities (one per WUBRG colour) — same any-colour fan-out
///   shape as Mana Confluence, but with NO {C} mode and NO pay-life /
///   pain cost ({T} alone).
/// - A single tap-for-mana <see cref="TriggeredAbility"/> (the analogue of
///   Manabarbs) gated on THIS land specifically (CR 603.2 / CR 605), with a
///   "target opponent" <see cref="Majik.Core.Players.Agents.TargetRequest"/>.
/// - On resolve the targeted opponent — not the controller — creates one
///   1/1 colourless Spirit creature token (CR 111 / 111.4).
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class ForbiddenOrchardFactoryTests
{
    private const string CardName = "Forbidden Orchard";

    public static IEnumerable<object[]> AllColors => new[]
    {
        new object[] { "W" },
        new object[] { "U" },
        new object[] { "B" },
        new object[] { "R" },
        new object[] { "G" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ForbiddenOrchard_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
    }

    [Fact]
    public void ForbiddenOrchard_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void ForbiddenOrchard_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }
    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ForbiddenOrchard_HasFiveManaAbilities_OnePerColor()
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one any-colour mana ability per WUBRG; no {C} mode");
    }

    [Fact]
    public void ForbiddenOrchard_HasNoColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);

        // No "{T}: Add {C}" mode: every mode produces one coloured mana.
        land.Abilities.OfType<ManaAbility>()
            .Should().NotContain(m =>
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0 &&
                m.ManaGenerated.Generic == 1);
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ForbiddenOrchard_ProducesEachColor(string color)
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Should().NotBeNull($"Forbidden Orchard can add {{{color}}}");
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ForbiddenOrchard_TapForMana_DoesNotCostLife(string color)
    {
        // Unlike Mana Confluence / City of Brass, the {T} ability has NO
        // additional life/pain cost — the downside is the Spirit token, not
        // life loss.
        var alice = new Player("Alice", 20);
        var land = ForbiddenOrchardFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Activate();

        alice.LifeTotal.Should().Be(20, "tapping for mana costs no life");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void ForbiddenOrchard_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = ForbiddenOrchardFactory.Create(alice);
        var white = FindColoredAbility(land, "W");
        var blue = FindColoredAbility(land, "U");

        white.Activate();

        blue.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Tap-for-mana trigger — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ForbiddenOrchard_HasOneTriggeredAbility()
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "whenever you tap this land for mana → one triggered ability");
    }

    [Fact]
    public void ForbiddenOrchard_TriggerRequestsOneTargetOpponent()
    {
        var alice = new Player("Alice", 20);

        var land = ForbiddenOrchardFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void ForbiddenOrchard_Trigger_FiresOnTapForManaOfThisLand()
    {
        var alice = new Player("Alice", 20);
        var land = ForbiddenOrchardFactory.Create(alice);
        land.SetZone(ZoneType.Battlefield); // a land can only tap for mana on the battlefield
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        var white = FindColoredAbility(land, "W");

        // A tap-for-mana of THIS land (CR 605) → the trigger fires.
        var e = new ManaAbilityActivatedEvent(white, alice, ManaCost.Parse("W"));

        trigger.IsTriggered(e).Should().BeTrue(
            "you tapped THIS land for mana");
    }

    [Fact]
    public void ForbiddenOrchard_Trigger_DoesNotFireOnOtherSourcesManaTap()
    {
        var alice = new Player("Alice", 20);
        var land = ForbiddenOrchardFactory.Create(alice);
        land.SetZone(ZoneType.Battlefield);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        // A different mana source tapping for mana must NOT fire the trigger
        // ("you tap THIS land", not "a land" — distinct from Manabarbs).
        var other = ForbiddenOrchardFactory.Create(alice);
        var otherWhite = FindColoredAbility(other, "W");
        var e = new ManaAbilityActivatedEvent(otherWhite, alice, ManaCost.Parse("W"));

        trigger.IsTriggered(e).Should().BeFalse(
            "only tapping THIS land fires the trigger");
    }

    // -----------------------------------------------------------------------
    // Trigger resolution — the OPPONENT creates the token
    // -----------------------------------------------------------------------

    [Fact]
    public void ForbiddenOrchard_OnResolve_TargetOpponentCreatesSpiritToken()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = ForbiddenOrchardFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        // Target opponent = Bob.
        trigger.SetChosenTargets(
            new IReadOnlyList<object>[] { new object[] { bob } });

        foreach (var effect in trigger.Effects) effect.Execute();

        var bobTokens = bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Spirit")
            .ToList();

        bobTokens.Should().HaveCount(1, "the targeted opponent creates the token");
        var token = bobTokens[0];
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.Controller.Should().BeSameAs(bob);
        token.HasSubtype(CardSubtype.Spirit).Should().BeTrue();

        // Controller (Alice) gets nothing.
        alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).Should().BeEmpty();
    }

    [Fact]
    public void ForbiddenOrchard_TokenIsColorless()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = ForbiddenOrchardFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(
            new IReadOnlyList<object>[] { new object[] { bob } });

        foreach (var effect in trigger.Effects) effect.Execute();

        var token = bob.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.IsToken && c.Name == "Spirit");

        // CR 111.4 — printed "1/1 colorless Spirit creature token".
        CardColors.GetColors(token).Should().BeEmpty("the Spirit token is colourless");
    }

    [Fact]
    public void ForbiddenOrchard_OnResolve_NoTargetChosen_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = ForbiddenOrchardFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        // No SetChosenTargets — ChosenTargets is empty.

        foreach (var effect in trigger.Effects) effect.Execute();

        bob.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManaAbility FindColoredAbility(Land land, string color)
    {
        var match = ManaCost.Parse(color);
        return land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green &&
            m.ManaGenerated.Generic == match.Generic);
    }
}
