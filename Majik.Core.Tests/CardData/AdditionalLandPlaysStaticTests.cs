using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 305.2 / 720 — "you may play N additional lands on each of your turns"
/// battlefield static. Covers Azusa, Lost but Seeking (+2) and Dryad of the
/// Ilysian Grove (+1) plus the LandDropTracker integration: cap raised while
/// the source is on the battlefield, lifts on leave, stacks additively, and
/// resets correctly per turn.
/// </summary>
public class AdditionalLandPlaysStaticTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player player, Permanent card)
    {
        card.SetZone(ZoneType.Battlefield);
        player.Zones.Battlefield.AddCard(card);
    }

    // ---------------------------------------------------------------------
    // Azusa, Lost but Seeking — Legendary Creature — Human Monk, +2 lands.
    // ---------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_Azusa_And_Dryad()
    {
        var azusa = NamedCardFactory.Create("Azusa, Lost but Seeking", _alice);
        azusa.Should().BeOfType<Creature>();
        ((Permanent)azusa).AdditionalLandPlaysGranted.Should().Be(2);

        var dryad = NamedCardFactory.Create("Dryad of the Ilysian Grove", _alice);
        dryad.Should().BeOfType<Creature>();
        ((Permanent)dryad).AdditionalLandPlaysGranted.Should().Be(1);
    }

    [Fact]
    public void Azusa_ShapeAndGrant()
    {
        var azusa = AzusaLostButSeekingFactory.Create(_alice);

        azusa.Name.Should().Be("Azusa, Lost but Seeking");
        azusa.HasType(CardType.Creature).Should().BeTrue();
        azusa.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        azusa.HasSubtype(CardSubtype.Human).Should().BeTrue();
        azusa.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        azusa.Power.Should().Be(1);
        azusa.Toughness.Should().Be(2);
        azusa.AdditionalLandPlaysGranted.Should().Be(2);
    }

    [Fact]
    public void Azusa_OnBattlefield_AllowsThreeLands_FourthRejected()
    {
        var tracker = new LandDropTracker();
        var azusa = AzusaLostButSeekingFactory.Create(_alice);
        PutOnBattlefield(_alice, azusa);

        // base 1 + Azusa 2 = 3 land plays this turn.
        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(3);

        for (var i = 0; i < 3; i++)
        {
            tracker.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _)
                .Should().BeTrue($"land {i + 1} of 3");
            tracker.RecordLandPlayed(_alice);
        }

        tracker.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out var reason)
            .Should().BeFalse("the 4th land exceeds the cap");
        reason.Should().Contain("already played");
    }

    [Fact]
    public void Azusa_LeavesBattlefield_CapBackToOne()
    {
        var tracker = new LandDropTracker();
        var azusa = AzusaLostButSeekingFactory.Create(_alice);
        PutOnBattlefield(_alice, azusa);

        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(3);

        // Azusa dies / bounces — grant lifts live.
        azusa.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(azusa);

        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(1);
    }

    [Fact]
    public void TwoAzusas_StackToFourAdditional()
    {
        var tracker = new LandDropTracker();
        PutOnBattlefield(_alice, AzusaLostButSeekingFactory.Create(_alice));
        PutOnBattlefield(_alice, AzusaLostButSeekingFactory.Create(_alice));

        // base 1 + 2 + 2 = 5.
        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(5);
    }

    [Fact]
    public void Azusa_CapResetsPerTurn_WhileStillOnBattlefield()
    {
        var tracker = new LandDropTracker();
        PutOnBattlefield(_alice, AzusaLostButSeekingFactory.Create(_alice));

        // Spend all three this turn.
        for (var i = 0; i < 3; i++) tracker.RecordLandPlayed(_alice);
        tracker.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _)
            .Should().BeFalse();

        // New turn — drops reset, Azusa still grants +2.
        tracker.ResetTurn();

        tracker.DropsUsedThisTurn(_alice).Should().Be(0);
        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(3);
    }

    [Fact]
    public void Azusa_DoesNotBenefitOpponent()
    {
        var tracker = new LandDropTracker();
        PutOnBattlefield(_alice, AzusaLostButSeekingFactory.Create(_alice));

        tracker.EffectiveMaxLandDropsThisTurn(_bob).Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // Dryad of the Ilysian Grove — Enchantment Creature — Nymph Dryad, +1.
    // ---------------------------------------------------------------------

    [Fact]
    public void Dryad_ShapeAndGrant()
    {
        var dryad = DryadOfTheIlysianGroveFactory.Create(_alice);

        dryad.Name.Should().Be("Dryad of the Ilysian Grove");
        dryad.HasType(CardType.Creature).Should().BeTrue();
        dryad.HasType(CardType.Enchantment).Should().BeTrue("it is an Enchantment Creature");
        dryad.HasSubtype(CardSubtype.Nymph).Should().BeTrue();
        dryad.HasSubtype(CardSubtype.Dryad).Should().BeTrue();
        dryad.Power.Should().Be(2);
        dryad.Toughness.Should().Be(4);
        dryad.AdditionalLandPlaysGranted.Should().Be(1);
    }

    [Fact]
    public void Dryad_OnBattlefield_AllowsTwoLands_ThirdRejected()
    {
        var tracker = new LandDropTracker();
        PutOnBattlefield(_alice, DryadOfTheIlysianGroveFactory.Create(_alice));

        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(2);

        tracker.RecordLandPlayed(_alice);
        tracker.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _)
            .Should().BeTrue("second land allowed");
        tracker.RecordLandPlayed(_alice);
        tracker.CanPlayLand(_alice, _alice, StepStateType.PreCombatMain, true, out _)
            .Should().BeFalse("third land exceeds the cap");
    }

    [Fact]
    public void Dryad_LayerFourBasicLandTypeGrant_WiresWithEffectsService()
    {
        // The effects-aware overload attaches the five Layer-4 basic-land-type
        // grants. Verify a basic Island the controller controls picks up the
        // other four basic types while Dryad is on the battlefield. Uses
        // ZoneService.MoveCard (same as the Leyline tests) so the
        // GrantLandSubtypeStaticEffect lifecycle fires on the CardMovedEvent.
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var zones = new ZoneService(bus);

        var island = (Land)NamedCardFactory.Create("Island", _alice);
        zones.MoveCard(island, ZoneType.Library, ZoneType.Battlefield, _alice);

        var dryad = DryadOfTheIlysianGroveFactory.Create(_alice, effects, bus);
        zones.MoveCard(dryad, ZoneType.Library, ZoneType.Battlefield, _alice);

        var subtypes = effects.Compute((Permanent)island).Subtypes;
        subtypes.Should().Contain(CardSubtype.Plains);
        subtypes.Should().Contain(CardSubtype.Island);
        subtypes.Should().Contain(CardSubtype.Swamp);
        subtypes.Should().Contain(CardSubtype.Mountain);
        subtypes.Should().Contain(CardSubtype.Forest);
    }
}
