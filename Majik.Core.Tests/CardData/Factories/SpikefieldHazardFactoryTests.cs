using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SpikefieldHazardFactory"/> and
/// <see cref="SpikefieldCaveFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card Spikefield Hazard // Spikefield
/// Cave.
///
/// Front face (Spikefield Hazard, {R}):
///   Instant. "Spikefield Hazard deals 1 damage to any target. If a
///   permanent dealt damage this way would die this turn, exile it instead."
///
/// Back face (Spikefield Cave):
///   Land. "This land enters tapped." "{T}: Add {R}."
///
/// Covers:
/// - Identity for both faces.
/// - <see cref="NamedCardFactory"/> dispatches both printed names to their
///   respective faces.
/// - MDFC face-tracker attachment (front-face card carries front-name +
///   back-name; back-face card carries the same pair pre-flipped).
/// - Front face — resolve: 1 damage to a creature target; exile-instead
///   rider rewrites the lethal battlefield→graveyard move to exile this
///   turn (CR 700.3 / CR 514.2); player target takes damage with no rider.
/// - Back face — enters-tapped replacement; {T}: Add {R} mana ability.
/// </summary>
[Trait("Color", "R")]
public class SpikefieldHazardFactoryTests
{
    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SpikefieldHazard_Identity()
    {
        var alice = new Player("Alice", 20);
        var card = SpikefieldHazardFactory.Create(alice);

        card.Name.Should().Be("Spikefield Hazard");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void SpikefieldHazard_IsRed()
    {
        var alice = new Player("Alice", 20);
        var card = SpikefieldHazardFactory.Create(alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Red);
    }
    [Fact]
    public void SpikefieldHazard_CarriesMdfcState_FrontNameAndBackName()
    {
        var alice = new Player("Alice", 20);
        var card = SpikefieldHazardFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "Spikefield Hazard is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Spikefield Hazard");
        card.MdfcState!.BackFaceName.Should().Be("Spikefield Cave");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Spikefield Hazard");
    }

    // =========================================================================
    // Front face — resolve: 1 damage + exile-instead rider
    // =========================================================================

    [Fact]
    public void SpikefieldHazard_Resolve_DealsOneDamageToCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bear);

        var def = SpikefieldHazardFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bear } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Damage.Should().Be(1, "Spikefield Hazard deals 1 damage to any target (CR 120.3)");
    }

    [Fact]
    public void SpikefieldHazard_Resolve_PlayerTarget_TakesDamage()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var def = SpikefieldHazardFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bob.LifeTotal.Should().Be(19, "1 damage to a player drops them 20 → 19");
    }

    [Fact]
    public void SpikefieldHazard_Resolve_LethalCreature_IsExiledNotBuried_WhenBusSupplied()
    {
        // CR 700.3 — "a permanent dealt damage this way would die this turn,
        // exile it instead." A 1/1 dealt 1 damage dies; the exile rider
        // rewrites the battlefield→graveyard move to exile.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new ReplacementBus();

        var goblin = new Creature("Goblin", "{R}", 1, 1) { Owner = bob, Controller = bob };
        goblin.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(goblin);

        var def = SpikefieldHazardFactory.BuildSpellDefinition(alice, o => o, replacements: bus);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { goblin } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        goblin.Damage.Should().Be(1);

        // Simulate the SBA-driven battlefield→graveyard move (CR 704.5g) and
        // confirm the rider redirects it to exile.
        var intent = new ZoneMoveIntent(
            Card: goblin,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: bob);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.ToZone.Should().Be(ZoneType.Exile,
            "the lethally-damaged creature is exiled instead of dying (CR 700.3)");
    }

    [Fact]
    public void SpikefieldHazard_ExileRider_OnlyScopedToTargetedCreature()
    {
        // CR 700.3 — "that permanent" is the single creature this spell
        // damaged. An untouched creature that dies this turn is unaffected.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new ReplacementBus();

        var goblin = new Creature("Goblin", "{R}", 1, 1) { Owner = bob, Controller = bob };
        goblin.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(goblin);

        var bystander = new Creature("Bystander", "{R}", 1, 1) { Owner = bob, Controller = bob };
        bystander.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bystander);

        var def = SpikefieldHazardFactory.BuildSpellDefinition(alice, o => o, replacements: bus);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { goblin } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        var bystanderIntent = new ZoneMoveIntent(
            Card: bystander,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: bob);

        var after = bus.Apply(bystanderIntent);
        after.Should().NotBeNull();
        after!.ToZone.Should().Be(ZoneType.Graveyard,
            "a creature Spikefield Hazard did not damage is not exiled");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SpikefieldCave_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = SpikefieldCaveFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Spikefield Cave");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Spikefield Cave is a non-Basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }
    [Fact]
    public void SpikefieldCave_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = SpikefieldCaveFactory.Create(alice);

        land.MdfcState.Should().NotBeNull(
            "Spikefield Cave is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Spikefield Hazard");
        land.MdfcState!.BackFaceName.Should().Be("Spikefield Cave");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Spikefield Cave");
    }

    // =========================================================================
    // Back face — {T}: Add {R}
    // =========================================================================

    [Fact]
    public void SpikefieldCave_HasSingleManaAbility_AddingRed()
    {
        var alice = new Player("Alice", 20);
        var land = SpikefieldCaveFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {R} ability");

        var expected = ManaCost.Parse("R");
        manaAbilities[0].ManaGenerated.Generic.Should().Be(expected.Generic);
        manaAbilities[0].ManaGenerated.Red.Should().Be(expected.Red);
        manaAbilities[0].ManaGenerated.Red.Should().BeGreaterThan(0, "produces red mana");
    }

    [Fact]
    public void SpikefieldCave_HasNoActivatedOrTriggeredAbilities_BeyondMana()
    {
        var alice = new Player("Alice", 20);
        var land = SpikefieldCaveFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Spikefield Cave has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("enters-tapped is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — enters-tapped replacement
    // =========================================================================

    [Fact]
    public void SpikefieldCave_EntersTapped()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var land = SpikefieldCaveFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Spikefield Cave always enters tapped (CR 614.1c)");
    }
}
