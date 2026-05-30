using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PlaguecrafterFactory"/> — Plaguecrafter
/// (Guilds of Ravnica, {2}{B}, Creature — Human Shaman 3/2).
///
/// Oracle text (Scryfall verified):
///   "When this creature enters, each player sacrifices a creature or
///    planeswalker of their choice. Each player who can't discards a card."
///
/// Covers:
/// - Identity (name, type, P/T 3/2, Human Shaman subtypes, cost).
/// - NamedCardFactory dispatch.
/// - ETB trigger (CR 603.1) iterates EACH player (controller included —
///   CR 109.5 / 800.4 "each player"):
///     * A player with a creature sacrifices one of their choice (CR 701.16)
///       and does NOT discard.
///     * A player with only a planeswalker sacrifices it.
///     * A player with NEITHER a creature nor a planeswalker "can't" — they
///       discard a card instead (CR 701.8).
///     * A player who can't sac AND has an empty hand does nothing (clean).
/// </summary>
public class PlaguecrafterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Plaguecrafter_Identity()
    {
        var c = PlaguecrafterFactory.Create(_alice);

        c.Name.Should().Be("Plaguecrafter");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{2}{B}");
    }

    [Fact]
    public void Plaguecrafter_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Plaguecrafter", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Plaguecrafter");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{B}");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — each player sacrifices a creature/planeswalker of their
    // choice; each who can't discards a card. CR 603.1 / 701.16 / 701.8.
    // -----------------------------------------------------------------------

    private static TriggeredAbility SelectEtbTrigger(Creature pc)
    {
        return pc.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<CardMovedEvent>)
            .Single(t => t.Effects.Any(e => e.Description != null
                && e.Description.Contains("each player sacrifices")));
    }

    [Fact]
    public void Plaguecrafter_Etb_ControllerSacrificesCreature_AndDoesNotDiscard()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // A card in hand that must NOT be discarded (controller CAN sacrifice).
        var spell = new Instant("Shock", "R");
        spell.SetOwner(alice);
        alice.Zones.Hand.AddCard(spell);
        spell.SetZone(ZoneType.Hand);

        var pc = PlaguecrafterFactory.Create(
            alice,
            playerResolver: () => new[] { alice },
            triggers: null,
            agent: null);

        var etb = SelectEtbTrigger(pc);
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards().Should().NotContain(bear,
            "CR 701.16 — the controller sacrifices a creature of their choice");
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        alice.Zones.Hand.GetCards().Should().Contain(spell,
            "a player who CAN sacrifice does not discard");
    }

    [Fact]
    public void Plaguecrafter_Etb_PlayerWithOnlyPlaneswalker_SacrificesPlaneswalker()
    {
        var alice = new Player("Alice", 20);

        var pw = new Planeswalker("Liliana of the Veil", "1BB", 3,
            subtypes: new[] { CardSubtype.Liliana });
        pw.SetOwner(alice);
        pw.SetController(alice);
        alice.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var card = new Instant("Shock", "R");
        card.SetOwner(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var pc = PlaguecrafterFactory.Create(
            alice,
            playerResolver: () => new[] { alice },
            triggers: null,
            agent: null);

        var etb = SelectEtbTrigger(pc);
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards().Should().NotContain(pw,
            "a creature OR planeswalker may be sacrificed (CR 701.16)");
        alice.Zones.Graveyard.GetCards().Should().Contain(pw);
        alice.Zones.Hand.GetCards().Should().Contain(card,
            "the planeswalker satisfied the sacrifice — no discard");
    }

    [Fact]
    public void Plaguecrafter_Etb_PlayerWhoCantSacrifice_DiscardsACard()
    {
        var alice = new Player("Alice", 20);

        // No creature and no planeswalker on the battlefield → "can't".
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var spell = new Instant("Shock", "R");
        spell.SetOwner(alice);
        alice.Zones.Hand.AddCard(spell);
        spell.SetZone(ZoneType.Hand);

        var pc = PlaguecrafterFactory.Create(
            alice,
            playerResolver: () => new[] { alice },
            triggers: null,
            agent: null);

        var etb = SelectEtbTrigger(pc);
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards().Should().Contain(land,
            "a land is neither a creature nor a planeswalker — nothing sacrificed");
        alice.Zones.Hand.GetCards().Should().NotContain(spell,
            "CR 701.8 — a player who can't sacrifice discards a card");
        alice.Zones.Graveyard.GetCards().Should().Contain(spell);
    }

    [Fact]
    public void Plaguecrafter_Etb_PlayerWhoCantSacrifice_WithEmptyHand_DoesNothing()
    {
        var alice = new Player("Alice", 20);
        // No creature, no planeswalker, empty hand → clean no-op for this player.

        var pc = PlaguecrafterFactory.Create(
            alice,
            playerResolver: () => new[] { alice },
            triggers: null,
            agent: null);

        var etb = SelectEtbTrigger(pc);
        Action act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Plaguecrafter_Etb_AffectsEachPlayer_IncludingOpponents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice (controller) has a creature to sacrifice.
        var aliceBear = new Creature("Grizzly Bears", "1G", 2, 2);
        aliceBear.SetOwner(alice);
        aliceBear.SetController(alice);
        alice.Zones.Battlefield.AddCard(aliceBear);
        aliceBear.SetZone(ZoneType.Battlefield);

        // Bob has no creature/planeswalker but a card in hand → he discards.
        var bobSpell = new Instant("Shock", "R");
        bobSpell.SetOwner(bob);
        bob.Zones.Hand.AddCard(bobSpell);
        bobSpell.SetZone(ZoneType.Hand);

        var pc = PlaguecrafterFactory.Create(
            alice,
            playerResolver: () => new[] { alice, bob },
            triggers: null,
            agent: null);

        var etb = SelectEtbTrigger(pc);
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(aliceBear,
            "the controller is a 'player' too and sacrifices");
        bob.Zones.Graveyard.GetCards().Should().Contain(bobSpell,
            "Bob couldn't sacrifice, so he discards a card (CR 701.8)");
        bob.Zones.Hand.GetCards().Should().NotContain(bobSpell);
    }
}
