using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KozilekButcherOfTruthFactory"/>.
///
/// Kozilek, Butcher of Truth (Rise of the Eldrazi, {10}). Legendary
/// Creature — Eldrazi 12/12. Oracle text (verified against Scryfall):
///   "When you cast this spell, draw four cards.
///    Annihilator 4 (Whenever this creature attacks, defending player
///    sacrifices four permanents of their choice.)
///    When Kozilek is put into a graveyard from anywhere, its owner
///    shuffles their graveyard into their library."
///
/// Coverage:
/// - Identity (name, types, supertype, subtype, cost, mv, colourless, P/T,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Annihilator 4 marker + Annihilator trigger (CR 702.86).
/// - Structural cast trigger (CR 603.6a) over SpellCastEvent, gated to this
///   card; resolution draws four.
/// - Put-into-graveyard-from-anywhere trigger (CR 603.6c): owner shuffles
///   their graveyard into their library; fires from a non-battlefield zone
///   too ("from anywhere").
/// </summary>
public class KozilekButcherOfTruthFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card MakeCard(Player owner, string name, string cost)
    {
        var c = new Instant(name, cost);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void FillLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Instant($"Lib {i}", "{1}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
        }
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void Kozilek_Identity()
    {
        var c = KozilekButcherOfTruthFactory.Create(_alice);

        c.Name.Should().Be("Kozilek, Butcher of Truth");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.ManaCost.Should().Be("{10}");
        c.ManaCostValue.TotalValue.Should().Be(10);
        c.BasePower.Should().Be(12);
        c.BaseToughness.Should().Be(12);
        CardColors.GetColors(c).Should().BeEmpty("Kozilek is colourless (no coloured mana symbols, CR 105.2c).");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kozilek_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kozilek, Butcher of Truth", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kozilek, Butcher of Truth");
        ((Creature)c).HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
    }

    // ── Annihilator 4 ───────────────────────────────────────────────────

    [Fact]
    public void Kozilek_HasAnnihilator4Marker()
    {
        var c = KozilekButcherOfTruthFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Annihilator" && k.Arg == 4,
            "CR 702.86 — printed Annihilator 4");
    }

    [Fact]
    public void Kozilek_AnnihilatorTrigger_SacrificesFourOnAttack()
    {
        var c = KozilekButcherOfTruthFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        // Bob has 5 bears; deterministic fallback sacrifices the first four.
        var seeded = new List<Creature>();
        for (var i = 0; i < 5; i++)
        {
            var b = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            b.SetOwner(_bob);
            b.SetController(_bob);
            b.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(b);
            seeded.Add(b);
        }

        var annihilator = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
        annihilator.Condition.Matches(new CreatureAttacksEvent(c, _bob), annihilator)
            .Should().BeTrue();
        foreach (var e in annihilator.Effects) e.Execute();

        seeded.Take(4).Should().OnlyContain(b => b.Zone == ZoneType.Graveyard,
            "Annihilator 4 — defending player sacrifices four permanents.");
        seeded[4].Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Cast trigger — draw four ────────────────────────────────────────

    [Fact]
    public void CastTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var card = KozilekButcherOfTruthFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

        var selfSpell = new Spell(card, _alice);
        var other = MakeCard(_alice, "Other", "{R}");
        var otherSpell = new Spell(other, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    [Fact]
    public void CastTrigger_DrawsFour()
    {
        FillLibrary(_alice, 20);

        var card = KozilekButcherOfTruthFactory.Create(_alice);
        card.SetZone(ZoneType.Stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

        var spell = new Spell(card, _alice);
        trigger.Condition.Matches(new SpellCastEvent(spell), trigger).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(4,
            "the on-cast trigger draws four cards (CR 603.6a).");
    }

    [Fact]
    public void CastTrigger_EmptyLibrary_StampsLossCondition()
    {
        var card = KozilekButcherOfTruthFactory.Create(_alice);
        card.SetZone(ZoneType.Stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

        foreach (var e in trigger.Effects) e.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library stamps the CR 704.5b / 120.3 loss condition.");
    }

    // ── Put-into-graveyard-from-anywhere trigger ────────────────────────

    [Fact]
    public void Kozilek_HasGraveyardShuffleTrigger_ActiveFromAnywhere()
    {
        var card = KozilekButcherOfTruthFactory.Create(_alice);

        var shuffle = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        // "from anywhere" — must match a graveyard arrival regardless of the
        // origin zone (battlefield, hand, library, stack, exile).
        foreach (var from in new[]
                 {
                     ZoneType.Battlefield, ZoneType.Hand, ZoneType.Library,
                     ZoneType.Stack, ZoneType.Exile,
                 })
        {
            shuffle.Condition
                .Matches(new CardMovedEvent(card, from, ZoneType.Graveyard), shuffle)
                .Should().BeTrue($"the trigger fires when Kozilek is put into a graveyard from {from}.");
        }

        // Does NOT fire when moving to a non-graveyard zone.
        shuffle.Condition
            .Matches(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield), shuffle)
            .Should().BeFalse();
    }

    [Fact]
    public void GraveyardShuffle_MovesGraveyardIntoLibrary()
    {
        // Alice has 3 cards in graveyard, 0 in library before the shuffle.
        var grave = new List<Card>();
        for (var i = 0; i < 3; i++)
        {
            var g = MakeCard(_alice, $"Grave {i}", "{1}");
            g.SetZone(ZoneType.Graveyard);
            _alice.Zones.Graveyard.AddCard(g);
            grave.Add(g);
        }

        var card = KozilekButcherOfTruthFactory.Create(_alice);
        var shuffle = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        foreach (var e in shuffle.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "owner shuffles their graveyard into their library — the graveyard ends empty.");
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(3);
        lib.Should().Contain(grave, "every graveyard card ends in the library.");
        lib.Should().OnlyContain(c => c.Zone == ZoneType.Library);
    }
}
