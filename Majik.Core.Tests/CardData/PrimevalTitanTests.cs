using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PrimevalTitanFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 6/6, Giant subtype, mana cost,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Trample keyword marker (CR 702.19).
/// - ETB triggered ability fetches up to two lands → battlefield tapped
///   (CR 603.1, CR 701.19a):
///   - 5 lands in library, selector picks 2 → both moved, library -2.
///   - Selector picks 1 → 1 land moved.
///   - Selector picks 0 → no-op.
/// - Attack triggered ability fires on CreatureAttacksEvent and runs the
///   same tutor effect (CR 508.1f).
/// </summary>
public class PrimevalTitanTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    private static Land MakeNonbasicLand(string name, Player owner)
    {
        var land = new Land(name, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_Identity()
    {
        var c = PrimevalTitanFactory.Create(_alice);

        c.Name.Should().Be("Primeval Titan");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue("Primeval Titan is a Giant (CR 205.3m)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{4}{G}{G}");
    }

    [Fact]
    public void PrimevalTitan_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Primeval Titan", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Primeval Titan");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.ManaCost.Should().Be("{4}{G}{G}");
    }

    // -----------------------------------------------------------------------
    // Trample keyword (CR 702.19)
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_HasTrampleKeyword()
    {
        var c = PrimevalTitanFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Trample",
            "CR 702.19 — Trample is a printed evergreen on Primeval Titan");

        CombatAbilities.HasTrample(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger — tutor up to two lands → battlefield tapped
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_ETB_SelectorPicksTwoLands_BothEnterTapped_LibraryShrinksByTwo()
    {
        var alice = new Player("Alice", 20);

        // Library has 5 lands (mix of basics + nonbasic — Tron-style).
        var forest1 = MakeBasicLand("Forest", alice, CardSubtype.Forest);
        var forest2 = MakeBasicLand("Forest", alice, CardSubtype.Forest);
        var mountain = MakeBasicLand("Mountain", alice, CardSubtype.Mountain);
        var tower = MakeNonbasicLand("Urza's Tower", alice);
        var mine = MakeNonbasicLand("Urza's Mine", alice);
        foreach (var land in new[] { forest1, forest2, mountain, tower, mine })
        {
            alice.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);
        }

        // Selector returns the two Tron lands deterministically.
        var titan = PrimevalTitanFactory.Create(
            alice, triggers: null,
            selector: _ => new ICard[] { tower, mine });

        // Place on battlefield so the trigger's source-zone guard is
        // satisfied, then fire the ETB effect directly.
        alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<Majik.Core.Events.CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        var bf = alice.Zones.Battlefield.GetCards().OfType<Land>().ToList();
        bf.Should().Contain(tower);
        bf.Should().Contain(mine);

        // Tron lands entered tapped (CR 701.19a + printed "tapped" clause).
        tower.IsTapped.Should().BeTrue();
        mine.IsTapped.Should().BeTrue();

        alice.Zones.Library.GetCards().Should().HaveCount(3,
            "two of the five library lands moved to the battlefield");
        alice.Zones.Library.GetCards().Should().NotContain(tower);
        alice.Zones.Library.GetCards().Should().NotContain(mine);
    }

    [Fact]
    public void PrimevalTitan_ETB_SelectorPicksOneLand_OneEntersTapped()
    {
        var alice = new Player("Alice", 20);

        var forest = MakeBasicLand("Forest", alice, CardSubtype.Forest);
        var mountain = MakeBasicLand("Mountain", alice, CardSubtype.Mountain);
        foreach (var land in new[] { forest, mountain })
        {
            alice.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);
        }

        // "Up to two" — selector returns a single land (decline second slot).
        var titan = PrimevalTitanFactory.Create(
            alice, triggers: null,
            selector: _ => new ICard[] { forest });

        alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<Majik.Core.Events.CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        var bfLands = alice.Zones.Battlefield.GetCards().OfType<Land>().ToList();
        bfLands.Should().ContainSingle().Which.Should().BeSameAs(forest);
        forest.IsTapped.Should().BeTrue();

        alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(mountain, "the un-fetched Mountain stays in the library");
    }

    [Fact]
    public void PrimevalTitan_ETB_SelectorPicksZero_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var forest = MakeBasicLand("Forest", alice, CardSubtype.Forest);
        alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // CR 701.19a — declining to find is legal even when candidates exist.
        var titan = PrimevalTitanFactory.Create(
            alice, triggers: null,
            selector: _ => Array.Empty<ICard>());

        alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<Majik.Core.Events.CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards().OfType<Land>().Should().BeEmpty(
            "selector declined both slots — no land enters the battlefield");
        alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(forest);
    }

    [Fact]
    public void PrimevalTitan_ETB_SelectorIgnoresNonLandPicks()
    {
        // Defensive: if a test selector returns a non-land (e.g. agent
        // confusion), the factory filters it out — only lands move.
        var alice = new Player("Alice", 20);
        var forest = MakeBasicLand("Forest", alice, CardSubtype.Forest);
        var bears = new Creature("Grizzly Bears", "1G", 2, 2);
        bears.SetOwner(alice);
        bears.SetController(alice);
        alice.Zones.Library.AddCard(forest);
        alice.Zones.Library.AddCard(bears);
        forest.SetZone(ZoneType.Library);
        bears.SetZone(ZoneType.Library);

        var titan = PrimevalTitanFactory.Create(
            alice, triggers: null,
            selector: _ => new ICard[] { bears, forest });

        alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<Majik.Core.Events.CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        // Forest moved; Grizzly Bears was rejected and remains in library.
        alice.Zones.Battlefield.GetCards().OfType<Land>().Should().ContainSingle()
            .Which.Should().BeSameAs(forest);
        forest.IsTapped.Should().BeTrue();
        alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bears);
    }

    // -----------------------------------------------------------------------
    // Attack trigger — same tutor effect (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_AttackTrigger_FiresOnCreatureAttacksEvent_AndTutorsTwoLands()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var forest = MakeBasicLand("Forest", alice, CardSubtype.Forest);
        var mountain = MakeBasicLand("Mountain", alice, CardSubtype.Mountain);
        foreach (var land in new[] { forest, mountain })
        {
            alice.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);
        }

        var titan = PrimevalTitanFactory.Create(
            alice, triggers: null,
            selector: _ => new ICard[] { forest, mountain });

        alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        // Locate the attack trigger by its CreatureAttacksEvent condition.
        var attackTrigger = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        // CR 508.1f — fires when this creature is declared as an attacker.
        var attackEvent = new CreatureAttacksEvent(titan, bob);
        attackTrigger.IsTriggered(attackEvent).Should().BeTrue(
            "the attack trigger matches CreatureAttacksEvent where the source is the attacker");

        // A different attacker should NOT trigger this ability.
        var otherAttacker = new Creature("Llanowar Elves", "G", 1, 1);
        otherAttacker.SetOwner(alice);
        otherAttacker.SetController(alice);
        otherAttacker.SetZone(ZoneType.Battlefield);
        var otherEvent = new CreatureAttacksEvent(otherAttacker, bob);
        attackTrigger.IsTriggered(otherEvent).Should().BeFalse(
            "the per-attacker trigger only fires for Primeval Titan itself");

        // Resolve the attack-trigger effect — same tutor body as the ETB.
        foreach (var effect in attackTrigger.Effects) effect.Execute();

        var bfLands = alice.Zones.Battlefield.GetCards().OfType<Land>().ToList();
        bfLands.Should().HaveCount(2);
        bfLands.Should().Contain(forest);
        bfLands.Should().Contain(mountain);
        forest.IsTapped.Should().BeTrue();
        mountain.IsTapped.Should().BeTrue();
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
