using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MalevolentRumbleFactory"/>.
///
/// Card: Malevolent Rumble — Sorcery {1}{G} (Modern Horizons 3).
///   "Reveal the top four cards of your library. You may put a permanent
///    card from among them into your hand. Put the rest into your
///    graveyard. Create a 0/1 colorless Eldrazi Spawn creature token with
///    \"Sacrifice this token: Add {C}.\""
///
/// The data-driven cast path (OracleSpellBinder → MalevolentRumblePattern)
/// already has its own test suite in <see cref="MalevolentRumbleTests"/>.
/// This file covers the parallel factory-shaped construction path:
///   - Identity ({1}{G}, Sorcery, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - <see cref="MalevolentRumbleFactory.BuildResolveEffect"/> resolution:
///       * Self-mills 4 (peeked cards leave the library).
///       * First permanent in the top-4 goes to caster's hand.
///       * Non-permanents go to graveyard.
///       * All-instants: no card to hand, all 4 in graveyard.
///       * Empty library: no throw, Spawn token still enters.
///       * Eldrazi Spawn token created on the battlefield (0/1, both
///         subtypes, ManaAbility producing {C}).
/// </summary>
public class MalevolentRumbleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MalevolentRumble_Identity()
    {
        var c = MalevolentRumbleFactory.Create(_alice);

        c.Name.Should().Be("Malevolent Rumble");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MalevolentRumble()
    {
        var card = NamedCardFactory.Create("Malevolent Rumble", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Malevolent Rumble");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: reveal-4 / permanent → hand / rest → graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_MillsTopFour_FromLibrary()
    {
        // Seed 6 cards on the library (only top 4 are peeked).
        var c1 = SeedLibraryCard(new Instant("I1", ""));
        var c2 = SeedLibraryCard(new Instant("I2", ""));
        var c3 = SeedLibraryCard(new Instant("I3", ""));
        var c4 = SeedLibraryCard(new Instant("I4", ""));
        var c5 = SeedLibraryCard(new Instant("I5", ""));
        var c6 = SeedLibraryCard(new Instant("I6", ""));

        Resolve();

        // Top 4 left the library; bottom 2 remain.
        _alice.Zones.Library.GetCards().Should().NotContain(new[] { c1, c2, c3, c4 });
        _alice.Zones.Library.GetCards().Should().Contain(new[] { c5, c6 });
    }

    [Fact]
    public void Resolve_FirstPermanent_GoesToHand_NonPermanents_GoToGraveyard()
    {
        var instant1 = SeedLibraryCard(new Instant("Counterspell", "UU"));
        var instant2 = SeedLibraryCard(new Instant("Shock", "R"));
        var bear = SeedLibraryCard(new Creature("Bear", "1G", 2, 2));
        var sorcery = SeedLibraryCard(new Sorcery("Doom Blade", "1B"));

        Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bear, "first permanent in the top 4 goes to hand");
        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new Card[] { instant1, instant2, sorcery },
                "non-permanent cards go to graveyard");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_AllNonPermanents_NothingGoesToHand_AllGoToGraveyard()
    {
        var cards = new[] { "A", "B", "C", "D" }
            .Select(n => SeedLibraryCard(new Instant(n, "")))
            .ToList();

        Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(cards);
    }

    [Fact]
    public void Resolve_EmptyLibrary_DoesNotThrow_AndStillCreatesSpawn()
    {
        var act = () => Resolve();

        act.Should().NotThrow("empty library is a clean no-op for the mill half");
        _alice.Zones.Hand.Should().NotBeNull();
        // Token still created — unconditional in oracle text.
        FindSpawn(_alice).Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Eldrazi Spawn token
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CreatesOneEldraziSpawnToken_OnBattlefield()
    {
        SeedLibraryCard(new Creature("Bear", "1G", 2, 2));

        Resolve();

        var spawn = FindSpawn(_alice);
        spawn.Should().NotBeNull("Malevolent Rumble always creates an Eldrazi Spawn token");
        spawn!.Power.Should().Be(0);
        spawn.Toughness.Should().Be(1);
        spawn.IsToken.Should().BeTrue();
        spawn.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        spawn.HasSubtype(CardSubtype.Spawn).Should().BeTrue();
        spawn.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_SpawnToken_HasColorlessManaAbility()
    {
        Resolve();

        var spawn = FindSpawn(_alice);
        spawn.Should().NotBeNull();
        spawn!.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Generic == 1,
                "Eldrazi Spawn's 'Sacrifice: Add {C}' is wired as a ManaAbility producing 1 colourless");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve()
    {
        foreach (var e in MalevolentRumbleFactory.BuildResolveEffect(_alice))
            e.Execute();
    }

    private T SeedLibraryCard<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    private static Creature? FindSpawn(Player p) =>
        p.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.Name == "Eldrazi Spawn");
}
