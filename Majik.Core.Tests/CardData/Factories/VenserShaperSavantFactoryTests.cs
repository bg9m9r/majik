using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VenserShaperSavantFactory"/>.
///
/// Card: Venser, Shaper Savant (Future Sight, {2}{U}{U}).
///   Legendary Creature — Human Wizard 2/2. Oracle text:
///     "Flash (You may cast this spell any time you could cast an instant.)
///      When Venser enters, return target spell or permanent to its owner's
///      hand."
///
/// Covers:
/// - Identity (Legendary Creature, Human Wizard, 2/2, {2}{U}{U}, owner /
///   controller, Legendary supertype).
/// - Flash keyword marker attached (CR 702.8).
/// - NamedCardFactory dispatch.
/// - ETB trigger shape — single 1..1 "target spell or permanent" request.
/// - ETB resolve bounces a target permanent to its owner's hand (CR 701.10).
/// - ETB resolve returns a target spell on the stack to its owner's hand —
///   NOT the graveyard, and NOT blocked by CannotBeCountered (bounce is not
///   a counter — CR 701.10 vs CR 701.5).
/// - ETB resolve guards against illegal-on-resolution targets (CR 608.2b).
/// - ETB resolve with no targets short-circuits.
/// </summary>
[Trait("Color", "U")]
public class VenserShaperSavantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Venser_Identity()
    {
        var c = VenserShaperSavantFactory.Create(_alice);

        c.Name.Should().Be("Venser, Shaper Savant");
        c.ManaCost.Should().Be("{2}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Venser_HasFlashMarker()
    {
        var c = VenserShaperSavantFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Flash");
    }
    [Fact]
    public void Venser_EtbTrigger_HasSingleSpellOrPermanentTarget()
    {
        var c = VenserShaperSavantFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("spell or permanent");
        req.Intent.Should().Be(BotIntent.Bounce);
    }

    [Fact]
    public void Venser_Etb_BouncesTargetPermanentToOwnersHand()
    {
        var venser = VenserShaperSavantFactory.Create(_alice);
        venser.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(venser);

        var target = new Creature("Goblin Guide", "{R}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var etb = venser.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var e in etb.Effects) e.Execute();

        target.Zone.Should().Be(ZoneType.Hand,
            "ETB returns the targeted permanent to its owner's hand (CR 701.10)");
        _bob.Zones.Hand.GetCards().Should().Contain(target);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(target);
    }

    [Fact]
    public void Venser_Etb_ReturnsTargetSpellToOwnersHand_NotGraveyard()
    {
        var venser = VenserShaperSavantFactory.Create(_alice);
        venser.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(venser);

        var stack = new Majik.Core.Stack.Stack();
        venser = VenserShaperSavantFactory.Create(_alice, stack, triggers: null);
        venser.SetZone(ZoneType.Battlefield);

        // Bob has a spell on the stack.
        var spellCard = new Instant("Lightning Bolt", "{R}");
        spellCard.SetOwner(_bob);
        spellCard.SetController(_bob);
        spellCard.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(spellCard, _bob);
        stack.Push(spell);

        var etb = venser.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });
        foreach (var e in etb.Effects) e.Execute();

        spellCard.Zone.Should().Be(ZoneType.Hand,
            "returning a spell sends its card to its owner's hand, NOT the graveyard (CR 701.10)");
        _bob.Zones.Hand.GetCards().Should().Contain(spellCard);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(spellCard);
        stack.GetAll().Should().NotContain(spell, "the spell leaves the stack");
    }

    [Fact]
    public void Venser_Etb_ReturnsUncounterableSpell()
    {
        // CR 701.10 vs CR 701.5 — returning a spell to hand is NOT
        // countering it, so an uncounterable spell is still a legal target
        // and is moved to its owner's hand.
        var stack = new Majik.Core.Stack.Stack();
        var venser = VenserShaperSavantFactory.Create(_alice, stack, triggers: null);
        venser.SetZone(ZoneType.Battlefield);

        var spellCard = new Instant("Cavern-cast Spell", "{R}");
        spellCard.SetOwner(_bob);
        spellCard.SetController(_bob);
        spellCard.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(spellCard, _bob) { CannotBeCountered = true };
        stack.Push(spell);

        var etb = venser.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });
        foreach (var e in etb.Effects) e.Execute();

        spellCard.Zone.Should().Be(ZoneType.Hand,
            "uncounterable spells can still be returned to hand — bounce is not a counter");
        stack.GetAll().Should().NotContain(spell);
    }

    [Fact]
    public void Venser_Etb_OffBattlefieldPermanentTarget_NoOp()
    {
        // CR 608.2b — if the targeted permanent has already left the
        // battlefield by resolution, the effect does nothing.
        var venser = VenserShaperSavantFactory.Create(_alice);
        venser.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(venser);

        var target = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var etb = venser.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var e in etb.Effects) e.Execute();

        target.Zone.Should().Be(ZoneType.Graveyard,
            "illegal-on-resolution target — bounce no-ops (CR 608.2b)");
        _bob.Zones.Hand.GetCards().Should().NotContain(target);
    }

    [Fact]
    public void Venser_Etb_NoTargets_NoOp()
    {
        var venser = VenserShaperSavantFactory.Create(_alice);
        venser.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(venser);

        var etb = venser.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(System.Array.Empty<IReadOnlyList<object>>());

        var act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
