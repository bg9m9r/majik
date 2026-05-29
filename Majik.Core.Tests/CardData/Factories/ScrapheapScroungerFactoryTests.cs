using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ScrapheapScroungerFactory"/>.
///
/// Scrapheap Scrounger (Kaladesh, {2}):
///   Artifact Creature — Construct 3/2.
///   "This creature can't block.
///    {1}{B}, Exile another creature card from your graveyard: Return this
///    card from your graveyard to the battlefield."
///
/// Covers:
///   - Identity (Artifact Creature — Construct 3/2 at {2}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Shape-only Create overload does NOT register the combat restriction;
///     the two-arg overload registers a non-expiring CannotBlock restriction
///     scoped to Scrounger (CR 509.1c).
///   - Activated ability shape: {1}{B} mana cost, no target requests
///     (CR 602).
///   - Resolution: exiles exactly one OTHER creature card from the
///     graveyard and returns Scrounger to the battlefield (CR 601.2g).
///   - Guards: with no other creature card available, or once Scrounger has
///     left the graveyard, the body is a no-op.
/// </summary>
public class ScrapheapScroungerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature FuelCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        return c;
    }

    private static void PutInGraveyard(Player owner, Card card)
    {
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    private static ActivatedAbility GraveyardAbility(Creature scrounger) =>
        scrounger.Abilities.OfType<ActivatedAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ScrapheapScrounger_Identity()
    {
        var c = ScrapheapScroungerFactory.Create(_alice);

        c.Name.Should().Be("Scrapheap Scrounger");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ScrapheapScrounger_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Scrapheap Scrounger", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Scrapheap Scrounger");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Can't-block static (CR 509.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void ScrapheapScrounger_ShapeOnly_DoesNotRegisterCombatRestriction()
    {
        var effects = new ContinuousEffectsService();
        var c = ScrapheapScroungerFactory.Create(_alice);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeFalse(
            "shape-only Create overload does not install the combat restriction");
    }

    [Fact]
    public void ScrapheapScrounger_WithEffectsService_RegistersCannotBlockRestriction()
    {
        var effects = new ContinuousEffectsService();
        var c = ScrapheapScroungerFactory.Create(_alice, effects);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "CR 509.1c — Scrapheap Scrounger's static 'can't block' rider is " +
            "registered as a non-expiring CombatRestrictionEffect");
    }

    [Fact]
    public void ScrapheapScrounger_Restriction_DoesNotExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var c = ScrapheapScroungerFactory.Create(_alice, effects);

        effects.ExpireEndOfTurn();
        effects.Prune();

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "the can't-block rider is a permanent static — it does NOT expire " +
            "at end of turn");
    }

    [Fact]
    public void ScrapheapScrounger_RestrictionIsScopedToScrounger()
    {
        var effects = new ContinuousEffectsService();
        var c = ScrapheapScroungerFactory.Create(_alice, effects);

        var bystander = new Creature("Bystander", "{1}", 1, 1);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);

        effects.HasRestriction(bystander, CombatRestriction.CannotBlock).Should().BeFalse(
            "the restriction targets Scrounger specifically");
        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape (CR 602)
    // -----------------------------------------------------------------------

    [Fact]
    public void ScrapheapScrounger_HasGraveyardRecursionAbility()
    {
        var c = ScrapheapScroungerFactory.Create(_alice);

        var ability = GraveyardAbility(c);
        ability.Source.Should().BeSameAs(c);
        ability.TargetRequests.Should().BeEmpty(
            "the recursion ability has no targets");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed {1}{B} mana cost is a cost-layer ManaCostCost");
    }

    // -----------------------------------------------------------------------
    // Resolution (CR 601.2g return-from-graveyard)
    // -----------------------------------------------------------------------

    [Fact]
    public void ScrapheapScrounger_Resolve_ExilesOtherCreature_ReturnsScroungerToBattlefield()
    {
        var c = ScrapheapScroungerFactory.Create(_alice);
        PutInGraveyard(_alice, c);
        var fuel = FuelCreature(_alice, "Fuel");

        GraveyardAbility(c).Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(c,
            "Scrounger returns from the graveyard to the battlefield");
        c.Zone.Should().Be(ZoneType.Battlefield);
        c.Controller.Should().BeSameAs(_alice);

        _alice.Zones.Exile.GetCards().Should().Contain(fuel,
            "the exile cost moves the other creature card to exile");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(fuel);
    }

    [Fact]
    public void ScrapheapScrounger_Resolve_NoOtherCreatureCard_IsNoOp()
    {
        var c = ScrapheapScroungerFactory.Create(_alice);
        PutInGraveyard(_alice, c);
        // Only a non-creature card present — cannot pay the "another creature
        // card" exile cost.
        var instant = new Instant("Bolt", "{R}");
        instant.SetOwner(_alice);
        PutInGraveyard(_alice, instant);

        GraveyardAbility(c).Resolve();

        _alice.Zones.Graveyard.GetCards().Should().Contain(c,
            "the exile cost can't be paid, so Scrounger does not return");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c);
        _alice.Zones.Exile.GetCards().Should().NotContain(instant,
            "no creature card was available to pay the cost");
    }

    [Fact]
    public void ScrapheapScrounger_Resolve_DoesNotExileItself()
    {
        var c = ScrapheapScroungerFactory.Create(_alice);
        PutInGraveyard(_alice, c);
        // Scrounger is the only creature card in the graveyard — "another"
        // excludes itself, so the cost can't be paid.

        GraveyardAbility(c).Resolve();

        _alice.Zones.Exile.GetCards().Should().NotContain(c,
            "the exile cost requires ANOTHER creature card, not Scrounger itself");
        _alice.Zones.Graveyard.GetCards().Should().Contain(c);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c);
    }

    [Fact]
    public void ScrapheapScrounger_Resolve_NotInGraveyard_IsNoOp()
    {
        var c = ScrapheapScroungerFactory.Create(_alice);
        // Scrounger is on the battlefield, not in the graveyard.
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        var fuel = FuelCreature(_alice, "Fuel");

        GraveyardAbility(c).Resolve();

        _alice.Zones.Exile.GetCards().Should().NotContain(fuel,
            "Scrounger isn't in the graveyard, so nothing happens (CR 608.2b)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(fuel);
    }
}
