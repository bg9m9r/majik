using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BloodghastFactory"/> (Zendikar, {1}{B}).
///
/// Covers:
///   - Card identity (name, Vampire Spirit, 2/1, {1}{B}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch produces the same shape.
///   - Can't-block restriction present when <see cref="ContinuousEffectsService"/>
///     is wired (<see cref="CombatRestriction.CannotBlock"/> permanent).
///   - Landfall trigger in graveyard → card returns to battlefield when a
///     land enters under the controller's control.
///   - Landfall trigger does NOT fire when a land enters an opponent's
///     battlefield.
///   - Landfall trigger does NOT fire when Bloodghast is on the battlefield
///     (activeZones = Graveyard, CR 603.6d).
/// </summary>
public class BloodghastTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Bloodghast_Identity_VampireSpirit_2_1_AtCost1B()
    {
        var card = BloodghastFactory.Create(_alice);

        card.Name.Should().Be("Bloodghast");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Bloodghast_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Bloodghast", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bloodghast");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
    }

    [Fact]
    public void Bloodghast_HasLandfallTrigger_AttachedToCard()
    {
        var card = BloodghastFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one landfall trigger is attached in shape (CR 603.6d)");
    }

    // -----------------------------------------------------------------------
    // Can't block — CR 509.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void Bloodghast_CantBlockRestriction_RegisteredOnContinuousEffectsService()
    {
        var effects = new ContinuousEffectsService();
        var card = BloodghastFactory.Create(_alice, effects, zoneService: null, triggers: null, opponentLifeProvider: null);

        var restriction = GetRegisteredEffects(effects)
            .OfType<CombatRestrictionEffect>()
            .SingleOrDefault(r => r.Restriction == CombatRestriction.CannotBlock
                               && ReferenceEquals(r.Target, card));

        restriction.Should().NotBeNull(
            "can't block is a permanent restriction on Bloodghast (CR 509.1c)");
        restriction!.ExpiresAtEndOfTurn.Should().BeFalse(
            "can't block is not an end-of-turn effect — it is always active");
    }

    // -----------------------------------------------------------------------
    // Landfall trigger — CR 603.6d (graveyard-resident)
    // -----------------------------------------------------------------------

    [Fact]
    public void LandfallTrigger_WhileInGraveyard_ReturnsToBattlefield_WhenControllerLandEnters()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        // Place Bloodghast in Alice's graveyard.
        var card = BloodghastFactory.Create(
            _alice,
            effects: null,
            zoneService: zones,
            triggers: triggers,
            opponentLifeProvider: null);

        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        // Play a land from Alice's hand via ZoneService — publishes a
        // CardMovedEvent that the graveyard-resident trigger watches.
        var land = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        // One trigger should be pending; resolve it.
        triggers.PendingCount.Should().Be(1, "landfall trigger queued for Bloodghast in graveyard");

        // Drain the pending trigger onto the stack and resolve it.
        triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        var triggerOnStack = (TriggeredAbility)stack.Pop()!;
        triggerOnStack.Resolve();

        card.Zone.Should().Be(ZoneType.Battlefield,
            "Bloodghast returns from graveyard to battlefield on landfall (CR 603.6d)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);
    }

    [Fact]
    public void LandfallTrigger_DoesNotFire_WhenLandEntersOpponentsBattlefield()
    {
        var (zones, _, triggers, _) = BuildEngine();

        var card = BloodghastFactory.Create(
            _alice,
            effects: null,
            zoneService: zones,
            triggers: triggers,
            opponentLifeProvider: null);

        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        // Bob plays a land — should NOT trigger Alice's Bloodghast.
        var bobLand = new Land("Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        bobLand.SetOwner(_bob);
        bobLand.SetController(_bob);
        _bob.Zones.Hand.AddCard(bobLand);
        bobLand.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobLand, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "Bloodghast's landfall only triggers when a land enters under its controller's control");
        card.Zone.Should().Be(ZoneType.Graveyard, "Bloodghast stays in graveyard");
    }

    [Fact]
    public void LandfallTrigger_DoesNotFire_WhenBloodghastIsOnBattlefield()
    {
        var (zones, _, triggers, _) = BuildEngine();

        // Bloodghast is on the battlefield, NOT in the graveyard.
        var card = BloodghastFactory.Create(
            _alice,
            effects: null,
            zoneService: zones,
            triggers: triggers,
            opponentLifeProvider: null);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var land = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "Bloodghast's graveyard-resident trigger (CR 603.6d) is only active while in the graveyard");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, MajikStack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
