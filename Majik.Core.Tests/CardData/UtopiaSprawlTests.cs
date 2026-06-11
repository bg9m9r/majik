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
/// Tests for Utopia Sprawl — Enchantment — Aura {G}.
///
///   "Enchant Forest
///    As this Aura enters, choose a color.
///    Whenever enchanted Forest is tapped for mana, its controller adds an
///    additional one mana of the chosen color."
///
/// Covers:
///   - Card identity (name, type, Aura subtype, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - The mana-doubling trigger fires when the enchanted Forest is tapped,
///     adding one mana of the chosen color to the Forest's controller's pool.
///   - Tapping a DIFFERENT Forest (not the enchanted one) does not trigger.
///   - "Enchant Forest" cast-time predicate only accepts Forests.
/// </summary>
public class UtopiaSprawlTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void UtopiaSprawl_Identity_EnchantmentAuraAtG()
    {
        var sprawl = UtopiaSprawlFactory.Create(_alice);

        sprawl.Name.Should().Be("Utopia Sprawl");
        sprawl.HasType(CardType.Enchantment).Should().BeTrue();
        sprawl.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        sprawl.IsAura.Should().BeTrue();
        sprawl.ManaCost.Should().Be("{G}");
        sprawl.Owner.Should().BeSameAs(_alice);
        sprawl.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UtopiaSprawl_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Utopia Sprawl", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Utopia Sprawl");
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void UtopiaSprawl_WithChosenColor_HasOneManaTrigger()
    {
        var sprawl = UtopiaSprawlFactory.Create(_alice, ManaColor.Blue, triggers: null);

        sprawl.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    /// <summary>
    /// Single-arg dispatch — the routed production entry point — attaches the
    /// triggered mana ability (defaulting to Green) so the routed build, which
    /// runs no triggered-ability binder, still produces a card whose bonus is
    /// present. Mirrors the shape posture of every other routed-trigger factory.
    /// </summary>
    [Fact]
    public void UtopiaSprawl_SingleArgDispatch_AttachesManaTrigger()
    {
        var sprawl = UtopiaSprawlFactory.Create(_alice);

        sprawl.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    /// <summary>
    /// PRODUCTION wiring: the routed instance-swap build dispatches the
    /// single-arg <see cref="UtopiaSprawlFactory.Create(Player)"/> overload and
    /// runs no triggered-ability binder, so the factory itself must register
    /// the trigger with the live per-game <see cref="TriggerManager"/> — looked
    /// up from the ambient <see cref="TriggerManagerRegistry"/> (installed at
    /// game start). This test drives exactly that path: set the ambient manager,
    /// build through <see cref="NamedCardFactory.Create(string, Player)"/> (the
    /// routed dispatcher), attach to a Forest, tap it, and assert the default
    /// Green bonus actually surfaces and resolves end-to-end. Without the
    /// ambient registration the trigger would be inert in a real match.
    /// </summary>
    [Fact]
    public void RoutedBuild_RegistersTriggerWithAmbientManager_AndFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var activator = new ManaAbilityActivator(bus);

        // Install the ambient per-game manager (GameDriver.RunGameAsync does
        // this in prod). PushScope isolates it from any concurrent test.
        using var scope = TriggerManagerRegistry.PushScope();
        TriggerManagerRegistry.Set(triggers);

        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        // Routed production entry point — single-arg dispatch.
        var sprawl = (Enchantment)NamedCardFactory.Create("Utopia Sprawl", _alice);
        sprawl.AttachTo(forest);
        sprawl.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sprawl);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();
        activator.ActivateManaAbility(manaAbility, _alice);

        triggers.PendingCount.Should().Be(1, "the factory registered the trigger with the ambient manager");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Green.Should().Be(2,
            "the Forest's own {G} plus Utopia Sprawl's default-Green bonus");
    }

    // -----------------------------------------------------------------------
    // Mana-doubling trigger
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enchant a Forest, choose blue. Tapping the enchanted Forest for {G}
    /// puts {G} in the pool (the land's own ability) and the trigger adds the
    /// chosen {U} — net pool after resolving the trigger: {G}{U}.
    /// </summary>
    [Fact]
    public void EnchantedForestTappedForMana_AddsChosenColor()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var sprawl = UtopiaSprawlFactory.Create(_alice, ManaColor.Blue, triggers);
        sprawl.AttachTo(forest);
        sprawl.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sprawl);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        // Tap the enchanted Forest. The land's {G} hits the pool immediately;
        // the Utopia Sprawl trigger is a triggered mana ability not yet
        // resolved (CR 605.3 — the land's ability doesn't use the stack).
        activator.ActivateManaAbility(manaAbility, _alice);

        _alice.ManaPool.Green.Should().Be(1, "the Forest's own {G}");
        _alice.ManaPool.Blue.Should().Be(0, "trigger has not resolved yet");
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Blue.Should().Be(1, "Utopia Sprawl adds the chosen color");
        _alice.ManaPool.Green.Should().Be(1, "the Forest's {G} is untouched");
    }

    /// <summary>
    /// The bonus mana goes to the player who tapped the Forest (its
    /// controller), even if Utopia Sprawl belongs to someone else — oracle
    /// reads "its controller". Here Bob controls the enchanted Forest while
    /// Alice owns the Aura.
    /// </summary>
    [Fact]
    public void BonusManaGoesToForestController_NotAuraOwner()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var forest = (Land)NamedCardFactory.Create("Forest", _bob);
        forest.SetController(_bob);
        forest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(forest);

        var sprawl = UtopiaSprawlFactory.Create(_alice, ManaColor.Red, triggers);
        sprawl.AttachTo(forest);
        sprawl.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sprawl);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _bob);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.ManaPool.Red.Should().Be(1, "the Forest's controller gets the bonus");
        _alice.ManaPool.Red.Should().Be(0, "the Aura's owner does not");
    }

    /// <summary>
    /// Tapping a Forest that is NOT enchanted by this Utopia Sprawl does not
    /// trigger the bonus — the condition gates on the Aura's AttachedTo slot.
    /// </summary>
    [Fact]
    public void UnenchantedForestTapped_DoesNotTrigger()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var enchanted = (Land)NamedCardFactory.Create("Forest", _alice);
        enchanted.SetController(_alice);
        enchanted.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enchanted);

        var other = (Land)NamedCardFactory.Create("Forest", _alice);
        other.SetController(_alice);
        other.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(other);

        var sprawl = UtopiaSprawlFactory.Create(_alice, ManaColor.Blue, triggers);
        sprawl.AttachTo(enchanted);
        sprawl.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sprawl);

        var otherMana = other.Abilities.OfType<IManaAbility>().Single();
        activator.ActivateManaAbility(otherMana, _alice);

        triggers.PendingCount.Should().Be(0, "only the enchanted Forest matters");
        _alice.ManaPool.Blue.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Enchant Forest — cast-time target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OnlyOffersForests()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        mountain.SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);

        var sprawl = UtopiaSprawlFactory.Create(_alice);

        var def = UtopiaSprawlFactory.BuildSpellDefinition(
            sprawl, _alice.Zones.Battlefield.GetCards().OfType<Permanent>());

        var candidates = def.TargetRequests.Single().LegalCandidates;
        candidates.Should().ContainSingle("only Forests are legal targets")
            .Which.Should().BeSameAs(forest);
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
