using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for the modal double-faced card
/// Blackbloom Rogue // Blackbloom Bog (Zendikar Rising).
///
/// Oracle text (verified against Scryfall 2026-06):
///   Front — Blackbloom Rogue, Creature — Human Rogue, {2}{B}, 2/3:
///     "Menace"
///     "This creature gets +3/+0 as long as an opponent has eight or more cards
///      in their graveyard."
///   Back — Blackbloom Bog, Land:
///     "This land enters tapped."
///     "{T}: Add {B}."
///
/// MDFC cast-either-face wiring mirrors <see cref="AkoumWarriorFactory"/> (front
/// carries a castable <see cref="Majik.Core.CardData.MDFCs.MdfcFace.Land"/>
/// back-face descriptor). The conditional self-pump mirrors
/// <see cref="InventorsApprenticeFactory"/>, swapping the predicate
/// (opponent-graveyard count) and the bonus (+3/+0).
/// </summary>
[Trait("Color", "B")]
public class BlackbloomRogueFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void FillGraveyard(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Instant($"Filler {owner.Name} {i}", "{B}") { Owner = owner };
            owner.Zones.Graveyard.AddCard(card);
        }
    }

    // -----------------------------------------------------------------------
    // Front face — Blackbloom Rogue identity + dispatch + MDFC + Menace
    // -----------------------------------------------------------------------

    [Fact]
    public void BlackbloomRogue_Identity_CreatureHumanRogue_2_3_Black2B()
    {
        var rogue = BlackbloomRogueFactory.Create(_alice);

        rogue.Name.Should().Be("Blackbloom Rogue");
        rogue.HasType(CardType.Creature).Should().BeTrue();
        rogue.HasType(CardType.Land).Should().BeFalse();
        rogue.ManaCost.Should().Be("{2}{B}");
        rogue.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(rogue).Should().Contain(ManaColor.Black);
        rogue.BasePower.Should().Be(2);
        rogue.BaseToughness.Should().Be(3);
        rogue.Subtypes.Should().Contain(CardSubtype.Human);
        rogue.Subtypes.Should().Contain(CardSubtype.Rogue);
        rogue.Owner.Should().BeSameAs(_alice);
        rogue.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BlackbloomRogue_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Blackbloom Rogue", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Blackbloom Rogue");
    }

    [Fact]
    public void BlackbloomRogue_HasMenace_KeywordMarker()
    {
        var rogue = BlackbloomRogueFactory.Create(_alice);

        // CR 702.111 — Menace present as a KeywordAbility marker, read by the
        // combat-keyword lookup.
        rogue.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Menace");
        CombatAbilities.HasMenace(rogue).Should().BeTrue();
    }

    [Fact]
    public void BlackbloomRogue_HasMdfcState_WithCastableLandBackFace()
    {
        var rogue = BlackbloomRogueFactory.Create(_alice);

        // CR 712.3 — front-face card carries the castable back-face descriptor.
        rogue.MdfcState.Should().NotBeNull();
        rogue.MdfcState!.FrontFaceName.Should().Be("Blackbloom Rogue");
        rogue.MdfcState.BackFaceName.Should().Be("Blackbloom Bog");
        rogue.MdfcState.IsBackFace.Should().BeFalse("the creature is the front face");
        rogue.MdfcState.CastableBackFace.Should().NotBeNull();
        rogue.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        rogue.MdfcState.CastableBackFace.Name.Should().Be("Blackbloom Bog");
    }

    // -----------------------------------------------------------------------
    // Front face — graveyard-conditional self-pump (Layer 7c)
    // -----------------------------------------------------------------------

    private Creature NewRogueOnBattlefield(Func<IReadOnlyList<Player>> players)
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var c = BlackbloomRogueFactory.Create(_alice, players, effects, bus);
        zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);
        c.ActiveEffects = effects;
        return c;
    }

    [Fact]
    public void Pump_OpponentGraveyardBelowThreshold_StaysTwoThree()
    {
        var c = NewRogueOnBattlefield(() => new[] { _alice, _bob });
        FillGraveyard(_bob, 7); // below 8

        c.Power.Should().Be(2, "no opponent has eight or more cards in their graveyard");
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void Pump_OpponentGraveyardAtThreshold_ActivatesBonus_FiveThree()
    {
        var c = NewRogueOnBattlefield(() => new[] { _alice, _bob });
        FillGraveyard(_bob, 8); // exactly 8

        c.Power.Should().Be(5, "2 + 3 when an opponent has eight cards in their graveyard");
        c.Toughness.Should().Be(3, "the bonus is +3/+0 — toughness is unchanged");
    }

    [Fact]
    public void Pump_OpponentGraveyardAboveThreshold_ActivatesBonus()
    {
        var c = NewRogueOnBattlefield(() => new[] { _alice, _bob });
        FillGraveyard(_bob, 12);

        c.Power.Should().Be(5);
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void Pump_OnlyControllersOwnGraveyardFull_DoesNotActivate()
    {
        // CR 102.1 — the controller's own graveyard never satisfies "an
        // opponent has...". Alice (controller) has a full graveyard; Bob does
        // not.
        var c = NewRogueOnBattlefield(() => new[] { _alice, _bob });
        FillGraveyard(_alice, 10);

        c.Power.Should().Be(2, "the controller's own graveyard does not count as an opponent's");
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void Pump_DynamicallyReevaluates_AsGraveyardFillsAndEmpties()
    {
        var c = NewRogueOnBattlefield(() => new[] { _alice, _bob });

        // Bob's graveyard starts empty.
        c.Power.Should().Be(2);

        // Bob's graveyard crosses the threshold → bonus flips on. The cards are
        // added via raw zone ops, so invalidate the layer-system cache
        // explicitly via Clear() — production's CardMovedEvent does this.
        FillGraveyard(_bob, 8);
        c.ActiveEffects!.Clear();
        c.Power.Should().Be(5);

        // Bob's graveyard drops back below the threshold → bonus flips off.
        var last = _bob.Zones.Graveyard.GetCards().First();
        _bob.Zones.Graveyard.RemoveCard(last);
        c.ActiveEffects!.Clear();
        c.Power.Should().Be(2, "an opponent dropping below eight cards turns the bonus off");
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void AnyOpponentHasFullGraveyard_HelperPredicate()
    {
        var all = new[] { _alice, _bob };

        BlackbloomRogueFactory.AnyOpponentHasFullGraveyard(_alice, all)
            .Should().BeFalse("no graveyard is full yet");

        FillGraveyard(_alice, 9);
        BlackbloomRogueFactory.AnyOpponentHasFullGraveyard(_alice, all)
            .Should().BeFalse("Alice's own full graveyard is not an opponent's");

        FillGraveyard(_bob, 8);
        BlackbloomRogueFactory.AnyOpponentHasFullGraveyard(_alice, all)
            .Should().BeTrue("Bob (the opponent) now has eight cards in their graveyard");

        // Null player list → no opponent to read.
        BlackbloomRogueFactory.AnyOpponentHasFullGraveyard(_alice, null)
            .Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Back face — Blackbloom Bog identity + mana ability + enters tapped
    // -----------------------------------------------------------------------

    [Fact]
    public void BlackbloomBog_Identity_Land_TapsForBlack_BackFace()
    {
        var bog = BlackbloomBogFactory.Create(_alice);

        bog.Name.Should().Be("Blackbloom Bog");
        bog.HasType(CardType.Land).Should().BeTrue();
        bog.HasSupertype(CardSupertype.Basic).Should().BeFalse("Blackbloom Bog is non-basic");
        bog.Owner.Should().BeSameAs(_alice);
        bog.Controller.Should().BeSameAs(_alice);

        // Pre-flipped to the back face — the land is the back face that exists.
        bog.MdfcState.Should().NotBeNull();
        bog.MdfcState!.IsBackFace.Should().BeTrue();
        bog.MdfcState.ActiveFaceName.Should().Be("Blackbloom Bog");

        // {T}: Add {B} — single mana ability producing one black.
        var manaAbilities = bog.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().ContainSingle();
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0, "produces black mana");
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void BlackbloomBog_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Blackbloom Bog", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Blackbloom Bog");
    }

    [Fact]
    public void BlackbloomBog_EntersTapped_ViaReplacementBus()
    {
        var bus = new ReplacementBus();
        var bog = BlackbloomBogFactory.Create(_alice, bus);

        // CR 614.1c — unconditional "this land enters tapped" replacement is
        // registered on the bus. Drive the ETB intent through it and confirm
        // EntersTapped is set.
        var intent = new ZoneMoveIntent(
            Card: bog,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var replaced = bus.Apply(intent);
        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue(
            "Blackbloom Bog always enters tapped (CR 614.1c)");
    }
}
