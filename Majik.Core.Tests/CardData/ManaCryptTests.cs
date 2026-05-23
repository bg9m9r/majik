using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for Mana Crypt ({0}).
///
/// Oracle text:
///   "At the beginning of your upkeep, flip a coin. If you lose the flip,
///    Mana Crypt deals 3 damage to you."
///   "{T}: Add {C}{C}."
///
/// Covers:
///   - Card identity (Artifact, {0}, owner/controller, one trigger + one
///     mana ability).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - {T}: Add {C}{C} taps Mana Crypt and produces +2 generic.
///   - Upkeep with deterministic "lose the flip" → controller takes 3 damage.
///   - Upkeep with deterministic "win the flip" → no damage.
///   - Live wiring: registered TriggerManager surfaces a pending trigger on
///     the controller's Upkeep StepStartedEvent and not on the opponent's.
/// </summary>
public class ManaCryptTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ManaCrypt_Identity()
    {
        var crypt = ManaCryptFactory.Create(_alice);

        crypt.Name.Should().Be("Mana Crypt");
        crypt.ManaCost.Should().Be("{0}");
        crypt.HasType(CardType.Artifact).Should().BeTrue();
        crypt.Supertypes.Should().BeEmpty("Mana Crypt is a plain Artifact (not Legendary)");
        crypt.Owner.Should().BeSameAs(_alice);
        crypt.Controller.Should().BeSameAs(_alice);

        crypt.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        crypt.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ManaCrypt_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mana Crypt", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mana Crypt");
        card.ManaCost.Should().Be("{0}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ManaCrypt_Tap_AddsTwoColorless()
    {
        var crypt = ManaCryptFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(crypt);
        crypt.SetZone(ZoneType.Battlefield);

        var ability = crypt.Abilities.OfType<ManaAbility>().Single();
        var produced = ability.Activate();

        // {C}{C} buckets as +2 generic per CR 107.4c.
        produced.Generic.Should().Be(2);
        produced.TotalValue.Should().Be(2);
        crypt.IsTapped.Should().BeTrue("activating the tap mana ability taps the source");
    }

    [Fact]
    public void ManaCrypt_Upkeep_FlipLost_DealsThreeDamage()
    {
        // Force the flip-loses branch.
        var crypt = ManaCryptFactory.Create(_alice, triggers: null, coinLoses: () => true);
        _alice.Zones.Battlefield.AddCard(crypt);
        crypt.SetZone(ZoneType.Battlefield);

        var lifeBefore = _alice.LifeTotal;

        var trigger = crypt.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(lifeBefore - 3,
            "the controller lost the coin flip, so Mana Crypt deals 3 damage");
    }

    [Fact]
    public void ManaCrypt_Upkeep_FlipWon_NoDamage()
    {
        // Force the flip-wins branch.
        var crypt = ManaCryptFactory.Create(_alice, triggers: null, coinLoses: () => false);
        _alice.Zones.Battlefield.AddCard(crypt);
        crypt.SetZone(ZoneType.Battlefield);

        var lifeBefore = _alice.LifeTotal;

        var trigger = crypt.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(lifeBefore,
            "the controller won the coin flip — no damage clause fires");
    }

    [Fact]
    public void ManaCrypt_Upkeep_NotOnBattlefield_DoesNothing()
    {
        // Trigger body re-checks Zone == Battlefield (defensive — matches
        // Mana Vault's ZoneType.Battlefield gate). If the card was moved
        // off the battlefield between trigger registration and resolution,
        // the damage clause is skipped.
        var crypt = ManaCryptFactory.Create(_alice, triggers: null, coinLoses: () => true);
        // Intentionally not added to battlefield zone.

        var lifeBefore = _alice.LifeTotal;

        var trigger = crypt.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(lifeBefore,
            "Mana Crypt off the battlefield — no upkeep damage");
    }

    [Fact]
    public void ManaCrypt_LiveWiring_UpkeepRegistersPendingTrigger_OnControllerOnly()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var crypt = ManaCryptFactory.Create(_alice, triggers, coinLoses: () => true);
        _alice.Zones.Battlefield.AddCard(crypt);
        crypt.SetZone(ZoneType.Battlefield);

        // Bob's upkeep — Alice's Mana Crypt does NOT trigger.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "Mana Crypt triggers only on its controller's own upkeep");

        // Alice's upkeep — trigger surfaces as pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }
}
