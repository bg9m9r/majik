using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Roadside Reliquary (March of the Machine: The Aftermath).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice this land: Draw a card if you control an artifact.
///    Draw a card if you control an enchantment."
///
/// Covers:
///   - Identity (Land, "Roadside Reliquary", owner/controller).
///   - NamedCardFactory dispatch.
///   - {T}: Add {C} mana ability is present (one colourless).
///   - Exactly one non-mana activated ability (the sac-draw).
///   - On resolve: the land is sacrificed (battlefield -> graveyard).
///   - Conditional draws (CR 121.1) — the two "if you control" clauses are
///     independent:
///       * control an artifact only  -> draw exactly 1.
///       * control an enchantment only -> draw exactly 1.
///       * control both             -> draw exactly 2.
///       * control neither          -> draw 0.
/// </summary>
[Trait("Color", "C")]
public class RoadsideReliquaryFactoryTests
{
    private static Player NewPlayerWithLibrary(int librarySize)
    {
        var p = new Player("Alice", 20);
        for (var i = 0; i < librarySize; i++)
        {
            var c = new Creature($"Filler_{i}", "1", 1, 1) { Owner = p };
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
        return p;
    }

    private static Land OnBattlefield(Player owner)
    {
        var land = RoadsideReliquaryFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    private static void GiveArtifact(Player p)
    {
        var a = new Artifact("Some Artifact", "1") { Owner = p };
        a.SetController(p);
        p.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
    }

    private static void GiveEnchantment(Player p)
    {
        var e = new Enchantment("Some Enchantment", "1") { Owner = p };
        e.SetController(p);
        p.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
    }

    // ------------------------------------------------------------------ Identity

    [Fact]
    public void RoadsideReliquary_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = RoadsideReliquaryFactory.Create(alice);

        land.Name.Should().Be("Roadside Reliquary");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }
    // -------------------------------------------------------------- Mana ability

    [Fact]
    public void RoadsideReliquary_HasColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);
        var land = RoadsideReliquaryFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle("land produces exactly {C}")
            .Which.ManaGenerated.Generic.Should().Be(1);
    }

    [Fact]
    public void RoadsideReliquary_HasExactlyOneActivatedAbility()
    {
        var alice = new Player("Alice", 20);
        var land = RoadsideReliquaryFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the sac-draw ability is the only non-mana activated ability");
    }

    // ------------------------------------------------------------ Sac + draws

    [Fact]
    public void RoadsideReliquary_SacAbility_Resolve_SacrificesLand()
    {
        var alice = NewPlayerWithLibrary(10);
        var land = OnBattlefield(alice);

        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        land.Zone.Should().Be(ZoneType.Graveyard,
            "the activated ability sacrifices the land on resolve");
    }

    [Fact]
    public void RoadsideReliquary_ControlArtifactOnly_DrawsOne()
    {
        var alice = NewPlayerWithLibrary(10);
        var land = OnBattlefield(alice);
        GiveArtifact(alice);

        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "control an artifact (not an enchantment) -> draw exactly 1");
    }

    [Fact]
    public void RoadsideReliquary_ControlEnchantmentOnly_DrawsOne()
    {
        var alice = NewPlayerWithLibrary(10);
        var land = OnBattlefield(alice);
        GiveEnchantment(alice);

        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "control an enchantment (not an artifact) -> draw exactly 1");
    }

    [Fact]
    public void RoadsideReliquary_ControlBoth_DrawsTwo()
    {
        var alice = NewPlayerWithLibrary(10);
        var land = OnBattlefield(alice);
        GiveArtifact(alice);
        GiveEnchantment(alice);

        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "control both an artifact and an enchantment -> draw 2");
    }

    [Fact]
    public void RoadsideReliquary_ControlNeither_DrawsZero()
    {
        var alice = NewPlayerWithLibrary(10);
        var land = OnBattlefield(alice);

        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "control neither an artifact nor an enchantment -> draw 0");
    }

    [Fact]
    public void RoadsideReliquary_LandItself_DoesNotCountAsArtifactOrEnchantment()
    {
        // Sanity: the Reliquary is a Land (not an artifact/enchantment), and
        // it is sacrificed as a cost before the draws are evaluated anyway.
        var alice = NewPlayerWithLibrary(10);
        var land = OnBattlefield(alice);

        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "a lone Roadside Reliquary is neither an artifact nor an enchantment");
    }
}
