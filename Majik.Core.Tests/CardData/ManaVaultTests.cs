using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for Mana Vault (Limited Edition Alpha, {1}).
///
/// Oracle text:
///   "Mana Vault doesn't untap during your untap step." (v1 deferred — no
///   engine surface for "doesn't untap" yet; see <see cref="ManaVaultFactory"/> xmldoc.)
///   "At the beginning of your upkeep, if Mana Vault is tapped, you may
///    pay {4}. If you don't, Mana Vault deals 1 damage to you."
///   "{T}: Add {C}{C}{C}."
///
/// Covers:
///   - Card identity (Artifact, {1}, owner/controller, one trigger + one mana ability).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - {T}: Add {C}{C}{C} taps Mana Vault and produces +3 generic.
///   - Upkeep with tapped Mana Vault + insufficient mana → controller loses 1 life.
///   - Upkeep with tapped Mana Vault + {4} pre-staged → mana is consumed, no life loss.
///   - Upkeep with UNtapped Mana Vault → intervening "if" fails, no payment, no damage.
///   - Live wiring: registered TriggerManager surfaces a pending trigger on the
///     controller's Upkeep StepStartedEvent.
/// </summary>
public class ManaVaultTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ManaVault_Identity()
    {
        var vault = ManaVaultFactory.Create(_alice);

        vault.Name.Should().Be("Mana Vault");
        vault.ManaCost.Should().Be("{1}");
        vault.HasType(CardType.Artifact).Should().BeTrue();
        vault.Owner.Should().BeSameAs(_alice);
        vault.Controller.Should().BeSameAs(_alice);

        // One upkeep trigger + one tap mana ability.
        vault.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        vault.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ManaVault_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mana Vault", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mana Vault");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ManaVault_Tap_AddsThreeColorless()
    {
        var vault = ManaVaultFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vault);
        vault.SetZone(ZoneType.Battlefield);

        var ability = vault.Abilities.OfType<ManaAbility>().Single();
        var produced = ability.Activate();

        // {C}{C}{C} buckets as +3 generic per CR 107.4c.
        produced.Generic.Should().Be(3);
        produced.TotalValue.Should().Be(3);
        vault.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void ManaVault_Upkeep_Tapped_NoMana_TakesOneDamage()
    {
        var vault = ManaVaultFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vault);
        vault.SetZone(ZoneType.Battlefield);

        // Tap it (simulate end-of-prior-turn tapped Mana Vault).
        vault.Tap();
        vault.IsTapped.Should().BeTrue();

        var lifeBefore = _alice.LifeTotal;

        var trigger = vault.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(lifeBefore - 1,
            "no mana was pre-staged so PayMana({4}) fails → 1 damage to controller");
    }

    [Fact]
    public void ManaVault_Upkeep_Tapped_WithFourGeneric_PaysAndNoDamage()
    {
        var vault = ManaVaultFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vault);
        vault.SetZone(ZoneType.Battlefield);
        vault.Tap();

        // Pre-stage 4 generic in Alice's mana pool. The v1 "may"
        // collapses to pay-if-able — the trigger will consume {4} and
        // skip the damage clause.
        _alice.AddManaToPool(ManaCost.Parse("4"));

        var lifeBefore = _alice.LifeTotal;

        var trigger = vault.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(lifeBefore, "Alice paid {4}, so Mana Vault doesn't deal 1 damage");
        _alice.ManaPool.Total.Should().Be(0, "PayMana({4}) consumed the pre-staged mana");
    }

    [Fact]
    public void ManaVault_Upkeep_Untapped_DoesNothing()
    {
        // Printed "if Mana Vault is tapped" intervening-if (CR 603.4) —
        // re-checked at resolution. If Mana Vault is untapped when the
        // trigger resolves, the pay-or-damage clause does not fire.
        var vault = ManaVaultFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vault);
        vault.SetZone(ZoneType.Battlefield);
        // (left untapped on purpose)

        var lifeBefore = _alice.LifeTotal;

        var trigger = vault.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(lifeBefore, "the intervening 'if tapped' check fails — no payment, no damage");
    }

    [Fact]
    public void ManaVault_LiveWiring_UpkeepRegistersPendingTrigger_OnControllerOnly()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vault = ManaVaultFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(vault);
        vault.SetZone(ZoneType.Battlefield);

        // Bob's upkeep — Alice's Mana Vault does NOT trigger.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "Mana Vault triggers only on its controller's own upkeep");

        // Alice's upkeep — trigger surfaces as pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }
}
