using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VaultbornTyrantFactory"/> (The Lost Caverns of
/// Ixalan, {5}{G}{G}).
///
/// Vaultborn Tyrant — Creature — Dinosaur 6/6. Oracle text (verified against
/// Scryfall):
///   "Trample
///    Whenever this creature or another creature you control with power 4 or
///    greater enters, you gain 3 life and draw a card.
///    When this creature dies, if it's not a token, create a token that's a
///    copy of it, except it's an artifact in addition to its other types."
///
/// Coverage (unique behaviour only — CardFactoryContractTests covers dispatch
/// + well-formedness automatically):
/// - Identity (Creature — Dinosaur, 6/6, {5}{G}{G}, green, Trample).
/// - Power-4+ ETB trigger fires on its OWN entry (6/6) → gain 3 life + draw.
/// - Power-4+ ETB fires on ANOTHER power-4+ creature you control entering.
/// - Power-3 creature entering does NOT fire (below threshold).
/// - Opponent's power-4+ creature entering does NOT fire ("you control").
/// - Dies (nontoken) → an artifact token copy of itself enters.
/// - A death-copy token is a token and carries NO dies trigger (no chain).
/// </summary>
[Trait("Color", "G")]
public class VaultbornTyrantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void VaultbornTyrant_Identity_CreatureDinosaur_6_6_Green5GG_Trample()
    {
        var card = VaultbornTyrantFactory.Create(_alice);

        card.Name.Should().Be("Vaultborn Tyrant");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{5}{G}{G}");
        card.ManaCostValue.TotalValue.Should().Be(7, "mana value of {5}{G}{G} is 7 (CR 202.3)");
        card.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(6);
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Trample", "CR 702.19");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ------------------------------------------------------------------
    // Power-4+ enters → gain 3 life + draw
    // ------------------------------------------------------------------

    [Fact]
    public void VaultbornTyrant_OwnEntry_FiresEtb_Gain3Life_DrawCard()
    {
        var (zones, stack, triggers) = BuildEngine();
        StackLibrary(_alice, 5);

        // Create already registers both triggers with the live manager.
        var card = VaultbornTyrantFactory.Create(_alice, zones, triggers);
        EnterBattlefield(card, _alice, zones);

        triggers.PendingCount.Should().Be(1,
            "Vaultborn Tyrant is a 6/6 — its own ETB satisfies 'power 4 or greater'");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(23, "gain 3 life (CR 119.3)");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "draw a card (CR 121.1)");
        _alice.Zones.Library.GetCards().Should().HaveCount(4);
    }

    [Fact]
    public void VaultbornTyrant_AnotherPower4PlusCreatureEnters_FiresEtb()
    {
        var (zones, stack, triggers) = BuildEngine();
        StackLibrary(_alice, 5);

        var card = VaultbornTyrantFactory.Create(_alice, zones, triggers);
        EnterBattlefield(card, _alice, zones);
        DrainPending(triggers, stack, _alice); // clear Vaultborn's own-ETB trigger

        // A 4/4 creature you control enters.
        var beast = new Creature("Beast", "{3}{G}", 4, 4);
        beast.SetOwner(_alice);
        EnterBattlefield(beast, _alice, zones);

        triggers.PendingCount.Should().Be(1,
            "another power-4+ creature you control entering triggers Vaultborn");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // 20 + 3 (Vaultborn's own ETB drained above) + 3 (the beast's ETB) = 26.
        _alice.LifeTotal.Should().Be(26, "gain 3 life on the second power-4+ ETB");
    }

    [Fact]
    public void VaultbornTyrant_Power3CreatureEnters_DoesNotFire()
    {
        var (zones, stack, triggers) = BuildEngine();
        StackLibrary(_alice, 5);

        var card = VaultbornTyrantFactory.Create(_alice, zones, triggers);
        EnterBattlefield(card, _alice, zones);
        DrainPending(triggers, stack, _alice);

        var smallGuy = new Creature("Squire", "{2}{G}", 3, 3);
        smallGuy.SetOwner(_alice);
        EnterBattlefield(smallGuy, _alice, zones);

        triggers.PendingCount.Should().Be(0,
            "a power-3 creature is below the 'power 4 or greater' threshold");
    }

    [Fact]
    public void VaultbornTyrant_OpponentPower4PlusCreatureEnters_DoesNotFire()
    {
        var (zones, stack, triggers) = BuildEngine();
        StackLibrary(_alice, 5);

        var card = VaultbornTyrantFactory.Create(_alice, zones, triggers);
        EnterBattlefield(card, _alice, zones);
        DrainPending(triggers, stack, _alice);

        var enemy = new Creature("Ogre", "{4}{R}", 5, 5);
        enemy.SetOwner(_bob);
        EnterBattlefield(enemy, _bob, zones);

        triggers.PendingCount.Should().Be(0,
            "the trigger is scoped to creatures YOU control (CR 109.5)");
    }

    // ------------------------------------------------------------------
    // Dies (nontoken) → artifact token copy
    // ------------------------------------------------------------------

    [Fact]
    public void VaultbornTyrant_Dies_CreatesArtifactTokenCopy()
    {
        var (zones, stack, triggers) = BuildEngine();
        StackLibrary(_alice, 5);

        var card = VaultbornTyrantFactory.Create(_alice, zones, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        triggers.BindCard(card);

        // Kill it: Battlefield → Graveyard.
        zones.MoveCard(card, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        // The dies trigger queues (the printed card is NOT a token).
        triggers.PendingCount.Should().Be(1, "the dies trigger fires on Battlefield → Graveyard");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 706.2 — a token that's a copy of it, except it's an artifact.
        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();
        token.Should().ContainSingle("the nontoken Vaultborn's death mints exactly one copy token");
        var copy = token[0];
        copy.Name.Should().Be("Vaultborn Tyrant");
        copy.IsToken.Should().BeTrue();
        copy.HasType(CardType.Creature).Should().BeTrue();
        copy.HasType(CardType.Artifact).Should().BeTrue(
            "the copy is an artifact in addition to its other types (CR 706.2 'except' clause)");
        copy.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        copy.BasePower.Should().Be(6);
        copy.BaseToughness.Should().Be(6);
    }

    [Fact]
    public void VaultbornTyrant_DeathCopyToken_HasNoDiesTrigger()
    {
        var (zones, stack, triggers) = BuildEngine();
        StackLibrary(_alice, 5);

        var card = VaultbornTyrantFactory.Create(_alice, zones, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        triggers.BindCard(card);

        zones.MoveCard(card, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var copy = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.IsToken);

        // "if it's not a token" — the copy IS a token, so it must NOT carry a
        // dies trigger (no infinite token chain). It still carries the power-4+
        // ETB trigger (CR 706.2 copies abilities).
        var triggerConditions = copy.Abilities.OfType<TriggeredAbility>().ToList();
        triggerConditions.Should().ContainSingle(
            "the death-copy token keeps the power-4+ ETB trigger but NOT the dies trigger");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }

    private static void StackLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature($"Pile {i}", "{0}", 1, 1);
            c.SetOwner(owner);
            owner.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    /// <summary>Route a card onto the battlefield via ZoneService (publishes
    /// CardMovedEvent so ETB triggers observe the entry).</summary>
    private static void EnterBattlefield(Permanent card, Player controller, ZoneService zones)
    {
        card.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(card);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller);
    }

    private static void DrainPending(TriggerManager triggers, Majik.Core.Stack.Stack stack, Player active)
    {
        if (triggers.PendingCount == 0) return;
        triggers.PutPendingTriggersOnStack(active);
        while (stack.Pop() is { } obj)
        {
            obj.Resolve();
        }
    }
}
