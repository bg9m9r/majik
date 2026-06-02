using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CastleVantressFactory"/>.
///
/// Oracle (Scryfall-confirmed, Throne of Eldraine):
///   "This land enters tapped unless you control an Island.
///    {T}: Add {U}.
///    {2}{U}{U}, {T}: Scry 2."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Castle
/// Vantress is NOT itself an Island.
///
/// Same Eldraine Castle cycle as <see cref="CastleArdenvaleFactory"/>; the
/// differences are the gating subtype (Island vs Plains), the produced
/// colour ({U} vs {W}), and the second activated ability (Scry 2 vs token
/// creation). Identity + both abilities are loaded from
/// <c>castle-vantress.json</c> via <see cref="CardDefinitionFactory"/>; the
/// ETB-tapped replacement is wired in code (the JSON schema models no
/// conditional ETB-tapped).
///
/// Covers:
/// - Identity: Land type, name, non-basic, non-legendary, not an Island.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Castle Vantress".
/// - Two abilities: one <see cref="ManaAbility"/> ({T}: Add {U}) + one
///   <see cref="ActivatedAbility"/> ({2}{U}{U},{T}: Scry 2).
/// - ETB predicate (via <see cref="ReplacementBus"/>):
///     · No Island controlled → enters tapped.
///     · Controller has an Island → enters untapped.
///     · Opponent controls an Island, not the controller → enters tapped.
///     · Castle Vantress itself is NOT an Island (cannot satisfy its own predicate).
/// - Mana ability: {T} produces {U}; CanActivate false when tapped.
/// - Activated ability cost: requires {2}{U}{U} + tap.
/// - Activated ability resolve: Scry 2 reorders the top of the library.
/// </summary>
[Trait("Color", "C")]
public class CastleVantressFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: place Castle Vantress on Alice's battlefield (untapped).
    // -----------------------------------------------------------------------
    private Land PlaceOnBattlefield()
    {
        var castle = CastleVantressFactory.Create(_alice);
        castle.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castle);
        return castle;
    }

    // -----------------------------------------------------------------------
    // Helper: add a basic Island to a player's battlefield.
    // -----------------------------------------------------------------------
    private static Land AddIsland(Player controller)
    {
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = controller, Controller = controller };
        island.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(island);
        return island;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedCastleVantress()
    {
        var castle = CastleVantressFactory.Create(_alice);
        castle.Name.Should().Be("Castle Vantress");
        castle.HasType(CardType.Land).Should().BeTrue();
        castle.Owner.Should().BeSameAs(_alice);
        castle.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary_NotIsland()
    {
        var castle = CastleVantressFactory.Create(_alice);
        castle.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Castle Vantress is nonbasic");
        castle.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Castle Vantress is not legendary");
        castle.HasSubtype(CardSubtype.Island).Should().BeFalse(
            "Castle Vantress has no Island subtype and cannot satisfy its own ETB predicate");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneActivated()
    {
        var castle = CastleVantressFactory.Create(_alice);
        castle.Abilities.Should().HaveCount(2,
            "one {T}: Add {U} mana ability + one {2}{U}{U},{T} activated ability");
        castle.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        castle.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_Activate_ProducesOneBlue()
    {
        var castle = PlaceOnBattlefield();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Blue.Should().Be(1, "the mana ability produces {U}");
        produced.Generic.Should().Be(0);
        castle.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ManaAbility_CanActivate_FalseWhenTapped()
    {
        var castle = PlaceOnBattlefield();
        castle.Tap();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoIsland()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var castle = CastleVantressFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Vantress enters tapped when controller has no Island");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasAnIsland()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        AddIsland(alice);

        var castle = CastleVantressFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Castle Vantress enters untapped when controller has an Island");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasIsland()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        AddIsland(bob);

        var castle = CastleVantressFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the 'you control' predicate checks the controller's battlefield, not the opponent's");
    }

    [Fact]
    public void PredicateExcludesSelf_CastleIsNotAnIsland()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var castle = CastleVantressFactory.Create(alice, replacements: bus);
        alice.Zones.Battlefield.AddCard(castle);
        castle.SetZone(ZoneType.Battlefield);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Vantress has no Island subtype; its presence on battlefield doesn't satisfy the predicate");
    }
    // -----------------------------------------------------------------------
    // Activated ability: {2}{U}{U}, {T} — cost gates
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasExpectedCosts()
    {
        var castle = CastleVantressFactory.Create(_alice);
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2,
            "costs are {2}{U}{U} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost must be the {2}{U}{U} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost must be the {T} tap cost");
    }

    // -----------------------------------------------------------------------
    // Activated ability: Scry 2 (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_ScryTwo_PutsTopTwoOnBottom_WithNoAgent()
    {
        // No agent registered → the default Scry decision sends all peeked
        // cards to the bottom (same fallback as CardDefinitionFactory's
        // scry_self path). Verify the top two cards end up on the bottom.
        var alice = new Player("Alice", 20);
        var top1 = new Land("L1", subtypes: new[] { CardSubtype.Island }) { Owner = alice };
        var top2 = new Land("L2", subtypes: new[] { CardSubtype.Island }) { Owner = alice };
        var rest = new Land("L3", subtypes: new[] { CardSubtype.Island }) { Owner = alice };
        alice.Zones.Library.AddCard(top1);
        alice.Zones.Library.AddCard(top2);
        alice.Zones.Library.AddCard(rest);

        var castle = CastleVantressFactory.Create(alice);
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        // Execute the resolve body directly (cost gates verified above).
        ability.Effects.Single().Execute();

        // After scrying 2 to the bottom, L3 should be the new top and the
        // two scryed cards live at the bottom.
        var library = alice.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(3, "scry does not change library size");
        library[0].Should().BeSameAs(rest, "the unscryed card is now on top");
        library.Should().Contain(top1).And.Contain(top2);
    }
}
