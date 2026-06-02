using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Valley Floodcaller (Bloomburrow, {2}{U}, Creature — Otter
/// Wizard 2/2).
///
/// Covers:
///   - Card identity (name, type, Otter + Wizard subtypes, P/T, mana cost,
///     owner/controller) + NamedCardFactory dispatch shape.
///   - Flash keyword marker on the Floodcaller itself (CR 702.8).
///   - "You may cast noncreature spells as though they had flash." (CR
///     117.1a / 702.8) — grants flash to the controller's noncreature cards
///     in hand while the Floodcaller is on the battlefield; does NOT grant
///     flash to creature cards nor to an opponent's cards; lifts on LTB.
///   - Cast-noncreature trigger pumps +1/+1 EOT and untaps Birds/Frogs/
///     Otters/Rats you control (CR 603.1 / Layer 7c / CR 514.2); creature
///     spell casts do not trigger; opponent casts do not trigger; non-tribal
///     creatures are unaffected.
/// </summary>
[Trait("Color", "U")]
public class ValleyFloodcallerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ValleyFloodcallerFactoryTests() => FlashGrantRegistry.Clear();

    public void Dispose() => FlashGrantRegistry.Clear();

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "U") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    private static Creature TribalCreature(Player owner, CardSubtype subtype, string name)
    {
        var c = new Creature(name, "{1}", 1, 1, subtypes: new[] { subtype });
        c.SetOwner(owner);
        c.SetController(owner);
        c.ActiveEffects = new ContinuousEffectsService();
        return c;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ValleyFloodcaller_Identity_OtterWizard_2_2_At2U()
    {
        var card = ValleyFloodcallerFactory.Create(_alice);

        card.Name.Should().Be("Valley Floodcaller");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ValleyFloodcaller_NamedCardFactory_DispatchesShape()
    {
        var card = Majik.Core.CardData.NamedCardFactory.Create("Valley Floodcaller", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Valley Floodcaller");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void ValleyFloodcaller_HasFlashKeywordMarker()
    {
        var card = ValleyFloodcallerFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Flash");
    }

    [Fact]
    public void ValleyFloodcaller_HasOneCastTriggeredAbility()
    {
        var card = ValleyFloodcallerFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // "You may cast noncreature spells as though they had flash."
    // -----------------------------------------------------------------------

    [Fact]
    public void OnBattlefield_GrantsFlashToOwnersNoncreatureSpellInHand()
    {
        var (bus, zones, _, _) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(floodcaller);
        floodcaller.SetZone(ZoneType.Hand);
        zones.MoveCardTo(floodcaller, ZoneType.Battlefield, controller: _alice);

        // A sorcery in Alice's hand — normally sorcery-speed only — gets
        // flash from the Floodcaller's static.
        var sorcery = new Sorcery("Divination", "{2}{U}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(sorcery);
        sorcery.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(sorcery).Should().BeTrue();

        var validator = new ActionValidator();
        var action = new CastSpellAction(sorcery, _alice, sorcerySpeedAvailable: false);
        validator.ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void OnBattlefield_DoesNotGrantFlashToOwnersCreatureSpell()
    {
        var (bus, zones, _, _) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(floodcaller);
        floodcaller.SetZone(ZoneType.Hand);
        zones.MoveCardTo(floodcaller, ZoneType.Battlefield, controller: _alice);

        // A vanilla creature in Alice's hand — the static only covers
        // *noncreature* spells, so this stays sorcery-speed only.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(bear).Should().BeFalse();
    }

    [Fact]
    public void OnBattlefield_DoesNotGrantFlashToOpponentsNoncreatureSpell()
    {
        var (bus, zones, _, _) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(floodcaller);
        floodcaller.SetZone(ZoneType.Hand);
        zones.MoveCardTo(floodcaller, ZoneType.Battlefield, controller: _alice);

        // "YOU may cast" — Bob's noncreature spell is unaffected.
        var bobSorcery = new Sorcery("Bob's Divination", "{2}{U}") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobSorcery);
        bobSorcery.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(bobSorcery).Should().BeFalse();
    }

    [Fact]
    public void LeavesBattlefield_FlashGrantLifted()
    {
        var (bus, zones, _, _) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(floodcaller);
        floodcaller.SetZone(ZoneType.Hand);
        zones.MoveCardTo(floodcaller, ZoneType.Battlefield, controller: _alice);

        var sorcery = new Sorcery("Divination", "{2}{U}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(sorcery);
        sorcery.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(sorcery).Should().BeTrue();

        zones.MoveCardTo(floodcaller, ZoneType.Graveyard, controller: _alice);

        TimingRules.CanCastAtInstantSpeed(sorcery).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Cast-noncreature trigger: pump +1/+1 EOT + untap the tribe
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_PumpsAndUntapsTribe()
    {
        var (bus, _, stack, triggers) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers);
        floodcaller.SetController(_alice);
        floodcaller.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(floodcaller);

        // Tribal members on Alice's battlefield, one of each relevant
        // subtype, all tapped so we can observe the untap.
        var bird = TribalCreature(_alice, CardSubtype.Bird, "Storm Crow");
        var frog = TribalCreature(_alice, CardSubtype.Frog, "Bullfrog");
        var rat = TribalCreature(_alice, CardSubtype.Rat, "Pack Rat");
        foreach (var c in new[] { bird, frog, rat })
        {
            _alice.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
            c.Tap();
        }

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Opt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        foreach (var c in new[] { bird, frog, rat })
        {
            c.Power.Should().Be(2, "each tribal member gets +1/+1");
            c.Toughness.Should().Be(2);
            c.IsTapped.Should().BeFalse("each tribal member is untapped");
        }
    }

    [Fact]
    public void CastingNoncreatureSpell_DoesNotPumpNonTribalCreature()
    {
        var (bus, _, stack, triggers) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers);
        floodcaller.SetController(_alice);
        floodcaller.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(floodcaller);

        // A Goblin (not in the Bird/Frog/Otter/Rat set) — must be untouched.
        var goblin = TribalCreature(_alice, CardSubtype.Goblin, "Goblin Guide");
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);
        goblin.Tap();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Opt")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        goblin.Power.Should().Be(1, "non-tribal creatures get no pump");
        goblin.Toughness.Should().Be(1);
        goblin.IsTapped.Should().BeTrue("non-tribal creatures are not untapped");
    }

    [Fact]
    public void CastingCreatureSpell_NoTrigger()
    {
        var (bus, _, _, triggers) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers);
        floodcaller.SetController(_alice);
        floodcaller.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(floodcaller);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void OpponentCastsNoncreatureSpell_NoTrigger()
    {
        var (bus, _, _, triggers) = BuildEngine();

        var floodcaller = ValleyFloodcallerFactory.Create(_alice, bus, triggers);
        floodcaller.SetController(_alice);
        floodcaller.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(floodcaller);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (EventBus bus, ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (bus, zones, stack, triggers);
    }
}
