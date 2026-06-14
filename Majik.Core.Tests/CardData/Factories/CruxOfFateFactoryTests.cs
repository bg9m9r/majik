using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CruxOfFateFactory"/> — Crux of Fate (Fate Reforged,
/// {3}{B}{B}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Choose one —
///    • Destroy all Dragon creatures.
///    • Destroy all non-Dragon creatures."
///
/// Covers the card's UNIQUE behaviour — the Dragon-partitioned modal board
/// sweep — plus a single identity assert for the non-vanilla mana cost:
/// <list type="bullet">
///   <item>Identity: Sorcery, {3}{B}{B} (built from the embedded JSON).</item>
///   <item>Mode 0 (Dragons): destroys every Dragon creature, leaves
///     non-Dragons; symmetric across both battlefields (CR 109.5).</item>
///   <item>Mode 1 (non-Dragons): destroys every non-Dragon creature, leaves
///     Dragons.</item>
///   <item>Non-creature permanents survive either mode.</item>
///   <item>Empty battlefields are a clean no-op.</item>
/// </list>
/// Dispatch + well-formedness is asserted for every implemented card by
/// <c>CardFactoryContractTests</c>, so no dispatch test is duplicated here.
/// </summary>
[Trait("Color", "B")]
public class CruxOfFateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CruxOfFate_Identity_SorceryAt3BB()
    {
        var card = CruxOfFateFactory.Create(_alice);

        card.Name.Should().Be("Crux of Fate");
        card.ManaCost.Should().Be("{3}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy all Dragon creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveChoice_Dragons_DestroysDragons_LeavesNonDragons_BothBattlefields()
    {
        var aliceDragon = SeedDragon(_alice, "Alice-Dragon");
        var bobDragon = SeedDragon(_bob, "Bob-Dragon");
        var aliceBear = SeedCreature(_alice, "Alice-Bear");
        var bobGoblin = SeedCreature(_bob, "Bob-Goblin");

        CruxOfFateFactory.ResolveChoice(new[] { _alice, _bob }, CruxOfFateFactory.DestroyDragons);

        // Dragons destroyed, regardless of controller (symmetric sweep).
        aliceDragon.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceDragon);
        bobDragon.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobDragon);

        // Non-Dragons survive.
        aliceBear.Zone.Should().Be(ZoneType.Battlefield);
        bobGoblin.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy all non-Dragon creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveChoice_NonDragons_DestroysNonDragons_LeavesDragons_BothBattlefields()
    {
        var aliceDragon = SeedDragon(_alice, "Alice-Dragon");
        var bobDragon = SeedDragon(_bob, "Bob-Dragon");
        var aliceBear = SeedCreature(_alice, "Alice-Bear");
        var bobGoblin = SeedCreature(_bob, "Bob-Goblin");

        CruxOfFateFactory.ResolveChoice(new[] { _alice, _bob }, CruxOfFateFactory.DestroyNonDragons);

        // Non-Dragons destroyed.
        aliceBear.Zone.Should().Be(ZoneType.Graveyard);
        bobGoblin.Zone.Should().Be(ZoneType.Graveyard);

        // Dragons survive.
        aliceDragon.Zone.Should().Be(ZoneType.Battlefield);
        bobDragon.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void ResolveChoice_LeavesNonCreaturePermanentsAlone()
    {
        var dragon = SeedDragon(_alice, "Alice-Dragon");
        var bear = SeedCreature(_alice, "Alice-Bear");

        var land = new Land("Swamp");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bone Saw", "");
        artifact.SetOwner(_alice); artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact); artifact.SetZone(ZoneType.Battlefield);

        // Destroying non-Dragons hits the bear but never the land/artifact.
        CruxOfFateFactory.ResolveChoice(new[] { _alice, _bob }, CruxOfFateFactory.DestroyNonDragons);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        dragon.Zone.Should().Be(ZoneType.Battlefield);
        land.Zone.Should().Be(ZoneType.Battlefield);
        artifact.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void ResolveChoice_EmptyBattlefields_IsCleanNoOp()
    {
        var actDragons = () =>
            CruxOfFateFactory.ResolveChoice(new[] { _alice, _bob }, CruxOfFateFactory.DestroyDragons);
        var actNonDragons = () =>
            CruxOfFateFactory.ResolveChoice(new[] { _alice, _bob }, CruxOfFateFactory.DestroyNonDragons);

        actDragons.Should().NotThrow();
        actNonDragons.Should().NotThrow();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedDragon(Player owner, string name)
    {
        var c = new Creature(name, "{4}{R}", power: 4, toughness: 4,
            subtypes: new[] { CardSubtype.Dragon });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{B}", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
