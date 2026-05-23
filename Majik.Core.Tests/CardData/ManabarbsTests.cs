using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Manabarbs (Sixth Edition, {2}{R}{R}).
///
/// Covers:
///   - Card identity (name, type, mana cost, owner/controller, one triggered ability).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - Alice taps a Mountain for {R} → Alice loses 1 life (symmetric, fires
///     even for Manabarbs's controller).
///   - Bob taps a Forest for {G} → Bob loses 1 life (symmetric, fires for
///     opponents too).
///   - Mox Opal mana ability activation (non-land source) → no damage.
/// </summary>
public class ManabarbsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Manabarbs_Identity_EnchantmentAt2RR()
    {
        var barbs = ManabarbsFactory.Create(_alice);

        barbs.Name.Should().Be("Manabarbs");
        barbs.ManaCost.Should().Be("{2}{R}{R}");
        barbs.HasType(CardType.Enchantment).Should().BeTrue();
        barbs.Owner.Should().BeSameAs(_alice);
        barbs.Controller.Should().BeSameAs(_alice);
        barbs.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Manabarbs_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Manabarbs", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Manabarbs");
        card.ManaCost.Should().Be("{2}{R}{R}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AliceTapsMountainForMana_AliceTakesOne()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        // Manabarbs on the battlefield under Alice's control.
        var barbs = ManabarbsFactory.Create(_alice, triggers);
        barbs.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(barbs);

        // Alice's Mountain — basic land with {T}: Add {R}.
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        mountain.SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);
        var manaAbility = mountain.Abilities.OfType<IManaAbility>().Single();

        var aliceLifeBefore = _alice.LifeTotal;

        activator.ActivateManaAbility(manaAbility, _alice);

        // Trigger queued; not yet resolved. Mana ability itself doesn't
        // use the stack (CR 605.3); the Manabarbs trigger does.
        triggers.PendingCount.Should().Be(1);
        _alice.LifeTotal.Should().Be(aliceLifeBefore, "trigger has not resolved yet");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1, "Manabarbs is symmetric — fires for controller too");
    }

    [Fact]
    public void BobTapsForestForMana_BobTakesOne_Symmetric()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        // Manabarbs is Alice's, but it still pings Bob — oracle reads
        // "a player", not "an opponent".
        var barbs = ManabarbsFactory.Create(_alice, triggers);
        barbs.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(barbs);

        var forest = (Land)NamedCardFactory.Create("Forest", _bob);
        forest.SetController(_bob);
        forest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(forest);
        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        activator.ActivateManaAbility(manaAbility, _bob);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 1, "the activator takes the damage");
        _alice.LifeTotal.Should().Be(aliceLifeBefore, "Manabarbs does not damage non-activators");
    }

    [Fact]
    public void MoxOpalManaAbility_DoesNotTriggerManabarbs()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var barbs = ManabarbsFactory.Create(_alice, triggers);
        barbs.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(barbs);

        // Mox Opal — Legendary Artifact with five colour mana abilities
        // gated on Metalcraft. Seed two more artifacts under Alice's
        // control so the gate (>= 3 artifacts) is met. CR 702.95.
        var mox = (Artifact)NamedCardFactory.Create("Mox Opal", _alice);
        mox.SetController(_alice);
        mox.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mox);

        for (int i = 0; i < 2; i++)
        {
            var filler = new Artifact($"Filler Artifact {i}", "{0}");
            filler.SetOwner(_alice);
            filler.SetController(_alice);
            filler.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(filler);
        }

        var moxMana = mox.Abilities.OfType<IManaAbility>().First();

        var aliceLifeBefore = _alice.LifeTotal;

        activator.ActivateManaAbility(moxMana, _alice);

        triggers.PendingCount.Should().Be(0, "Mox Opal is an Artifact, not a Land — Manabarbs's source gate rejects it");
        _alice.LifeTotal.Should().Be(aliceLifeBefore);
    }

    [Fact]
    public void ManaAbilityActivatedEvent_IsPublishedOnActivation()
    {
        // Sanity check on the event-bus surface that Manabarbs subscribes
        // to: ManaAbilityActivator publishes ManaAbilityActivatedEvent
        // for every successful activation, regardless of source.
        var bus = new EventBus();
        var activator = new ManaAbilityActivator(bus);

        ManaAbilityActivatedEvent? captured = null;
        bus.Subscribe<ManaAbilityActivatedEvent>(e => captured = e);

        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        mountain.SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        var manaAbility = mountain.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _alice);

        captured.Should().NotBeNull();
        captured!.Player.Should().BeSameAs(_alice);
        captured.Source.Should().BeSameAs(mountain);
        captured.ManaGenerated.Should().Be(ManaCost.Parse("R"));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers, ManaAbilityActivator activator) BuildEngine()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var activator = new ManaAbilityActivator(bus);
        return (bus, stack, triggers, activator);
    }
}
