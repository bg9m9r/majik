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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Kataki, War's Wage (Saviors of Kamigawa, {1}{W}).
///
/// Oracle text (verified against Scryfall):
///   "All artifacts have 'At the beginning of your upkeep, sacrifice this
///    artifact unless you pay {1}.'"
///
/// Exercises the TRIGGERED variant of the CR 613.1f Layer-6 group
/// ability-grant: <see cref="GrantAbilityToGroupStaticEffect"/> /
/// <see cref="GrantAbilityToGroupLifecycle"/> now register a granted
/// <see cref="ITriggeredAbility"/> with a live <see cref="TriggerManager"/>
/// (and unregister it when membership / source changes), so the granted
/// upkeep tax actually FIRES — the activated/mana group-grant (#2322,
/// Chromatic Lantern) covered only abilities that surface through the
/// permanent's <c>Abilities</c> list with no manager wiring.
///
/// "All artifacts" is symmetric (every artifact on the battlefield, any
/// controller); each granted trigger is scoped to the BEARER's own
/// controller (CR 611.2c / 603.1) — it fires on that controller's upkeep and
/// that controller pays / sacrifices.
/// </summary>
public class KatakiWarsWageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public KatakiWarsWageTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
    }

    private Artifact MakeArtifact(string name, Player owner)
    {
        var a = new Artifact(name, "{2}");
        a.ChangeOwner(owner);
        a.ChangeController(owner);
        return a;
    }

    private void PutOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private static bool IsArtifactOnBattlefield(Permanent p) =>
        p.HasType(CardType.Artifact) && p.Zone == ZoneType.Battlefield;

    private IEnumerable<Permanent> AllBattlefield() =>
        new[] { _alice, _bob }
            .SelectMany(pl => pl.Zones.Battlefield.GetCards())
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);

    private Creature MakeKataki()
    {
        return KatakiWarsWageFactory.Create(
            _alice, _effects, _bus, _triggers,
            membershipProvider: AllBattlefield);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Kataki_IsTwoManaLegendarySpirit_2_1()
    {
        var kataki = KatakiWarsWageFactory.Create(_alice);

        kataki.Name.Should().Be("Kataki, War's Wage");
        kataki.HasType(CardType.Creature).Should().BeTrue();
        kataki.ManaCostValue.TotalValue.Should().Be(2);
        kataki.Power.Should().Be(2);
        kataki.Toughness.Should().Be(1);
        kataki.Supertypes.Should().Contain(CardSupertype.Legendary);
        kataki.Subtypes.Should().Contain(CardSubtype.Spirit);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Kataki()
    {
        var kataki = NamedCardFactory.Create("Kataki, War's Wage", _alice);
        kataki.Should().BeOfType<Creature>();
        kataki.Name.Should().Be("Kataki, War's Wage");
    }

    // -----------------------------------------------------------------------
    // Grant the upkeep tax to every artifact + register it with the manager
    // -----------------------------------------------------------------------

    [Fact]
    public void Kataki_GrantsUpkeepTriggerToArtifact_RegisteredWithManager()
    {
        var widget = MakeArtifact("Widget", _alice);
        PutOnBattlefield(widget, _alice);

        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        var granted = widget.Abilities.OfType<ITriggeredAbility>().ToList();
        granted.Should().HaveCount(1, "Kataki grants the upkeep tax to the artifact");
        _triggers.IsRegistered(granted[0]).Should().BeTrue(
            "the granted triggered ability must be registered with the live TriggerManager so it can fire");
    }

    [Fact]
    public void Kataki_ArtifactEnteringLater_AlsoGainsTheTrigger()
    {
        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        // Artifact enters AFTER Kataki — live membership must pick it up.
        var widget = MakeArtifact("Widget", _alice);
        PutOnBattlefield(widget, _alice);

        var granted = widget.Abilities.OfType<ITriggeredAbility>().ToList();
        granted.Should().HaveCount(1, "a later artifact still gains the tax (CR 611.2c)");
        _triggers.IsRegistered(granted[0]).Should().BeTrue();
    }

    [Fact]
    public void Kataki_DoesNotGrantToNonArtifacts()
    {
        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.ChangeOwner(_alice);
        bear.ChangeController(_alice);
        PutOnBattlefield(bear, _alice);

        bear.Abilities.OfType<ITriggeredAbility>().Should().BeEmpty(
            "the grant scope is 'artifacts' — a plain creature gains nothing");
    }

    // -----------------------------------------------------------------------
    // The granted trigger actually FIRES on the bearer-controller's upkeep
    // -----------------------------------------------------------------------

    [Fact]
    public void Kataki_GrantedTrigger_FiresOnArtifactControllerUpkeep()
    {
        var widget = MakeArtifact("Widget", _alice);
        PutOnBattlefield(widget, _alice);

        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        // Bob's upkeep — Alice's artifact tax does NOT trigger.
        _bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _bob));
        _triggers.PendingCount.Should().Be(0,
            "the granted tax is scoped to the bearer's controller (Alice), not Bob");

        // Alice's upkeep — the artifact's granted tax surfaces as pending.
        _bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));
        _triggers.PendingCount.Should().Be(1,
            "Alice's artifact taxes on Alice's upkeep");
    }

    [Fact]
    public void Kataki_SymmetricGrant_OpponentArtifactTaxedOnOpponentUpkeep()
    {
        // "ALL artifacts" — Bob's artifact is taxed too, on BOB's upkeep, and
        // Bob is the one who must pay.
        var bobWidget = MakeArtifact("Bob's Widget", _bob);
        PutOnBattlefield(bobWidget, _bob);

        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        bobWidget.Abilities.OfType<ITriggeredAbility>().Should().HaveCount(1,
            "Kataki grants to ALL artifacts, not just the controller's");

        _bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));
        _triggers.PendingCount.Should().Be(0,
            "Bob's artifact tax is scoped to Bob's upkeep, not Alice's");

        _bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _bob));
        _triggers.PendingCount.Should().Be(1,
            "Bob's artifact taxes on Bob's upkeep");
    }

    // -----------------------------------------------------------------------
    // Resolution — pay {1} keeps the artifact; failing to pay sacrifices it
    // -----------------------------------------------------------------------

    [Fact]
    public void Kataki_Resolution_SacrificesArtifact_WhenControllerCannotPay()
    {
        var widget = MakeArtifact("Widget", _alice);
        PutOnBattlefield(widget, _alice);

        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        var tax = widget.Abilities.OfType<TriggeredAbility>().Single();
        // Alice's pool is empty — she cannot pay {1}; the artifact is sacrificed.
        foreach (var e in tax.Effects) e.Execute();

        widget.Zone.Should().Be(ZoneType.Graveyard,
            "unpaid tax sacrifices the artifact (Battlefield -> Graveyard)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(widget);
    }

    [Fact]
    public void Kataki_Resolution_KeepsArtifact_WhenControllerPaysOne()
    {
        var widget = MakeArtifact("Widget", _alice);
        PutOnBattlefield(widget, _alice);

        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        // Give Alice one generic mana so she can pay the {1} tax.
        _alice.AddManaToPool(ManaCost.Parse("{1}"));

        var tax = widget.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in tax.Effects) e.Execute();

        widget.Zone.Should().Be(ZoneType.Battlefield,
            "paying {1} keeps the artifact on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Membership revocation — Kataki leaves -> taxes lift + unregister
    // -----------------------------------------------------------------------

    [Fact]
    public void Kataki_Leaves_ArtifactLosesTax_AndManagerUnregisters()
    {
        var widget = MakeArtifact("Widget", _alice);
        PutOnBattlefield(widget, _alice);

        var kataki = MakeKataki();
        PutOnBattlefield(kataki, _alice);

        var tax = widget.Abilities.OfType<ITriggeredAbility>().Single();
        _triggers.IsRegistered(tax).Should().BeTrue();

        // Kataki leaves — revoke the grant from every artifact.
        _zones.MoveCard(kataki, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        _effects.Prune();

        widget.Abilities.OfType<ITriggeredAbility>().Should().BeEmpty(
            "with Kataki gone the artifact is no longer taxed");
        _triggers.IsRegistered(tax).Should().BeFalse(
            "the granted trigger is unregistered from the manager when the grant ends");

        // And it no longer fires.
        _bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));
        _triggers.PendingCount.Should().Be(0);
    }
}
