using System.Linq;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CartoucheOfSolidarityFactory"/>.
///
/// Card: Cartouche of Solidarity — Enchantment — Aura Cartouche {W}
/// (Amonkhet).
///   "Enchant creature you control
///    When this Aura enters, create a 1/1 white Warrior creature token with
///    vigilance.
///    Enchanted creature gets +1/+1 and has first strike."
///
/// Covers:
///   - Identity / dispatch (Aura + Cartouche subtypes, {W}, white).
///   - +1/+1 boost + First Strike via AttachedBoostEffect (Layers 7c / 6).
///   - Boost inert while unattached.
///   - ETB trigger mints a 1/1 white Warrior token with Vigilance.
///   - Target predicate: only creatures the caster controls are legal.
/// </summary>
public class CartoucheOfSolidarityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CartoucheOfSolidarity_Identity()
    {
        var c = CartoucheOfSolidarityFactory.Create(_alice);

        c.Name.Should().Be("Cartouche of Solidarity");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cartouche).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CartoucheOfSolidarity()
    {
        var card = NamedCardFactory.Create("Cartouche of Solidarity", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Cartouche of Solidarity");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cartouche).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static boost — +1/+1 + first strike
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_PlusOnePlusOne_AppliesToAttachedCreature()
    {
        var effects = new ContinuousEffectsService();
        var cartouche = CartoucheOfSolidarityFactory.Create(_alice, effects);
        PlaceOnBattlefield(cartouche, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        cartouche.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(3, "2 + 1 = 3");
        chars.Toughness.Should().Be(3, "2 + 1 = 3");
    }

    [Fact]
    public void Static_GrantsFirstStrike()
    {
        var effects = new ContinuousEffectsService();
        var cartouche = CartoucheOfSolidarityFactory.Create(_alice, effects);
        PlaceOnBattlefield(cartouche, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        cartouche.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("First Strike");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var cartouche = CartoucheOfSolidarityFactory.Create(_alice, effects);
        PlaceOnBattlefield(cartouche, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);

        // Don't attach.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("First Strike");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — create a 1/1 white Warrior token with vigilance
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_HasExactlyOneTriggeredAbility()
    {
        var cartouche = CartoucheOfSolidarityFactory.Create(_alice);
        cartouche.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Cartouche of Solidarity has exactly one triggered ability (the ETB token trigger)");
    }

    [Fact]
    public void Etb_CreatesOneWhiteWarriorTokenWithVigilance()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var cartouche = CartoucheOfSolidarityFactory.Create(
            _alice, continuousEffects: null, triggers: triggers, zoneService: zones);

        // Move the Aura onto the battlefield via ZoneService so the ETB
        // CardMovedEvent fires (CR 603.6a / 603.6d).
        cartouche.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cartouche);
        zones.MoveCard(cartouche, ZoneType.Hand, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1,
            "the ETB trigger must queue when the Aura enters the battlefield");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1,
            "exactly one Warrior token is created when the Aura enters");

        var token = tokens.Single();
        token.Name.Should().Be("Warrior");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice,
            "the token is under the controller's control (CR 111.4)");
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Vigilance",
                "the Warrior token has Vigilance (CR 702.20)");
        token.TokenColorsOverride.Should().NotBeNull();
        token.TokenColorsOverride!.Should().Contain(ManaColor.White,
            "the Warrior token is white (CR 105 / CR 111.4)");
    }

    // -----------------------------------------------------------------------
    // Target predicate — "creature you control"
    // -----------------------------------------------------------------------

    [Fact]
    public void IsCreatureYouControl_AcceptsOwnCreature()
    {
        var bear = NewCreatureOnBattlefield("Bear", _alice);
        CartoucheOfSolidarityFactory.IsCreatureYouControl(bear, _alice).Should().BeTrue();
    }

    [Fact]
    public void IsCreatureYouControl_RejectsOpponentCreature()
    {
        var bear = NewCreatureOnBattlefield("Bear", _bob);
        CartoucheOfSolidarityFactory.IsCreatureYouControl(bear, _alice).Should().BeFalse(
            "the printed clause is 'creature you control'");
    }

    [Fact]
    public void IsCreatureYouControl_RejectsNonCreature()
    {
        var land = new Land("Plains");
        land.SetController(_alice);
        CartoucheOfSolidarityFactory.IsCreatureYouControl(land, _alice).Should().BeFalse();
    }

    [Fact]
    public void BuildSpellDefinition_FiltersOnlyControlledCreatures()
    {
        var cartouche = CartoucheOfSolidarityFactory.Create(_alice);

        var myBear = NewCreatureOnBattlefield("My Bear", _alice);
        var theirBear = NewCreatureOnBattlefield("Their Bear", _bob);
        var myLand = new Land("Plains");
        myLand.SetController(_alice);

        var battlefield = new Permanent[] { myBear, theirBear, myLand };
        var def = CartoucheOfSolidarityFactory.BuildSpellDefinition(cartouche, _alice, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(myBear);
        candidates.Should().NotContain(theirBear);
        candidates.Should().NotContain(myLand);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(string name, Player controller)
    {
        var bear = new Creature(name, "{1}{G}", 2, 2);
        bear.SetOwner(controller);
        bear.SetController(controller);
        controller.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }

    private static void PlaceOnBattlefield(Enchantment cartouche, Player owner)
    {
        cartouche.SetOwner(owner);
        cartouche.SetController(owner);
        owner.Zones.Battlefield.AddCard(cartouche);
        cartouche.SetZone(ZoneType.Battlefield);
    }
}
