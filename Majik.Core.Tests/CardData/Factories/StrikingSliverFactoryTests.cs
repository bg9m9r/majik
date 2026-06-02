using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="StrikingSliverFactory"/> (Magic 2014 / many
/// reprints, {R}). Creature — Sliver 1/1. Oracle text (verified against
/// Scryfall):
///   "Sliver creatures you control have first strike."
///
/// Covers:
/// - Identity (Sliver, mana cost {R}, 1/1, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - The Sliver lord static grants first strike to Slivers the controller
///   controls (Striking Sliver itself included — no "Other" qualifier).
/// - A non-Sliver creature you control is NOT granted first strike.
/// - An opponent's Sliver is NOT granted first strike (controller-scoped).
/// </summary>
[Trait("Color", "R")]
public class StrikingSliverFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeSliver(Player owner, string name = "Galerider Sliver")
    {
        var c = new Creature(name, "{U}", 1, 1, subtypes: new[] { CardSubtype.Sliver });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonSliver(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StrikingSliver_Identity()
    {
        var card = StrikingSliverFactory.Create(_alice);

        card.Name.Should().Be("Striking Sliver");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Sliver).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Sliver lord static — "Sliver creatures you control have first strike."
    // -----------------------------------------------------------------------

    [Fact]
    public void StrikingSliver_GrantsFirstStrike_ToOtherControlledSliver()
    {
        var continuous = new ContinuousEffectsService();

        var otherSliver = MakeSliver(_alice, "Galerider Sliver");
        otherSliver.ActiveEffects = continuous;

        var striking = StrikingSliverFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(striking);
        striking.SetZone(ZoneType.Battlefield);

        var chars = continuous.Compute(otherSliver);

        chars.Keywords.Should().Contain("First strike",
            "CR 613.1f — Striking Sliver's static grants first strike to Slivers you control");
    }

    [Fact]
    public void StrikingSliver_GrantsFirstStrike_ToItself()
    {
        var continuous = new ContinuousEffectsService();

        var striking = StrikingSliverFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(striking);
        striking.SetZone(ZoneType.Battlefield);
        striking.ActiveEffects = continuous;

        var chars = continuous.Compute(striking);

        chars.Keywords.Should().Contain("First strike",
            "the printed text has no 'Other' qualifier and Striking Sliver is itself a Sliver, "
            + "so it grants first strike to itself (includeSelf: true).");
    }

    [Fact]
    public void StrikingSliver_DoesNotGrantFirstStrike_ToNonSliver()
    {
        var continuous = new ContinuousEffectsService();

        var bears = MakeNonSliver(_alice);
        bears.ActiveEffects = continuous;

        var striking = StrikingSliverFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(striking);
        striking.SetZone(ZoneType.Battlefield);

        var chars = continuous.Compute(bears);

        chars.Keywords.Should().NotContain("First strike",
            "matching subtype = Sliver only; non-Sliver creatures aren't granted first strike.");
    }

    [Fact]
    public void StrikingSliver_DoesNotGrantFirstStrike_ToOpponentSliver()
    {
        var continuous = new ContinuousEffectsService();

        var bobSliver = MakeSliver(_bob, "Sidewinder Sliver");
        bobSliver.ActiveEffects = continuous;

        var striking = StrikingSliverFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(striking);
        striking.SetZone(ZoneType.Battlefield);

        var chars = continuous.Compute(bobSliver);

        chars.Keywords.Should().NotContain("First strike",
            "controller-scoped lord (allPlayers: false) — Bob's Slivers are unaffected.");
    }
}
