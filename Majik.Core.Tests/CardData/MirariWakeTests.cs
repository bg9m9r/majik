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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MirariWakeFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller wiring).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Anthem (+1/+1) to controller's creatures via
///   <see cref="ControllerCreatureAnthemEffect"/>.
/// - Opponent's creatures untouched.
/// - LTB lifts the bonus.
/// - Two copies stack additively.
///
/// Mana-tap doubling is deferred (see factory xmldoc) — not covered here.
/// </summary>
public class MirariWakeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MirarisWake_Identity()
    {
        var card = MirariWakeFactory.Create(_alice);

        card.Name.Should().Be("Mirari's Wake");
        card.ManaCost.Should().Be("{3}{G}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MirarisWake_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mirari's Wake", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Mirari's Wake");
    }

    [Fact]
    public void MirarisWake_BuffsControllersCreatures_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake = MirariWakeFactory.Create(_alice, svc);
        wake.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(3,
            "Mirari's Wake gives all creatures you control +1/+1 (2→3).");
        bear.GetToughness().Should().Be(3);
    }

    [Fact]
    public void MirarisWake_DoesNotPump_OpponentCreatures()
    {
        var svc = new ContinuousEffectsService();

        var oppBear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake = MirariWakeFactory.Create(_alice, svc);
        wake.Zone = ZoneType.Battlefield;

        oppBear.GetPower().Should().Be(2,
            "Mirari's Wake is scoped to controller's creatures (CR 109.5 — 'you').");
        oppBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MirarisWake_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake = MirariWakeFactory.Create(_alice, svc);
        wake.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(3);

        // Mirari's Wake LTB → IsActive gate falls (CR 613).
        wake.SetZone(ZoneType.Graveyard);

        bear.GetPower().Should().Be(2, "bonus lifts on LTB");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void TwoMirarisWakes_StackAdditively()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake1 = MirariWakeFactory.Create(_alice, svc);
        wake1.Zone = ZoneType.Battlefield;

        var wake2 = MirariWakeFactory.Create(_alice, svc);
        wake2.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(4, "two Mirari's Wakes stack: 2 base + 1 + 1 = 4.");
        bear.GetToughness().Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // "Whenever you tap a land for mana, add one mana of any type that land
    //  produced." (CR 605.1b — a triggered mana ability.)
    // -----------------------------------------------------------------------

    [Fact]
    public void MirarisWake_HasTapLandForManaTrigger()
    {
        var svc = new ContinuousEffectsService();
        var wake = MirariWakeFactory.Create(_alice, svc);

        // The mana-doubling trigger has no targets.
        var tapTrigger = wake.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        tapTrigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    /// <summary>
    /// You tap a Forest for {G}: the land adds its own {G}, and Mirari's Wake's
    /// trigger adds an additional {G} (one mana of the type the land produced).
    /// </summary>
    [Fact]
    public void TappingYourLandForMana_AddsAdditionalManaOfThatType()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var wake = MirariWakeFactory.Create(_alice, triggers);
        wake.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wake);

        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _alice);

        // The Forest's own {G} is in the pool immediately; the trigger is
        // pending (CR 605.3 — the land's mana ability doesn't use the stack).
        _alice.ManaPool.Green.Should().Be(1, "the Forest's own {G}");
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Green.Should().Be(2,
            "Mirari's Wake adds an additional {G} of the type the land produced (CR 605.1b)");
    }

    /// <summary>
    /// "Whenever you tap a LAND for mana" — tapping a non-land mana source
    /// (a mana rock) does not trigger, even though it's yours.
    /// </summary>
    [Fact]
    public void TappingYourNonLandForMana_DoesNotTrigger()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var wake = MirariWakeFactory.Create(_alice, triggers);
        wake.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wake);

        // A mana rock: an Artifact with "{T}: Add {C}". Not a land.
        var rock = new Artifact("Mind Stone", "2");
        rock.SetController(_alice);
        rock.SetZone(ZoneType.Battlefield);
        rock.AddAbility(new ManaAbility(rock, _alice, ManaCost.Parse("C")));
        _alice.Zones.Battlefield.AddCard(rock);

        var manaAbility = rock.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _alice);

        triggers.PendingCount.Should().Be(0, "a mana rock isn't a land");
    }

    /// <summary>
    /// The clause is "Whenever YOU tap a land for mana" — only the Wake's
    /// controller tapping a land counts. An opponent tapping their own land
    /// does not trigger.
    /// </summary>
    [Fact]
    public void OpponentTappingTheirLand_DoesNotTrigger()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var wake = MirariWakeFactory.Create(_alice, triggers);
        wake.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wake);

        var bobForest = (Land)NamedCardFactory.Create("Forest", _bob);
        bobForest.SetController(_bob);
        bobForest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobForest);

        var manaAbility = bobForest.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _bob);

        triggers.PendingCount.Should().Be(0, "only YOU tapping a land triggers it");
        _alice.ManaPool.Green.Should().Be(0);
    }

    private static (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers, ManaAbilityActivator activator) BuildEngine()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var activator = new ManaAbilityActivator(bus);
        return (bus, stack, triggers, activator);
    }
}
