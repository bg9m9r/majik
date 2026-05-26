using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RecommissionFactory"/> — Sorcery {1}{W}:
///   "Return target artifact or creature card with mana value 3 or
///    less from your graveyard to the battlefield. If a creature
///    enters this way, it enters with an additional +1/+1 counter on
///    it."
///
/// Covers:
/// - Card identity (name, type, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect: returns a MV ≤ 3 creature from caster's graveyard
///   to caster's battlefield and adds one +1/+1 counter.
/// - Resolve effect: returns a MV ≤ 3 artifact (non-creature) and does
///   NOT add a +1/+1 counter.
/// - Resolve effect: filters out MV > 3 cards.
/// - Resolve effect: filters out non-artifact non-creature cards
///   (e.g. an instant in the graveyard).
/// - Resolve effect: no legal target is a clean no-op (graveyard is
///   left untouched, no exception).
/// </summary>
public class RecommissionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    [Fact]
    public void Recommission_Identity()
    {
        var c = RecommissionFactory.Create(_alice);

        c.Name.Should().Be("Recommission");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{1}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Recommission_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Recommission", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Recommission");
        c.ManaCost.Should().Be("{1}{W}");
    }

    // ------------------------------------------------------------------
    // Reanimate creature path
    // ------------------------------------------------------------------

    [Fact]
    public void Recommission_Resolve_ReturnsCreature_WithPlusOnePlusOneCounter()
    {
        // Savannah Lions — MV 1 creature, fits under the MV ≤ 3 cap.
        var lion = new Creature("Savannah Lions", "{W}", 2, 1);
        lion.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(lion);
        lion.SetZone(ZoneType.Graveyard);

        foreach (var effect in RecommissionFactory.BuildResolveEffect(_alice))
        {
            effect.Execute();
        }

        lion.Zone.Should().Be(ZoneType.Battlefield,
            "the targeted creature was reanimated");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(lion);
        _alice.Zones.Battlefield.GetCards().Should().Contain(lion);
        lion.Controller.Should().BeSameAs(_alice);

        lion.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the printed +1/+1 counter rider applies because the returned card is a Creature");
    }

    // ------------------------------------------------------------------
    // Reanimate non-creature artifact path
    // ------------------------------------------------------------------

    [Fact]
    public void Recommission_Resolve_ReturnsArtifact_NoPlusOnePlusOneCounter()
    {
        // Sol Ring — MV 1 non-creature Artifact, fits under the MV ≤ 3
        // cap.
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(solRing);
        solRing.SetZone(ZoneType.Graveyard);

        foreach (var effect in RecommissionFactory.BuildResolveEffect(_alice))
        {
            effect.Execute();
        }

        solRing.Zone.Should().Be(ZoneType.Battlefield,
            "the targeted artifact was reanimated");
        _alice.Zones.Battlefield.GetCards().Should().Contain(solRing);
        solRing.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-creature artifacts skip the +1/+1 counter rider");
    }

    // ------------------------------------------------------------------
    // MV cap
    // ------------------------------------------------------------------

    [Fact]
    public void Recommission_Resolve_SkipsHighManaValueCards()
    {
        // Force a Wrath of God (MV 4) — too expensive for Recommission.
        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(wrath);
        wrath.SetZone(ZoneType.Graveyard);

        // And a Hill Giant (MV 4) — also too expensive.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        foreach (var effect in RecommissionFactory.BuildResolveEffect(_alice))
        {
            effect.Execute();
        }

        wrath.Zone.Should().Be(ZoneType.Graveyard,
            "Wrath of God is MV 4 — over the MV ≤ 3 cap");
        giant.Zone.Should().Be(ZoneType.Graveyard,
            "Hill Giant is MV 4 — over the MV ≤ 3 cap");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Type filter
    // ------------------------------------------------------------------

    [Fact]
    public void Recommission_Resolve_SkipsNonArtifactNonCreatureCards()
    {
        // Lightning Bolt — MV 1 instant, ineligible (not artifact, not
        // creature).
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        foreach (var effect in RecommissionFactory.BuildResolveEffect(_alice))
        {
            effect.Execute();
        }

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "Lightning Bolt is an Instant — not artifact, not creature");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Recommission_Resolve_NoLegalTarget_CleanNoOp()
    {
        // Empty graveyard — nothing to reanimate; the spell still
        // resolves with a clean no-op (CR 608.2b).
        var act = () =>
        {
            foreach (var effect in RecommissionFactory.BuildResolveEffect(_alice))
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Preference among multiple eligible cards
    // ------------------------------------------------------------------

    [Fact]
    public void Recommission_Resolve_PicksFirstEligibleCreature_OverIneligibleHigherMV()
    {
        // Mix of legal + illegal targets. Deterministic v1 picker
        // returns the first eligible match in the graveyard's
        // enumeration order.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3); // MV 4 — illegal
        giant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var lion = new Creature("Savannah Lions", "{W}", 2, 1); // MV 1 — legal
        lion.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(lion);
        lion.SetZone(ZoneType.Graveyard);

        foreach (var effect in RecommissionFactory.BuildResolveEffect(_alice))
        {
            effect.Execute();
        }

        lion.Zone.Should().Be(ZoneType.Battlefield,
            "the lion is the first eligible card (MV ≤ 3 creature)");
        giant.Zone.Should().Be(ZoneType.Graveyard,
            "the giant is over the MV cap and stays in the graveyard");
        lion.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }
}
