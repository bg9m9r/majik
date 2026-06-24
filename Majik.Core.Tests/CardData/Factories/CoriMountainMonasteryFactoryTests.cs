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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CoriMountainMonasteryFactory"/> — Cori Mountain
/// Monastery (Tarkir: Dragonstorm), a conditional-tapland impulse land.
/// Oracle text:
///   "This land enters tapped unless you control a Plains or an Island.
///    {T}: Add {R}.
///    {3}{R}, {T}: Exile the top card of your library. Until the end of your
///    next turn, you may play that card."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (Land, nonbasic, no printed subtype).
/// - The {T}: Add {R} mana ability (CR 605.1).
/// - The {3}{R}, {T} impulse activated ability — cost shape ({3}{R} mana +
///   {T} tap) and resolution (exile library-top, grant "you may play it until
///   end of your next turn" covering both the cast and land-play halves).
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c): no Plains/Island -> tapped; a Plains -> untapped; an Island
///   -> untapped; only opponent's Plains doesn't count; single-arg path
///   registers no replacement.
/// </summary>
[Trait("Color", "R")]
public class CoriMountainMonasteryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------
    [Fact]
    public void CoriMountainMonastery_Identity_IsNonbasicLandNoSubtype()
    {
        var land = CoriMountainMonasteryFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Cori Mountain Monastery is a nonbasic land");
        land.Subtypes.Should().BeEmpty("no printed land subtype");
    }

    [Fact]
    public void CoriMountainMonastery_HasManaAbility_ProducingR()
    {
        var land = (Land)NamedCardFactory.Create("Cori Mountain Monastery", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(1, "{T}: Add {R}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    // -----------------------------------------------------------------------
    // {3}{R}, {T} impulse activated ability
    // -----------------------------------------------------------------------
    [Fact]
    public void CoriMountainMonastery_ImpulseAbility_HasManaPlusTapCost()
    {
        var land = CoriMountainMonasteryFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the only non-mana activated ability is the {3}{R}, {T} impulse");
        var impulse = land.Abilities.OfType<ActivatedAbility>().Single();

        impulse.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the impulse ability's mana cost is {3}{R}");
        // The {T} tap-self cost is the second cost component (CR 602.5/605.3a).
        impulse.Costs.Should().HaveCount(2, "{3}{R} mana cost + {T} tap-self cost");
    }

    [Fact]
    public void CoriMountainMonastery_ImpulseResolve_ExilesTop_AndGrantsPlay()
    {
        var land = CoriMountainMonasteryFactory.Create(_alice);
        land.SetController(_alice);

        // Seed a library: top card is the one that gets impulsed.
        var top = NamedCardFactory.Create("Lightning Bolt", _alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        ResolveImpulse(land);

        // "Exile the top card of your library" (CR 701.20).
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        _alice.Zones.Exile.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Exile);

        // "Until the end of your next turn, you may play that card" — the
        // runtime cast grant nominates Alice (CR 118.9).
        ((Card)top).RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the impulsed spell is castable from exile by the activator");
    }

    [Fact]
    public void CoriMountainMonastery_ImpulseResolve_GrantsLandPlay_ForImpulsedLand()
    {
        var land = CoriMountainMonasteryFactory.Create(_alice);
        land.SetController(_alice);

        // Top card is a land — "you may PLAY that card" must authorise a land
        // play from exile (CR 305.2 / 601.1), not just a cast.
        var topLand = NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Library.AddCard(topLand);
        topLand.SetZone(ZoneType.Library);

        ResolveImpulse(land);

        _alice.Zones.Exile.GetCards().Should().Contain(topLand);
        ((Card)topLand).RuntimeExileLandPlayAllowedPlayer.Should().BeSameAs(_alice,
            "an impulsed land becomes playable from exile by the activator");
    }

    [Fact]
    public void CoriMountainMonastery_ImpulseResolve_EmptyLibrary_IsNoOp()
    {
        var land = CoriMountainMonasteryFactory.Create(_alice);
        land.SetController(_alice);

        var act = () => ResolveImpulse(land);

        act.Should().NotThrow("an empty library is a clean no-op for the exile move");
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "unless you control a Plains or an
    // Island"
    // -----------------------------------------------------------------------
    [Fact]
    public void CoriMountainMonastery_EntersTapped_WhenNoPlainsOrIsland()
    {
        var bus = new ReplacementBus();
        var land = CoriMountainMonasteryFactory.Create(_alice, replacements: bus, eventBus: null);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Plains or Island controlled -> enters tapped");
    }

    [Fact]
    public void CoriMountainMonastery_EntersUntapped_WhenControlPlains()
    {
        var bus = new ReplacementBus();
        SeedBattlefield("Plains", _alice);
        var land = CoriMountainMonasteryFactory.Create(_alice, replacements: bus, eventBus: null);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "controlling a Plains -> enters untapped");
    }

    [Fact]
    public void CoriMountainMonastery_EntersUntapped_WhenControlIsland()
    {
        var bus = new ReplacementBus();
        SeedBattlefield("Island", _alice);
        var land = CoriMountainMonasteryFactory.Create(_alice, replacements: bus, eventBus: null);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "controlling an Island -> enters untapped");
    }

    [Fact]
    public void CoriMountainMonastery_EntersTapped_WhenOnlyOpponentControlsPlains()
    {
        // "you control" — an opponent's Plains doesn't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedBattlefield("Plains", bob);

        var land = CoriMountainMonasteryFactory.Create(_alice, replacements: bus, eventBus: null);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's own Plains/Island counts");
    }

    [Fact]
    public void CoriMountainMonastery_SingleArgPath_RegistersNoReplacement()
    {
        // The shape-only single-arg path attaches abilities but no ETB-tapped
        // replacement (parity with every other ETB-replacement factory).
        var bus = new ReplacementBus();
        var land = CoriMountainMonasteryFactory.Create(_alice);
        _alice.Zones.Battlefield.GetCards(); // no-op; ensure no exceptions

        // Without a registered replacement the bus leaves the intent untouched.
        var after = ApplyEtb(bus, land, _alice);
        after.EntersTapped.Should().BeFalse(
            "no replacement registered on the single-arg path");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------
    [Fact]
    public void CoriMountainMonastery_Create_ThrowsOnNullOwner()
    {
        var act = () => CoriMountainMonasteryFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ResolveImpulse(Land land)
    {
        var impulse = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in impulse.Effects)
        {
            effect.Execute();
        }
    }

    private static void SeedBattlefield(string name, Player owner)
    {
        var card = NamedCardFactory.Create(name, owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static ZoneMoveIntent ApplyEtb(ReplacementBus bus, Land land, Player controller)
    {
        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        return after!;
    }
}
