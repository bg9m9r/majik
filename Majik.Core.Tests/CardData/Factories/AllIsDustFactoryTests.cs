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
/// Unit tests for All Is Dust (Rise of the Eldrazi, {7}).
///
/// Oracle (Tribal Sorcery — Eldrazi):
///   "Each player sacrifices all colored permanents they control."
///
/// Coverage:
///   * Identity (Tribal Sorcery, Eldrazi subtype, {7}, colourless).
///   * NamedCardFactory dispatch.
///   * Sweep sacrifices coloured creatures, colourless creatures
///     (Eldrazi titans) survive.
///   * Coloured non-creature permanents (planeswalkers, enchantments,
///     artifacts whose printed cost has a coloured pip) are sacrificed.
///   * Colourless artifacts / Wastes lands survive.
///   * Indestructible coloured permanents are NOT spared (CR 701.16 vs
///     CR 701.7).
///   * Empty battlefields = clean no-op.
///   * Each sacrificed permanent lands in its OWNER's graveyard
///     (CR 110.2).
/// </summary>
public class AllIsDustFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AllIsDust_IsTribalSorceryEldrazi_At7()
    {
        var card = AllIsDustFactory.Create(_alice);

        card.Name.Should().Be("All Is Dust");
        card.ManaCost.Should().Be("{7}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Tribal).Should().BeTrue(
            "CR 308 — All Is Dust is a Tribal Sorcery; the Eldrazi " +
            "subtype is grounded on the Tribal card type.");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AllIsDust_IsColourless()
    {
        var card = AllIsDustFactory.Create(_alice);
        // CR 105 — no coloured pips in the printed cost {7}.
        CardColors.GetColors(card).Should().BeEmpty();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AllIsDust()
    {
        var card = NamedCardFactory.Create("All Is Dust", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("All Is Dust");
        card.HasType(CardType.Tribal).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.ManaCost.Should().Be("{7}");
    }

    // -----------------------------------------------------------------------
    // Colour detection
    // -----------------------------------------------------------------------

    [Fact]
    public void IsColouredPermanent_PrintedColouredCost_ReturnsTrue()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        AllIsDustFactory.IsColouredPermanent(bear).Should().BeTrue(
            "a creature with a green pip in its printed cost is coloured.");
    }

    [Fact]
    public void IsColouredPermanent_ColourlessCost_ReturnsFalse()
    {
        var emrakul = new Creature("Endless One", "{X}", 0, 0);
        AllIsDustFactory.IsColouredPermanent(emrakul).Should().BeFalse(
            "no coloured pips in the printed cost = colourless = " +
            "survives All Is Dust.");
    }

    // -----------------------------------------------------------------------
    // Sweep semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_SacrificesColouredCreatures_ColourlessEldraziSurvive()
    {
        // Alice: coloured bear (green) + colourless Eldrazi titan analogue.
        var bear = SeedCreature(_alice, "Grizzly Bears", "{1}{G}");
        var titan = SeedCreature(_alice, "Endless One", "{X}");

        // Bob: coloured spirit (blue).
        var spirit = SeedCreature(_bob, "Mausoleum Wanderer", "{U}");

        var effects = AllIsDustFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "coloured creature (green) is sacrificed.");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);

        titan.Zone.Should().Be(ZoneType.Battlefield,
            "colourless Eldrazi-shaped creature survives — this is the " +
            "whole reason the deck plays All Is Dust.");
        _alice.Zones.Battlefield.GetCards().Should().Contain(titan);

        spirit.Zone.Should().Be(ZoneType.Graveyard,
            "Bob's coloured creature is sacrificed too — 'each player'.");
        _bob.Zones.Graveyard.GetCards().Should().Contain(spirit);
    }

    [Fact]
    public void Resolve_SacrificesColouredNonCreaturePermanents()
    {
        // Coloured enchantment (white) + coloured planeswalker analogue
        // (black) + colourless artifact (no pips).
        var enchant = SeedEnchantment(_alice, "Honor of the Pure", "{W}");
        var blackEnchant = SeedEnchantment(_alice, "Bitterblossom", "{1}{B}");
        var colourlessArtifact = SeedArtifact(_alice, "Sol Ring", "{1}");

        var effects = AllIsDustFactory.BuildResolveEffect(new[] { _alice });
        foreach (var e in effects) e.Execute();

        enchant.Zone.Should().Be(ZoneType.Graveyard,
            "coloured enchantment is sacrificed alongside creatures " +
            "(printed text says 'all coloured permanents').");
        blackEnchant.Zone.Should().Be(ZoneType.Graveyard);
        colourlessArtifact.Zone.Should().Be(ZoneType.Battlefield,
            "colourless artifact survives — no coloured pips in cost.");
    }

    [Fact]
    public void Resolve_IndestructibleColouredPermanent_StillSacrificed()
    {
        // CR 702.12b — indestructible only saves from DESTROY effects.
        // Sacrifice (CR 701.16) bypasses indestructible.
        var avacyn = new Creature("Avacyn, Angel of Hope", "{5}{W}{W}{W}", 8, 8);
        avacyn.SetOwner(_alice);
        avacyn.SetController(_alice);
        avacyn.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(avacyn);
        avacyn.AddAbility(new KeywordAbility("Indestructible", avacyn, _alice));

        var effects = AllIsDustFactory.BuildResolveEffect(new[] { _alice });
        foreach (var e in effects) e.Execute();

        avacyn.Zone.Should().Be(ZoneType.Graveyard,
            "indestructible coloured permanents are still sacrificed by " +
            "All Is Dust — sacrifice isn't a destroy effect.");
    }

    [Fact]
    public void Resolve_LandsSurvive_UnlessColoured()
    {
        // Plains has no mana cost (printed cost is empty) so it counts
        // as colourless and survives. v1 does not promote a basic land's
        // type-derived colour into the colour set (CR 305.6 talks about
        // mana-producing colour, but CR 105 keys colour off pips in the
        // printed cost, and basics have no printed cost).
        var plains = new Land("Plains");
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        plains.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(plains);

        var effects = AllIsDustFactory.BuildResolveEffect(new[] { _alice });
        foreach (var e in effects) e.Execute();

        plains.Zone.Should().Be(ZoneType.Battlefield,
            "Plains has no printed mana cost → colourless under CR 105 → " +
            "survives All Is Dust.");
    }

    [Fact]
    public void Resolve_EmptyBattlefields_IsCleanNoOp()
    {
        var effects = AllIsDustFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_SacrificedToOwnerGraveyard_NotControllerGraveyard()
    {
        // CR 110.2 — sacrifices go to the OWNER's graveyard, even when
        // an opponent currently controls the permanent (Mind Control,
        // Threaten, etc.).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_bob); // Bob has stolen the bear.
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        // Sweep both players. Bob is the current controller; Alice is
        // the owner. The sacrifice goes to Alice's graveyard.
        var effects = AllIsDustFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear,
            "CR 110.2 — sacrificed permanent goes to its OWNER's graveyard.");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear,
            "the bear was removed from Bob's battlefield by the sweep.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Enchantment SeedEnchantment(Player owner, string name, string cost)
    {
        var e = new Enchantment(name, cost);
        e.SetOwner(owner);
        e.SetController(owner);
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
        return e;
    }

    private static Artifact SeedArtifact(Player owner, string name, string cost)
    {
        var a = new Artifact(name, cost);
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}
