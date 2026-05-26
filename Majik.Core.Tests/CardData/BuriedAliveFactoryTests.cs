using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BuriedAliveFactory"/>.
///
/// Buried Alive — Sorcery {2}{B} (Odyssey):
///   "Search your library for up to three creature cards, put them into
///    your graveyard, then shuffle."
/// </summary>
public class BuriedAliveFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ChosenSpellParams Choose() =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell)
    {
        foreach (var fx in spell.EffectFactory(Choose()))
        {
            fx.Execute();
        }
    }

    [Fact]
    public void BuriedAlive_Identity()
    {
        var c = BuriedAliveFactory.Create(_alice);

        c.Name.Should().Be("Buried Alive");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{2}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuriedAlive_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Buried Alive", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Buried Alive");
        c.ManaCost.Should().Be("{2}{B}");
    }

    [Fact]
    public void BuriedAlive_Resolve_TutorsUpToThreeCreaturesIntoGraveyard()
    {
        // Library has 4 creatures + 1 instant. No agent → deterministic
        // first-three creature picks.
        var bear = AddLibraryCard(new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        var giant = AddLibraryCard(new Creature("Hill Giant", "{3}{R}", 3, 3));
        var ornithopter = AddLibraryCard(new Creature("Ornithopter", "{0}", 0, 2));
        var skipped = AddLibraryCard(new Creature("Goblin Guide", "{R}", 2, 2));
        var bolt = AddLibraryCard(new Instant("Lightning Bolt", "{R}"));

        Resolve(BuriedAliveFactory.BuildSpellDefinition(_alice));

        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bear, giant, ornithopter },
            "the first three creature cards (in library order) are milled");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(skipped,
            "the fourth creature is not picked — printed cap is three");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bolt,
            "instants are not creature cards");
        bear.Zone.Should().Be(ZoneType.Graveyard);
        giant.Zone.Should().Be(ZoneType.Graveyard);
        ornithopter.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void BuriedAlive_Resolve_LibraryHasFewerThanThreeCreatures_PicksWhatItCan()
    {
        var bear = AddLibraryCard(new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        var bolt = AddLibraryCard(new Instant("Lightning Bolt", "{R}"));

        var act = () => Resolve(BuriedAliveFactory.BuildSpellDefinition(_alice));

        act.Should().NotThrow("fewer-than-three creatures → resolve picks what it can");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        bear.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Library, "instants are never picked");
    }

    [Fact]
    public void BuriedAlive_Resolve_NoCreaturesInLibrary_IsCleanNoOp()
    {
        AddLibraryCard(new Instant("Lightning Bolt", "{R}"));

        var act = () => Resolve(BuriedAliveFactory.BuildSpellDefinition(_alice));

        act.Should().NotThrow();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "no creature cards in library → nothing milled");
    }

    [Fact]
    public void BuriedAlive_Resolve_RoutesThroughZoneService_PublishesCardMovedEvents()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var bear = AddLibraryCard(new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        var giant = AddLibraryCard(new Creature("Hill Giant", "{3}{R}", 3, 3));

        Resolve(BuriedAliveFactory.BuildSpellDefinition(_alice, zones));

        movedEvents
            .Where(e => e.FromZone == ZoneType.Library && e.ToZone == ZoneType.Graveyard)
            .Select(e => e.Card)
            .Should().BeEquivalentTo(new ICard[] { bear, giant },
                "each library → graveyard move publishes CardMovedEvent (CR 603.6a)");
    }


    private TCard AddLibraryCard<TCard>(TCard card) where TCard : Card
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}
