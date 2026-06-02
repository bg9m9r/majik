using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KozileksReturnFactory"/>.
///
/// Card: Kozilek's Return — Instant {2}{R} (Oath of the Gatewatch).
///   "Devoid (This card has no color.)
///    Kozilek's Return deals 2 damage to each creature.
///    Whenever you cast an Eldrazi creature spell with mana value 7 or
///    greater, you may exile this card from your graveyard. If you do, this
///    card deals 5 damage to each creature."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller) + JSON shape.
///   - NamedCardFactory dispatch.
///   - Devoid (CR 702.114) — IsDevoid flag + keyword marker + colourless.
///   - Printed sweep deals 2 damage to every creature on both battlefields.
///   - Graveyard recursion trigger:
///       * active in the graveyard (CR 603.6d),
///       * condition matches an Eldrazi creature spell MV >= 7 cast by the
///         controller, and rejects non-Eldrazi / low-MV / opponent casts,
///       * resolution exiles the card from the graveyard and deals 5 to
///         each creature,
///       * declining the "may" leaves the card + creatures untouched.
/// </summary>
[Trait("Color", "C")]
public class KozileksReturnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KozileksReturn_Identity()
    {
        var c = KozileksReturnFactory.Create(_alice);

        c.Name.Should().Be("Kozilek's Return");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Devoid (CR 702.114)
    // -----------------------------------------------------------------------

    [Fact]
    public void KozileksReturn_IsDevoid_AndColourless_DespiteRedPip()
    {
        var card = KozileksReturnFactory.Create(_alice);

        card.IsDevoid.Should().BeTrue("CR 702.114 — Devoid stamps the colourless flag");
        card.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Devoid")
            .Should().ContainSingle("the Devoid keyword marker is attached for ability scans");

        CardColors.GetColors(card).Should().BeEmpty(
            "CR 702.114 — a Devoid card is colourless even though its mana cost has a {R} pip");
    }

    // -----------------------------------------------------------------------
    // Printed sweep — 2 damage to each creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsTwoDamage_ToEveryCreature_AcrossBothPlayers()
    {
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobWall = NewCreatureOnBattlefield(_bob, "Wall of Doubt", "{2}{U}", 0, 5);

        var effects = KozileksReturnFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        aliceBear.Damage.Should().Be(2);
        bobWall.Damage.Should().Be(2, "opponent creatures are also hit");
        aliceBear.IsDead().Should().BeTrue("2 damage on a 2/2 is lethal");
        bobWall.IsDead().Should().BeFalse("2 damage on a 0/5 survives");
    }

    // -----------------------------------------------------------------------
    // Graveyard recursion trigger — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void RecursionTrigger_IsActiveInGraveyard()
    {
        var card = KozileksReturnFactory.Create(_alice);
        var trigger = GetRecursionTrigger(card);

        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "CR 603.6d — the recursion ability is active while Kozilek's Return is in the graveyard");
    }

    // -----------------------------------------------------------------------
    // Graveyard recursion trigger — condition gating
    // -----------------------------------------------------------------------

    [Fact]
    public void RecursionTrigger_Matches_EldraziCreatureSpell_ManaValue7Plus_OwnCast()
    {
        var card = KozileksReturnFactory.Create(_alice);
        var trigger = GetRecursionTrigger(card);

        var ulamog = NewEldraziCreature(_alice, "Ulamog, the Infinite Gyre", "{11}", 10, 10);
        var ev = new SpellCastEvent(new StubSpell(ulamog, _alice));

        trigger.Condition.Matches(ev, trigger).Should().BeTrue(
            "an Eldrazi creature spell with mana value >= 7 cast by the controller qualifies");
    }

    [Fact]
    public void RecursionTrigger_DoesNotMatch_LowManaValueEldrazi()
    {
        var card = KozileksReturnFactory.Create(_alice);
        var trigger = GetRecursionTrigger(card);

        // Eldrazi creature, but mana value 4 (< 7).
        var smallEldrazi = NewEldraziCreature(_alice, "Eldrazi Skyspawner", "{3}{C}", 2, 1);
        var ev = new SpellCastEvent(new StubSpell(smallEldrazi, _alice));

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "mana value 4 is below the 'mana value 7 or greater' threshold");
    }

    [Fact]
    public void RecursionTrigger_DoesNotMatch_NonEldraziBigCreature()
    {
        var card = KozileksReturnFactory.Create(_alice);
        var trigger = GetRecursionTrigger(card);

        // Mana value 7+ creature, but not an Eldrazi.
        var dragon = new Creature("Big Dragon", "{5}{R}{R}", 7, 7,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(_alice);
        dragon.SetController(_alice);
        var ev = new SpellCastEvent(new StubSpell(dragon, _alice));

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "a non-Eldrazi creature spell does not satisfy 'an Eldrazi creature spell'");
    }

    [Fact]
    public void RecursionTrigger_DoesNotMatch_OpponentCast()
    {
        var card = KozileksReturnFactory.Create(_alice);
        var trigger = GetRecursionTrigger(card);

        // Bob casts the qualifying Eldrazi — "you cast" is Alice-scoped.
        var ulamog = NewEldraziCreature(_bob, "Ulamog, the Infinite Gyre", "{11}", 10, 10);
        var ev = new SpellCastEvent(new StubSpell(ulamog, _bob));

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "'Whenever you cast' is scoped to Kozilek's Return's controller");
    }

    // -----------------------------------------------------------------------
    // Graveyard recursion trigger — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Recursion_OnYes_ExilesFromGraveyard_AndDeals5ToEachCreature()
    {
        var card = KozileksReturnFactory.Create(_alice);
        PutInGraveyard(_alice, card);

        var aliceCreature = NewCreatureOnBattlefield(_alice, "Tarmogoyf", "{1}{G}", 4, 5);
        var bobCreature = NewCreatureOnBattlefield(_bob, "Hill Giant", "{3}{R}", 3, 3);

        // No agent registered → the "may" auto-accepts (board-wipe upside).
        var effects = KozileksReturnFactory.BuildGraveyardRecursionEffect(
            card, _alice, new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // CR 701.21 — the card moved Graveyard → Exile.
        card.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);
        _alice.Zones.Exile.GetCards().Should().Contain(card);

        // CR 608.2 — 5 damage to each creature.
        aliceCreature.Damage.Should().Be(5);
        bobCreature.Damage.Should().Be(5, "opponent creatures are also hit");
    }

    [Fact]
    public void Recursion_WhenCardNotInGraveyard_NoOps()
    {
        var card = KozileksReturnFactory.Create(_alice);
        // Card never placed in graveyard — still in its default zone.

        var creature = NewCreatureOnBattlefield(_alice, "Tarmogoyf", "{1}{G}", 4, 5);

        var effects = KozileksReturnFactory.BuildGraveyardRecursionEffect(
            card, _alice, new[] { _alice });
        foreach (var e in effects) e.Execute();

        creature.Damage.Should().Be(0,
            "CR 608.2b — the optional exile is impossible if the card is not in the graveyard, so no sweep");
        card.Zone.Should().NotBe(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility GetRecursionTrigger(Instant card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature NewEldraziCreature(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Eldrazi });
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void PutInGraveyard(Player owner, Instant card)
    {
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    private sealed class StubSpell : ISpell
    {
        public StubSpell(ICard card, Player controller)
        {
            Card = card;
            Controller = controller;
        }

        public ICard Card { get; }
        public Player Controller { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool IsResolving => false;
        public IReadOnlyList<ITarget> Targets { get; } = Array.Empty<ITarget>();
        public IReadOnlyList<ICost> Costs { get; } = Array.Empty<ICost>();
        public bool CannotBeCountered => false;
        public void Resolve() { }
    }
}
