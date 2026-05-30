using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Wistfulness (Modern Horizons 3, {3}{G/U}{G/U}, Creature —
/// Elemental Incarnation 6/5).
///
/// Covers:
/// - Identity (Elemental + Incarnation, {3}{G/U}{G/U}, 6/5).
/// - NamedCardFactory dispatch + Evoke marker.
/// - GG-conditional ETB: exile an opponent's artifact/enchantment ONLY when
///   {G}{G} spent.
/// - UU-conditional ETB: draw 2 then discard 1 ONLY when {U}{U} spent.
/// - {G}{U} cast: NEITHER conditional fires (the deferral #15 distinction).
/// </summary>
public class WistfulnessTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static IReadOnlyDictionary<ManaColor, int> Counts(
        params (ManaColor Color, int N)[] pairs)
    {
        var d = new Dictionary<ManaColor, int>();
        foreach (var (c, n) in pairs) d[c] = n;
        return d;
    }

    private (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers,
        ZoneService zones) Harness()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        return (bus, stack, triggers, zones);
    }

    private Creature MakeWistfulnessInLibrary(TriggerManager triggers)
    {
        var w = WistfulnessFactory.Create(_alice);
        w.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(w);
        triggers.BindCard(w);
        return w;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Wistfulness_Identity()
    {
        var w = WistfulnessFactory.Create(_alice);

        w.Name.Should().Be("Wistfulness");
        w.ManaCost.Should().Be("{3}{G/U}{G/U}");
        w.HasType(CardType.Creature).Should().BeTrue();
        w.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        w.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();
        w.BasePower.Should().Be(6);
        w.BaseToughness.Should().Be(5);
    }

    [Fact]
    public void Wistfulness_DispatchesViaNamedCardFactory_WithEvokeMarker()
    {
        var c = NamedCardFactory.Create("Wistfulness", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Wistfulness");
        c.Abilities.OfType<KeywordAbility>().Should().Contain(k => k.Keyword == "Evoke");
    }

    // -----------------------------------------------------------------------
    // GG ETB — exile an opponent's artifact/enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_GGSpent_ExilesOpponentArtifact()
    {
        var (bus, stack, triggers, zones) = Harness();
        var w = MakeWistfulnessInLibrary(triggers);

        var bobArtifact = new Artifact("Bob's Bauble", "{1}");
        bobArtifact.SetOwner(_bob);
        bobArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);
        bobArtifact.SetZone(ZoneType.Battlefield);

        w.SetPendingCastColorCounts(Counts((ManaColor.Green, 2)));
        zones.MoveCardTo(w, ZoneType.Battlefield);

        var gg = w.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        gg.SetChosenTargets(new[] { new[] { (object)bobArtifact } });

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bobArtifact.Zone.Should().Be(ZoneType.Exile, "{G}{G} spent → exile opponent's artifact");
    }

    [Fact]
    public void Etb_UUSpent_GGExileDoesNotFire()
    {
        var (bus, stack, triggers, zones) = Harness();
        var w = MakeWistfulnessInLibrary(triggers);

        var bobArtifact = new Artifact("Bob's Bauble", "{1}");
        bobArtifact.SetOwner(_bob);
        bobArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);
        bobArtifact.SetZone(ZoneType.Battlefield);

        // {U}{U} only — GG intervening-if false.
        w.SetPendingCastColorCounts(Counts((ManaColor.Blue, 2)));
        zones.MoveCardTo(w, ZoneType.Battlefield);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bobArtifact.Zone.Should().Be(ZoneType.Battlefield, "no {G}{G} → no exile");
    }

    // -----------------------------------------------------------------------
    // UU ETB — draw two then discard one
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_UUSpent_DrawsTwoThenDiscardsOne()
    {
        var (bus, stack, triggers, zones) = Harness();
        var w = MakeWistfulnessInLibrary(triggers);

        // Stock the library with three draw targets.
        for (var i = 0; i < 3; i++)
        {
            var c = new Creature($"Filler{i}", "{1}", 1, 1);
            c.SetOwner(_alice);
            c.SetController(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
        }

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        var gyBefore = _alice.Zones.Graveyard.GetCards().Count();

        w.SetPendingCastColorCounts(Counts((ManaColor.Blue, 2)));
        zones.MoveCardTo(w, ZoneType.Battlefield);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        // Drew 2, discarded 1 → net +1 hand, +1 graveyard.
        (_alice.Zones.Hand.GetCards().Count() - handBefore).Should().Be(1,
            "draw two then discard one → net +1 card in hand");
        (_alice.Zones.Graveyard.GetCards().Count() - gyBefore).Should().Be(1,
            "the discarded card lands in the graveyard");
    }

    [Fact]
    public void Etb_GGSpent_UULootDoesNotFire()
    {
        var (bus, stack, triggers, zones) = Harness();
        var w = MakeWistfulnessInLibrary(triggers);

        for (var i = 0; i < 3; i++)
        {
            var c = new Creature($"Filler{i}", "{1}", 1, 1);
            c.SetOwner(_alice);
            c.SetController(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
        }

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        // {G}{G} only — UU intervening-if false. (No GG target → exile no-ops.)
        w.SetPendingCastColorCounts(Counts((ManaColor.Green, 2)));
        zones.MoveCardTo(w, ZoneType.Battlefield);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "no {U}{U} → no loot");
    }

    // -----------------------------------------------------------------------
    // GU cast — neither conditional fires (deferral #15 distinction)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_GUSpent_NeitherConditionalFires()
    {
        var (bus, stack, triggers, zones) = Harness();
        var w = MakeWistfulnessInLibrary(triggers);

        var bobArtifact = new Artifact("Bob's Bauble", "{1}");
        bobArtifact.SetOwner(_bob);
        bobArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);
        bobArtifact.SetZone(ZoneType.Battlefield);

        for (var i = 0; i < 3; i++)
        {
            var c = new Creature($"Filler{i}", "{1}", 1, 1);
            c.SetOwner(_alice);
            c.SetController(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
        }

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        // {G}{U} (one each) — neither GG nor UU.
        w.SetPendingCastColorCounts(Counts((ManaColor.Green, 1), (ManaColor.Blue, 1)));
        zones.MoveCardTo(w, ZoneType.Battlefield);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bobArtifact.Zone.Should().Be(ZoneType.Battlefield, "{G}{U} is not {G}{G} → no exile");
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "{G}{U} is not {U}{U} → no loot");
    }
}
