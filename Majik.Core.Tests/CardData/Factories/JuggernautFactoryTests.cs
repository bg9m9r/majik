using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="JuggernautFactory"/>.
///
/// Juggernaut (Antiquities, {4}) — Artifact Creature — Juggernaut 5/3.
/// Oracle text (verified against Scryfall 2026-06-23):
///   "This creature attacks each combat if able.
///    This creature can't be blocked by Walls."
///
/// Covers identity (non-vanilla statline / Artifact + Creature / Juggernaut
/// subtype) plus the two unique combat behaviours: the must-attack marker
/// (CR 508.1a / 702.43) and the "can't be blocked by Walls" restriction
/// (CR 509.1b).
/// </summary>
[Trait("Color", "C")]
public class JuggernautFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Juggernaut_Identity()
    {
        var c = JuggernautFactory.Create(_alice);

        c.Name.Should().Be("Juggernaut");
        c.ManaCost.Should().Be("{4}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Juggernaut).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Must-attack marker (CR 508.1a / 702.43) ──────────────────────────

    [Fact]
    public void Juggernaut_HasAttacksEachCombatMarker()
    {
        var c = JuggernautFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "AttacksEachCombat",
                "CR 508.1a — \"attacks each combat if able\" is enforced by CombatFlow via this marker.");
    }

    // ── Can't be blocked by Walls (CR 509.1b) ────────────────────────────

    [Fact]
    public void Juggernaut_CantBeBlockedByWalls()
    {
        var svc = new ContinuousEffectsService();
        var jugg = JuggernautFactory.Create(_alice, svc);
        jugg.SetZone(ZoneType.Battlefield);

        var wall = Blocker("Wall of Stone", CardSubtype.Wall);
        var nonWall = Blocker("Grizzly Bears", subtype: null);

        BlockLegality.CanBlock(wall, jugg, out _).Should().BeFalse(
            "a creature with the Wall subtype can't block Juggernaut.");
        BlockLegality.CanBlock(nonWall, jugg, out _).Should().BeTrue(
            "a non-Wall creature is a legal blocker.");
    }

    [Fact]
    public void Juggernaut_ShapeOnlyPath_NoRestrictionRegistered()
    {
        // No effects service → the block restriction isn't wired; a Wall blocks.
        var jugg = JuggernautFactory.Create(_alice);
        jugg.SetZone(ZoneType.Battlefield);

        var wall = Blocker("Wall of Stone", CardSubtype.Wall);
        BlockLegality.CanBlock(wall, jugg, out _).Should().BeTrue(
            "shape-only path registers no block restriction.");
    }

    private Creature Blocker(string name, CardSubtype? subtype)
    {
        var subtypes = subtype is { } s ? new[] { s } : System.Array.Empty<CardSubtype>();
        var c = new Creature(name, "{1}", 0, 4, subtypes: subtypes);
        c.SetOwner(_bob);
        c.SetController(_bob);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
