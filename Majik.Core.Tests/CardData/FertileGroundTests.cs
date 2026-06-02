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
/// Tests for Fertile Ground — Enchantment — Aura {1}{G}.
///
///   "Enchant land
///    Whenever enchanted land is tapped for mana, its controller adds an
///    additional one mana of any color."
///
/// Structurally identical to Utopia Sprawl's mana-doubling trigger
/// (CR 605.1b — a triggered mana ability firing off
/// <see cref="ManaAbilityActivatedEvent"/>), with two differences:
///   - "Enchant land" accepts ANY land, not just a Forest (CR 702.5b).
///   - "one mana of any color" — the colour is chosen on resolution, not
///     fixed "as this Aura enters". Same v1 deferral as Lotus Cobra /
///     Crumbling Vestige: a <c>colorPicker</c> callback, defaulting to Green
///     (CR 106.1b — "any color" means a WUBRG colour).
///
/// Covers:
///   - Card identity (name, type, Aura subtype, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - The bonus trigger fires when the enchanted land is tapped, adding one
///     mana of the chosen colour to the land's controller's pool.
///   - The bonus goes to the land's controller, not the Aura's owner.
///   - Tapping a DIFFERENT land (not the enchanted one) does not trigger.
///   - "Enchant land" cast-time predicate offers any land.
/// </summary>
public class FertileGroundTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FertileGround_Identity_EnchantmentAuraAt1G()
    {
        var ground = FertileGroundFactory.Create(_alice);

        ground.Name.Should().Be("Fertile Ground");
        ground.HasType(CardType.Enchantment).Should().BeTrue();
        ground.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        ground.IsAura.Should().BeTrue();
        ground.ManaCost.Should().Be("{1}{G}");
        ground.Owner.Should().BeSameAs(_alice);
        ground.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FertileGround_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Fertile Ground", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Fertile Ground");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void FertileGround_WiredUp_HasOneManaTrigger()
    {
        var ground = FertileGroundFactory.Create(_alice, triggers: null, colorPicker: null);

        ground.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana-bonus trigger
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enchant a Forest, picking blue. Tapping the enchanted land for {G} puts
    /// {G} in the pool (the land's own ability) and the trigger adds the chosen
    /// {U} — net pool after resolving the trigger: {G}{U}.
    /// </summary>
    [Fact]
    public void EnchantedLandTappedForMana_AddsChosenColor()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var ground = FertileGroundFactory.Create(_alice, triggers, colorPicker: () => ManaColor.Blue);
        ground.AttachTo(forest);
        ground.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ground);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _alice);

        _alice.ManaPool.Green.Should().Be(1, "the Forest's own {G}");
        _alice.ManaPool.Blue.Should().Be(0, "trigger has not resolved yet");
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Blue.Should().Be(1, "Fertile Ground adds the chosen color");
        _alice.ManaPool.Green.Should().Be(1, "the Forest's {G} is untouched");
    }

    /// <summary>
    /// "Enchant land" — the bonus also fires off a non-Forest land. Enchant an
    /// Island; tapping it for {U} triggers the bonus, default colour Green.
    /// </summary>
    [Fact]
    public void EnchantedNonForestLandTapped_Triggers()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var island = (Land)NamedCardFactory.Create("Island", _alice);
        island.SetController(_alice);
        island.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);

        var ground = FertileGroundFactory.Create(_alice, triggers, colorPicker: null);
        ground.AttachTo(island);
        ground.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ground);

        var manaAbility = island.Abilities.OfType<IManaAbility>().Single();
        activator.ActivateManaAbility(manaAbility, _alice);

        triggers.PendingCount.Should().Be(1, "any land, not just Forests");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Green.Should().Be(1, "default 'any color' is Green");
        _alice.ManaPool.Blue.Should().Be(1, "the Island's own {U} is untouched");
    }

    /// <summary>
    /// The bonus mana goes to the player who tapped the land (its controller),
    /// even if Fertile Ground belongs to someone else — oracle reads "its
    /// controller". Here Bob controls the enchanted land while Alice owns the
    /// Aura.
    /// </summary>
    [Fact]
    public void BonusManaGoesToLandController_NotAuraOwner()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var forest = (Land)NamedCardFactory.Create("Forest", _bob);
        forest.SetController(_bob);
        forest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(forest);

        var ground = FertileGroundFactory.Create(_alice, triggers, colorPicker: () => ManaColor.Red);
        ground.AttachTo(forest);
        ground.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ground);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _bob);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.ManaPool.Red.Should().Be(1, "the land's controller gets the bonus");
        _alice.ManaPool.Red.Should().Be(0, "the Aura's owner does not");
    }

    /// <summary>
    /// Tapping a land that is NOT enchanted by this Fertile Ground does not
    /// trigger the bonus — the condition gates on the Aura's AttachedTo slot.
    /// </summary>
    [Fact]
    public void UnenchantedLandTapped_DoesNotTrigger()
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

        var ground = FertileGroundFactory.Create(_alice, triggers, colorPicker: () => ManaColor.Blue);
        ground.AttachTo(enchanted);
        ground.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ground);

        var otherMana = other.Abilities.OfType<IManaAbility>().Single();
        activator.ActivateManaAbility(otherMana, _alice);

        triggers.PendingCount.Should().Be(0, "only the enchanted land matters");
        _alice.ManaPool.Blue.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Enchant land — cast-time target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OffersAnyLand()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        mountain.SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);

        var ground = FertileGroundFactory.Create(_alice);

        var def = FertileGroundFactory.BuildSpellDefinition(
            ground, _alice.Zones.Battlefield.GetCards().OfType<Permanent>());

        var candidates = def.TargetRequests.Single().LegalCandidates;
        candidates.Should().HaveCount(2, "any land is a legal target");
        candidates.Should().Contain(forest).And.Contain(mountain);
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
