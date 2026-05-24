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
/// Tests for Amped Raptor (Modern Horizons 3, {1}{R}, Creature — Dinosaur 3/1).
///
/// Covers:
/// - Identity (name, type, cost, P/T, subtype).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Trample keyword wired.
/// - ETB exiles up to four cards of the controller's library.
/// - Eligible spell in pile (MV ≤ 2 instant / sorcery) → may be cast.
/// - No eligible spell → clean no-op, exiled cards remain in exile.
/// - Multiple eligible spells → picker selects one; only the picked card
///   is reported as <see cref="AmpedRaptorFactory.Result.Picked"/>.
/// - Exiled cards stay in exile after the ETB resolves (no return-to-library).
/// </summary>
public class AmpedRaptorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeCostPT()
    {
        var card = AmpedRaptorFactory.Create(_alice);

        card.Name.Should().Be("Amped Raptor");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
        card.ManaCostValue.TotalValue.Should().Be(2);

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.Power.Should().Be(3);
        creature.Toughness.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AmpedRaptor()
    {
        var card = NamedCardFactory.Create("Amped Raptor", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Amped Raptor");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Card_HasTrampleKeyword()
    {
        var card = AmpedRaptorFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample");
    }

    [Fact]
    public void Card_HasOneEtbTriggeredAbility()
    {
        var card = AmpedRaptorFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Amped Raptor prints one triggered ability — its ETB exile-4 + may-cast clause.");
    }

    [Fact]
    public void Etb_ExilesTopFourCardsOfLibrary()
    {
        // Library: 5 cards. ETB should exile exactly the top 4 (order =
        // first-out = top), leaving 1 in the library.
        var cards = new ICard[]
        {
            new Sorcery("Top 1", "{R}"),
            new Sorcery("Top 2", "{R}"),
            new Sorcery("Top 3", "{R}"),
            new Sorcery("Top 4", "{R}"),
            new Sorcery("Bottom", "{R}"),
        };
        foreach (var c in cards)
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = AmpedRaptorFactory.ResolveEtb(_alice);

        result.Exiled.Should().HaveCount(4);
        result.Exiled.Select(c => c.Name).Should()
            .ContainInOrder("Top 1", "Top 2", "Top 3", "Top 4");

        _alice.Zones.Library.Count.Should().Be(1);
        _alice.Zones.Library.GetCards().Single().Name.Should().Be("Bottom");
        _alice.Zones.Exile.Count.Should().Be(4);
    }

    [Fact]
    public void Etb_EligibleSpellInPile_PickedAndLeftInExileForFreeCast()
    {
        // Library: Mountain (land — ineligible), Lightning Bolt (Instant MV 1 — eligible),
        // Counterspell (Instant MV 2 — eligible), Wrath of God (Sorcery MV 4 — ineligible).
        var land = NamedCardFactory.Create("Mountain", _alice);
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_alice);
        var counter = new Instant("Counterspell", "{U}{U}"); counter.SetOwner(_alice);
        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}"); wrath.SetOwner(_alice);

        foreach (var c in new ICard[] { land, bolt, counter, wrath })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AmpedRaptorFactory.Result? captured = null;
        var card = AmpedRaptorFactory.Create(
            _alice,
            triggers: null,
            chooseSpell: pile => pile.First(),
            onEtbResolved: r => captured = r);

        // Fire the ETB effect directly — TriggerManager would do this on
        // CardMovedEvent → Battlefield via the live event bus.
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Exiled.Should().HaveCount(4);
        captured.Eligible.Should().HaveCount(2);
        captured.Eligible.Select(c => c.Name).Should().BeEquivalentTo(new[] { "Lightning Bolt", "Counterspell" });
        captured.Picked.Should().BeSameAs(bolt);

        // Picked card sits in exile, ready for the caller to drive
        // SpellCastFlow with CastFromExileAlternativeCost(ManaCost.Zero).
        bolt.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Etb_NoEligibleSpell_CleanNoOp_CardsRemainInExile()
    {
        // Top 4: all lands / creatures / too-expensive. No eligible spell.
        var m1 = NamedCardFactory.Create("Mountain", _alice);
        var m2 = NamedCardFactory.Create("Forest", _alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2); bear.SetOwner(_alice);
        var heavy = new Sorcery("Big Spell", "{2}{R}"); heavy.SetOwner(_alice);

        foreach (var c in new ICard[] { m1, m2, bear, heavy })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AmpedRaptorFactory.Result? captured = null;
        var card = AmpedRaptorFactory.Create(
            _alice, triggers: null,
            chooseSpell: pile => pile.First(),
            onEtbResolved: r => captured = r);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Exiled.Should().HaveCount(4);
        captured.Eligible.Should().BeEmpty();
        captured.Picked.Should().BeNull();

        // All four still in exile — printed oracle does NOT return them
        // to the library (distinct from Cascade).
        _alice.Zones.Library.Count.Should().Be(0);
        _alice.Zones.Exile.Count.Should().Be(4);
        foreach (var c in captured.Exiled) c.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Etb_MultipleEligibleSpells_PickerSelectsOne_OthersRemainEligibleButUncast()
    {
        // Three eligible spells in the top 4 — Bolt, Shock, Counterspell (all MV ≤ 2),
        // plus a Mountain. Picker forces Counterspell. Only Counterspell is
        // reported as Picked; Bolt + Shock are eligible but uncast and stay
        // in exile.
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_alice);
        var shock = new Instant("Shock", "{R}"); shock.SetOwner(_alice);
        var counter = new Instant("Counterspell", "{U}{U}"); counter.SetOwner(_alice);
        var land = NamedCardFactory.Create("Mountain", _alice);

        foreach (var c in new ICard[] { bolt, shock, counter, land })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AmpedRaptorFactory.Result? captured = null;
        var card = AmpedRaptorFactory.Create(
            _alice, triggers: null,
            // Force-pick Counterspell.
            chooseSpell: pile => pile.First(c => c.Name == "Counterspell"),
            onEtbResolved: r => captured = r);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().HaveCount(3);
        captured.Picked.Should().BeSameAs(counter);

        // All four sit in exile (the picked one will be moved by the
        // host's SpellCastFlow → Stack at production cast time).
        _alice.Zones.Exile.Count.Should().Be(4);
        bolt.Zone.Should().Be(ZoneType.Exile);
        shock.Zone.Should().Be(ZoneType.Exile);
        counter.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Etb_CardsStayInExile_NotReturnedToLibrary()
    {
        // Printed oracle: cards not cast remain in exile. Confirm the
        // top-4 set is untouched in exile after the ETB resolves with a
        // declined "may" (chooser returns null).
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_alice);
        var shock = new Instant("Shock", "{R}"); shock.SetOwner(_alice);
        var m1 = NamedCardFactory.Create("Mountain", _alice);
        var m2 = NamedCardFactory.Create("Forest", _alice);
        // Extra card so we can prove only the top 4 were exiled.
        var bottom = new Sorcery("Bottom", "{R}"); bottom.SetOwner(_alice);

        foreach (var c in new ICard[] { bolt, shock, m1, m2, bottom })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        AmpedRaptorFactory.Result? captured = null;
        var card = AmpedRaptorFactory.Create(
            _alice, triggers: null,
            // Decline the "may".
            chooseSpell: _ => null,
            onEtbResolved: r => captured = r);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Picked.Should().BeNull();

        // Exactly the top 4 in exile, the 5th still in the library.
        _alice.Zones.Exile.Count.Should().Be(4);
        _alice.Zones.Library.Count.Should().Be(1);
        _alice.Zones.Library.GetCards().Single().Name.Should().Be("Bottom");

        foreach (var c in new ICard[] { bolt, shock, m1, m2 })
        {
            c.Zone.Should().Be(ZoneType.Exile);
        }
    }

    [Fact]
    public void Etb_ShortLibrary_ExilesWhatRemains_NoThrow()
    {
        // Library has 2 cards; ETB exiles both, candidate pool computed
        // off whatever made it into exile.
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_alice);
        var m1 = NamedCardFactory.Create("Mountain", _alice);
        foreach (var c in new ICard[] { bolt, m1 })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = AmpedRaptorFactory.ResolveEtb(_alice);

        result.Exiled.Should().HaveCount(2);
        result.Eligible.Should().ContainSingle().Which.Should().BeSameAs(bolt);
        result.Picked.Should().BeSameAs(bolt);
        _alice.Zones.Library.Count.Should().Be(0);
        _alice.Zones.Exile.Count.Should().Be(2);
    }

    [Fact]
    public void BuildAlternativeCost_IsZeroCost_FromExile()
    {
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_alice);
        var alt = AmpedRaptorFactory.BuildAlternativeCost(bolt);

        alt.Should().NotBeNull();
        alt.AlternativeManaCost.TotalValue.Should().Be(0);
        alt.Description.Should().Contain("Lightning Bolt");
    }
}
