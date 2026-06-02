using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Overgrowth — Enchantment — Aura {2}{G}.
///
///   "Enchant land
///    Whenever enchanted land is tapped for mana, its controller adds an
///    additional {G}{G}."
///
/// Covers:
///   - Card identity (name, type, Aura subtype, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - The mana trigger fires when the enchanted land is tapped, adding {G}{G}
///     to the land's controller's pool.
///   - The bonus goes to the land's controller, not the Aura's owner.
///   - Tapping a DIFFERENT land (not the enchanted one) does not trigger.
///   - "Enchant land" cast-time predicate only accepts lands.
/// </summary>
public class OvergrowthTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Overgrowth_Identity_EnchantmentAuraAt2G()
    {
        var aura = OvergrowthFactory.Create(_alice);

        aura.Name.Should().Be("Overgrowth");
        aura.HasType(CardType.Enchantment).Should().BeTrue();
        aura.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        aura.IsAura.Should().BeTrue();
        aura.ManaCost.Should().Be("{2}{G}");
        aura.Owner.Should().BeSameAs(_alice);
        aura.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Overgrowth_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Overgrowth", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Overgrowth");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void Overgrowth_Wired_HasOneManaTrigger()
    {
        var aura = OvergrowthFactory.Create(_alice, triggers: null);

        aura.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana trigger
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enchant a Forest. Tapping the enchanted land for {G} puts {G} in the
    /// pool (the land's own ability) and the trigger adds {G}{G} — net pool
    /// after resolving the trigger: three green mana.
    /// </summary>
    [Fact]
    public void EnchantedLandTappedForMana_AddsTwoGreen()
    {
        var (bus, stack, triggers, activator) = BuildEngine();

        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var aura = OvergrowthFactory.Create(_alice, triggers);
        aura.AttachTo(forest);
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        // Tap the enchanted land. The land's {G} hits the pool immediately;
        // the Overgrowth trigger is a triggered mana ability not yet resolved
        // (CR 605.3 — the land's ability doesn't use the stack).
        activator.ActivateManaAbility(manaAbility, _alice);

        _alice.ManaPool.Green.Should().Be(1, "the Forest's own {G}");
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Green.Should().Be(3, "the Forest's {G} plus Overgrowth's {G}{G}");
    }

    /// <summary>
    /// The bonus mana goes to the player who tapped the land (its controller),
    /// even if Overgrowth belongs to someone else — oracle reads "its
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

        var aura = OvergrowthFactory.Create(_alice, triggers);
        aura.AttachTo(forest);
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);

        var manaAbility = forest.Abilities.OfType<IManaAbility>().Single();

        activator.ActivateManaAbility(manaAbility, _bob);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.ManaPool.Green.Should().Be(3, "the land's {G} plus Overgrowth's {G}{G} go to the land's controller");
        _alice.ManaPool.Green.Should().Be(0, "the Aura's owner does not get the bonus");
    }

    /// <summary>
    /// Tapping a land that is NOT enchanted by this Overgrowth does not
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

        var aura = OvergrowthFactory.Create(_alice, triggers);
        aura.AttachTo(enchanted);
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);

        var otherMana = other.Abilities.OfType<IManaAbility>().Single();
        activator.ActivateManaAbility(otherMana, _alice);

        triggers.PendingCount.Should().Be(0, "only the enchanted land matters");
        _alice.ManaPool.Green.Should().Be(1, "just the unenchanted Forest's own {G}");
    }

    // -----------------------------------------------------------------------
    // Enchant land — cast-time target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OnlyOffersLands()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var aura = OvergrowthFactory.Create(_alice);

        var def = OvergrowthFactory.BuildSpellDefinition(
            aura, _alice.Zones.Battlefield.GetCards().OfType<Permanent>());

        var candidates = def.TargetRequests.Single().LegalCandidates;
        candidates.Should().ContainSingle("only lands are legal targets")
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
