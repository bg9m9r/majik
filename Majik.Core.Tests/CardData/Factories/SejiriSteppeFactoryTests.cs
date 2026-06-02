using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SejiriSteppeFactory"/> — Zendikar ({W} land).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, target creature you control gains protection from
///    the color of your choice until end of turn.
///    {T}: Add {W}."
///
/// Same enters-tapped + {T}: Add {W} land shell as the Akoum Refuge / Sejiri
/// Glacier cycle, with an ETB triggered ability whose protection-grant body
/// mirrors <see cref="SejiriShelterFactory"/> (the ZNR instant with the
/// identical "target creature you control gains protection from the color of
/// your choice until end of turn" effect).
///
/// Covers:
/// - Identity (name, Land type, non-basic, owner/controller) — from JSON.
/// - {T}: Add {W} mana ability (CR 605.1) — from JSON.
/// - One battlefield-active ETB triggered ability with a single 1..1 "target
///   creature you control" request.
/// - ETB resolution grants protection from the chosen colour EOT (default
///   white; injected pick honoured).
/// - Grant expires at end of turn (CR 514.2 / CR 613.6e).
/// - Illegal target / not-controlled / non-creature → clean no-op
///   (CR 608.2b/608.2c).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the Refuge cycle / Sejiri Glacier.
/// </summary>
[Trait("Color", "W")]
public class SejiriSteppeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeBear(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // =========================================================================
    // Identity + mana
    // =========================================================================

    [Fact]
    public void SejiriSteppe_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Steppe", _alice);

        land.Name.Should().Be("Sejiri Steppe");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Sejiri Steppe is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SejiriSteppe_HasSingleManaAbility_AddingWhite()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Steppe", _alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {W} ability");
        manaAbilities[0].ManaGenerated.White.Should().BeGreaterThan(0, "produces white mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
    }

    // =========================================================================
    // ETB trigger shape
    // =========================================================================

    [Fact]
    public void SejiriSteppe_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Steppe", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void SejiriSteppe_EtbTrigger_HasSingleTargetCreatureRequest()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Steppe", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1, "target creature you control");
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void SejiriSteppe_TargetGather_OnlyControllerCreatures()
    {
        var mine = MakeBear("My Bear", _alice);
        PutOnBattlefield(_alice, mine);
        var theirs = MakeBear("Their Bear", _bob);
        PutOnBattlefield(_bob, theirs);

        var land = (Land)NamedCardFactory.Create("Sejiri Steppe", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        var candidates = trigger.TargetRequests[0].CandidateGatherer!(null!);

        candidates.Should().Contain(mine);
        candidates.Should().NotContain(theirs, "printed 'creature you control' — opponent creatures aren't candidates");
    }

    // =========================================================================
    // Resolution — grant protection from chosen colour
    // =========================================================================

    [Fact]
    public void Resolve_GrantsProtection_DefaultColour_White()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_alice, bear);

        var granted = SejiriSteppeFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeSameAs(bear);
        var prot = bear.Abilities.OfType<ProtectionAbility>().Should().ContainSingle(
            "protection from the chosen colour is granted").Subject;
        prot.Quality.Should().Be("white", "default colour pick when none supplied");
    }

    [Fact]
    public void Resolve_HonoursInjectedColourPick()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_alice, bear);

        SejiriSteppeFactory.Resolve(
            _alice, bear, o => o!,
            colorPicker: (_, _) => SejiriSteppeFactory.QualityRed);

        var prot = bear.Abilities.OfType<ProtectionAbility>().Single();
        prot.Quality.Should().Be("red", "the injected colour pick is honoured");
    }

    [Fact]
    public void Resolve_Grant_ExpiresAtEndOfTurn()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        var effects = new ContinuousEffectsService();
        bear.ActiveEffects = effects;
        PutOnBattlefield(_alice, bear);

        SejiriSteppeFactory.Resolve(_alice, bear, o => o!);
        bear.Abilities.OfType<ProtectionAbility>().Should().ContainSingle("grant is live before cleanup");

        // CR 514.2 — cleanup step expires "until end of turn" grants.
        effects.ExpireEndOfTurn();

        bear.Abilities.OfType<ProtectionAbility>().Should().BeEmpty(
            "the until-end-of-turn grant is revoked at cleanup (CR 514.2 / CR 613.6e)");
    }

    // =========================================================================
    // Illegal targets (CR 608.2b/608.2c)
    // =========================================================================

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoGrant()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        bear.ActiveEffects = new ContinuousEffectsService();
        bear.SetZone(ZoneType.Hand); // not on battlefield

        var granted = SejiriSteppeFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeNull("illegal target at resolution → ability does nothing (CR 608.2c)");
        bear.Abilities.OfType<ProtectionAbility>().Should().BeEmpty("no grant on an illegal target");
    }

    [Fact]
    public void Resolve_TargetNotControlledByCaster_NoGrant()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        var granted = SejiriSteppeFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeNull("Sejiri Steppe only targets a creature YOU control");
        bear.Abilities.OfType<ProtectionAbility>().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_TargetNotACreature_NoGrant()
    {
        var granted = SejiriSteppeFactory.Resolve(
            _alice,
            rawTarget: "not-a-creature",
            resolver: _ => "not-a-creature");

        granted.Should().BeNull("non-creature target → ability does nothing");
    }
}
