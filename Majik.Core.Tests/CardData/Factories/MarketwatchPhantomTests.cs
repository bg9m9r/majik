using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MarketwatchPhantomFactory"/> (Murders at Karlov
/// Manor, {1}{W}).
///
/// Oracle text:
///   "Whenever another creature you control with power 2 or less enters,
///    this creature gains flying until end of turn."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, type, mana cost, P/T, Spirit + Detective subtypes,
///   NO printed Flying).
/// - Trigger fires for ANOTHER creature you control with power 2 or less
///   entering → grants flying until end of turn.
/// - Does NOT fire for a power-3 creature you control entering.
/// - Does NOT fire for an opponent's creature entering.
/// - Does NOT fire on the Phantom's OWN entry ("another", CR 109.5).
/// </summary>
[Trait("Color", "W")]
public class MarketwatchPhantomTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void MarketwatchPhantom_Identity()
    {
        var phantom = MarketwatchPhantomFactory.Create(_alice);

        phantom.Name.Should().Be("Marketwatch Phantom");
        phantom.ManaCost.Should().Be("{1}{W}");
        phantom.HasType(CardType.Creature).Should().BeTrue();
        phantom.HasSubtype(CardSubtype.Spirit).Should().BeTrue("Marketwatch Phantom is a Spirit");
        phantom.HasSubtype(CardSubtype.Detective).Should().BeTrue("Marketwatch Phantom is a Detective");
        phantom.BasePower.Should().Be(2);
        phantom.BaseToughness.Should().Be(2);
        phantom.Owner.Should().BeSameAs(_alice);
        phantom.Controller.Should().BeSameAs(_alice);

        // No PRINTED Flying — Flying is only ever granted by the trigger.
        phantom.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Flying",
                "Marketwatch Phantom has no printed Flying — it must trigger for it");
        phantom.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the ETB-other-creature trigger is attached");
    }

    // ── Trigger fires + grants flying ───────────────────────────────────────

    [Fact]
    public void MarketwatchPhantom_AnotherSmallCreatureYouControlEnters_GrantsFlying()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var phantom = MarketwatchPhantomFactory.Create(_alice, triggers: null, effects: effects);
        _alice.Zones.Battlefield.AddCard(phantom);
        phantom.SetZone(ZoneType.Battlefield);

        var token = new Creature("Clue Token", "1G", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };

        var trigger = phantom.Abilities.OfType<TriggeredAbility>().Single();
        var etb = new CardMovedEvent(token, ZoneType.Stack, ZoneType.Battlefield);

        trigger.IsTriggered(etb).Should().BeTrue(
            "another creature you control with power 2 or less entering matches");

        CombatAbilities.HasFlying(phantom).Should().BeFalse("no flying before resolution");

        foreach (var e in trigger.Effects) e.Execute();

        CombatAbilities.HasFlying(phantom).Should().BeTrue(
            "the trigger grants flying until end of turn");
    }

    // ── Power-2-or-less filter ──────────────────────────────────────────────

    [Fact]
    public void MarketwatchPhantom_PowerThreeCreature_DoesNotFire()
    {
        var phantom = MarketwatchPhantomFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(phantom);
        phantom.SetZone(ZoneType.Battlefield);

        var big = new Creature("Big", "2G", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };

        var trigger = phantom.Abilities.OfType<TriggeredAbility>().Single();
        var etb = new CardMovedEvent(big, ZoneType.Stack, ZoneType.Battlefield);

        trigger.IsTriggered(etb).Should().BeFalse(
            "power 3 exceeds the 'power 2 or less' filter");
    }

    // ── "you control" scope ─────────────────────────────────────────────────

    [Fact]
    public void MarketwatchPhantom_OpponentsSmallCreature_DoesNotFire()
    {
        var phantom = MarketwatchPhantomFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(phantom);
        phantom.SetZone(ZoneType.Battlefield);

        var theirs = new Creature("Theirs", "1G", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var trigger = phantom.Abilities.OfType<TriggeredAbility>().Single();
        var etb = new CardMovedEvent(theirs, ZoneType.Stack, ZoneType.Battlefield);

        trigger.IsTriggered(etb).Should().BeFalse(
            "a creature an opponent controls is not 'a creature you control'");
    }

    // ── "another" self-exclusion ────────────────────────────────────────────

    [Fact]
    public void MarketwatchPhantom_OwnEntry_DoesNotFire()
    {
        var phantom = MarketwatchPhantomFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(phantom);
        phantom.SetZone(ZoneType.Battlefield);

        var trigger = phantom.Abilities.OfType<TriggeredAbility>().Single();
        var etb = new CardMovedEvent(phantom, ZoneType.Stack, ZoneType.Battlefield);

        trigger.IsTriggered(etb).Should().BeFalse(
            "the Phantom's OWN entry never fires it ('another', CR 109.5)");
    }
}
