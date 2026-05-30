using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RipApartFactory"/> (Strixhaven, {R}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Rip Apart deals 3 damage to target creature or planeswalker.
///     • Destroy target artifact or enchantment."
///
/// CR 700.2d — modal "Choose one —" with per-mode targeting. Modal shape
/// mirrors <see cref="WitherbloomCharmFactory"/>; the damage mode mirrors
/// <see cref="FlameSlashFactory"/> (extended to planeswalkers), the destroy
/// mode mirrors <see cref="NaturalizeFactory"/>.
/// </summary>
public class RipApartFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static IReadOnlyList<object>[] Slots(int modeIndex, params object[] targets)
    {
        var slots = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        slots[modeIndex] = targets;
        return slots;
    }

    private ChosenSpellParams Chosen(int modeIndex, params object[] targets) =>
        new(
            ModeIndex: modeIndex,
            X: null,
            Targets: Slots(modeIndex, targets),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void RipApart_Create_HasSorceryShape_RedWhite()
    {
        var card = RipApartFactory.Create(_alice);

        card.Name.Should().Be("Rip Apart");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{R}{W} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RipApart_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Rip Apart", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Rip Apart");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void RipApart_BuildDefinition_ExposesModes_AndPerModeTargets()
    {
        var def = RipApartFactory.BuildDefinition(o => o);

        def.Modes.Should().HaveCount(2);
        def.Modes[RipApartFactory.ModeDamage].Should().Contain("damage");
        def.Modes[RipApartFactory.ModeDestroy].Should().Contain("Destroy");

        def.TargetRequests.Should().HaveCount(2);
        // Each mode carries its own target, MinTargets=0 so the unchosen mode
        // doesn't gate the cast.
        def.TargetRequests[RipApartFactory.ModeDamage].MinTargets.Should().Be(0);
        def.TargetRequests[RipApartFactory.ModeDamage].MaxTargets.Should().Be(1);
        def.TargetRequests[RipApartFactory.ModeDamage].Description.Should().Contain("creature");
        def.TargetRequests[RipApartFactory.ModeDestroy].MinTargets.Should().Be(0);
        def.TargetRequests[RipApartFactory.ModeDestroy].MaxTargets.Should().Be(1);
        def.TargetRequests[RipApartFactory.ModeDestroy].Description.Should().Contain("artifact");
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — 3 damage to target creature or planeswalker.
    // -----------------------------------------------------------------------

    [Fact]
    public void RipApart_Mode0_DealsThreeDamageToCreature()
    {
        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var def = RipApartFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(RipApartFactory.ModeDamage, target))) e.Execute();

        target.Damage.Should().Be(3, because: "mode 0 deals 3 damage to target creature");
    }

    [Fact]
    public void RipApart_Mode0_RemovesThreeLoyaltyFromPlaneswalker()
    {
        var pw = new Planeswalker("Test Walker", "{2}{R}", startingLoyalty: 5)
        { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = RipApartFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(RipApartFactory.ModeDamage, pw))) e.Execute();

        pw.Loyalty.Should().Be(2,
            because: "3 damage to a planeswalker removes 3 loyalty (CR 306.7)");
    }

    [Fact]
    public void RipApart_Mode0_NoOp_OnNonCreatureNonPlaneswalkerTarget()
    {
        // CR 608.2b — a player is not a legal target for mode 0; no damage.
        var def = RipApartFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(RipApartFactory.ModeDamage, _bob))) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            because: "mode 0 damages only creatures/planeswalkers, not players");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy target artifact or enchantment.
    // -----------------------------------------------------------------------

    [Fact]
    public void RipApart_Mode1_DestroysArtifact()
    {
        var artifact = new Artifact("Mind Stone", "{2}") { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var def = RipApartFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(RipApartFactory.ModeDestroy, artifact))) e.Execute();

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys the targeted artifact");
    }

    [Fact]
    public void RipApart_Mode1_DestroysEnchantment()
    {
        var enchantment = new Enchantment("Pacifism", "{1}{W}") { Owner = _bob, Controller = _bob };
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var def = RipApartFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(RipApartFactory.ModeDestroy, enchantment))) e.Execute();

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys the targeted enchantment");
    }

    [Fact]
    public void RipApart_Mode1_DoesNotDestroyCreature()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = RipApartFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(RipApartFactory.ModeDestroy, creature))) e.Execute();

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 1 destroys only artifacts/enchantments, not creatures (CR 608.2b)");
    }
}
