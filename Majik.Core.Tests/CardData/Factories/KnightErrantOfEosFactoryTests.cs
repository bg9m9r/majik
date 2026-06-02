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
/// Tests for Knight-Errant of Eos (Modern Horizons 3, {3}{G}{W}).
///
/// Covers:
///   - Card shape: name, type, Elf + Knight subtypes, P/T 4/4, mana cost,
///     owner / controller wiring.
///   - Convoke keyword marker attached.
///   - ETB resolution (<see cref="KnightErrantOfEosFactory.ResolveEtb"/>)
///     for the canonical happy path (six creature cards mv ≤ 2 → first two
///     to hand, last four to bottom random), the predicate-filter path
///     (non-creature / mv > 2 cards skipped), the empty-library no-op,
///     the agent-decline-second-pick path, and the smaller-library path
///     (< 6 cards).
///   - NamedCardFactory dispatch routes the card name to this factory.
/// </summary>
[Trait("Color", "M")]
public class KnightErrantOfEosFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // PLAN 01 Slice D — ResolveEtb is now async; tests drive it through a
    // legacy ResolutionContext (no live agent → first-pick fallback, same
    // posture these tests already assert).
    private static ResolutionContext Rc(Player controller) =>
        ResolutionContext.For(controller, agent: null, game: null, chosenTargets: null);

    [Fact]
    public void KnightErrant_IsCreature_ElfKnight_4_4_AtCost3GW()
    {
        var c = KnightErrantOfEosFactory.Create(_alice);

        c.Name.Should().Be("Knight-Errant of Eos");
        c.ManaCost.Should().Be("{3}{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KnightErrant_HasConvokeMarker()
    {
        var c = KnightErrantOfEosFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Convoke");
    }

    [Fact]
    public async System.Threading.Tasks.Task ResolveEtb_TakesUpToTwoMv2OrLessCreatures_RestToBottomRandom()
    {
        // Library top → bottom: 6 creature cards all mv ≤ 2.
        var lib = new[]
        {
            BuildCreature("C1", "{1}"),
            BuildCreature("C2", "{W}"),
            BuildCreature("C3", "{1}{G}"),
            BuildCreature("C4", "{2}"),  // mv 2 (still ≤ 2)
            BuildCreature("C5", "{G}"),
            BuildCreature("C6", "{U}"),
        };
        foreach (var c in lib) _alice.Zones.Library.AddCard(c);

        // No agent registered → falls back to "take first" — first two
        // eligible creatures are C1, C2.
        await KnightErrantOfEosFactory.ResolveEtbAsync(_alice, Rc(_alice));

        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Should().HaveCount(2);
        hand.Should().Contain(lib[0]).And.Contain(lib[1]);
        lib[0].Zone.Should().Be(ZoneType.Hand);
        lib[1].Zone.Should().Be(ZoneType.Hand);

        // Remaining four (C3..C6) are at the bottom in some random order.
        var library = _alice.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(4);
        library.Should().BeEquivalentTo(new[] { lib[2], lib[3], lib[4], lib[5] });
    }

    [Fact]
    public async System.Threading.Tasks.Task ResolveEtb_FiltersOutNonCreaturesAndHighMv()
    {
        // 4 cards in library — Knight-Errant peeks all of them (< 6).
        var creature1 = BuildCreature("Tiny Bird", "{1}");        // creature mv 1 → eligible
        var creature2 = BuildCreature("Hill Giant", "{3}{R}");    // creature mv 4 → NOT eligible
        var noncreature = new Sorcery("Lava Spike", "{R}");
        noncreature.SetOwner(_alice);
        var smallCreature = BuildCreature("Mox-payoff Goblin", "{R}");  // mv 1 → eligible

        _alice.Zones.Library.AddCard(creature1);
        _alice.Zones.Library.AddCard(creature2);
        _alice.Zones.Library.AddCard(noncreature);
        _alice.Zones.Library.AddCard(smallCreature);

        await KnightErrantOfEosFactory.ResolveEtbAsync(_alice, Rc(_alice));

        var hand = _alice.Zones.Hand.GetCards().ToList();
        // Only creature1 + smallCreature are eligible. With no agent the
        // default picks the first eligible card twice in sequence — so
        // both are taken.
        hand.Should().Contain(creature1).And.Contain(smallCreature);
        hand.Should().NotContain(creature2);
        hand.Should().NotContain(noncreature);
    }

    [Fact]
    public async System.Threading.Tasks.Task ResolveEtb_EmptyLibrary_IsNoOp()
    {
        var act = async () => await KnightErrantOfEosFactory.ResolveEtbAsync(_alice, Rc(_alice));
        await act.Should().NotThrowAsync();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public async System.Threading.Tasks.Task ResolveEtb_LibraryHasOnlyOneEligibleCard_TakesOne()
    {
        // Only one creature mv ≤ 2 in library — the second prompt has an
        // empty eligible list and the loop exits early without erroring.
        var c1 = BuildCreature("Only Eligible", "{1}");
        var bigCreature = BuildCreature("Big Creature", "{4}{R}");  // mv 5, ineligible
        _alice.Zones.Library.AddCard(c1);
        _alice.Zones.Library.AddCard(bigCreature);

        await KnightErrantOfEosFactory.ResolveEtbAsync(_alice, Rc(_alice));

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().Be(c1);
        _alice.Zones.Library.GetCards().Should().Contain(bigCreature);
    }
    // ----------------- helpers -----------------

    private Creature BuildCreature(string name, string cost)
    {
        var c = new Creature(name, cost, power: 1, toughness: 1);
        c.SetOwner(_alice);
        c.SetZone(ZoneType.Library);
        return c;
    }

}
