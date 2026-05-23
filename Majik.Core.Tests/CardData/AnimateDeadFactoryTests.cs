using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AnimateDeadFactory"/>.
///
/// Covers:
/// - Card identity (Enchantment + Aura subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - SpellDefinition target shape: creature CARDs in graveyards.
/// - Resolve effect reanimates the chosen creature + auto-attaches the aura.
/// - LTB trigger sacrifices the attached creature on leave-battlefield.
/// - Static -1/-0 boost via AttachedBoostEffect at Layer 7c.
/// </summary>
public class AnimateDeadFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AnimateDead_Identity()
    {
        var c = AnimateDeadFactory.Create(_alice);

        c.Name.Should().Be("Animate Dead");
        c.Should().BeOfType<Enchantment>();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.IsAura.Should().BeTrue();
        c.ManaCost.Should().Be("{1}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AnimateDead_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Animate Dead", _alice);

        c.Should().BeOfType<Enchantment>("Animate Dead is an Enchantment");
        c.Name.Should().Be("Animate Dead");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void AnimateDead_BuildSpellDefinition_ListsCreatureCardsInGraveyardsAsTargets()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Mix of cards in graveyards — only creature cards should be
        // returned as candidates.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var aura = AnimateDeadFactory.Create(alice);

        var def = AnimateDeadFactory.BuildSpellDefinition(
            aura, new[] { alice, bob });

        def.TargetRequests.Should().HaveCount(1);
        var request = def.TargetRequests[0];
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
        request.LegalCandidates.Should().Contain(new object[] { bear, giant },
            "creature cards in any graveyard are legal targets (CR 700.6)");
        request.LegalCandidates.Should().NotContain(bolt,
            "Lightning Bolt is an instant — not a creature card");
    }

    [Fact]
    public void AnimateDead_Resolve_ReanimatesChosenCard_AndAttachesAuraToIt()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var aura = AnimateDeadFactory.Create(
            alice, continuousEffects: null, zoneService: zones, eventBus: bus, triggers: null);

        var def = AnimateDeadFactory.BuildSpellDefinition(
            aura, new[] { alice }, zoneService: zones);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { giant } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        giant.Zone.Should().Be(ZoneType.Battlefield,
            "the chosen creature card was reanimated to the caster's battlefield");
        giant.Controller.Should().BeSameAs(alice,
            "reanimated under the caster's control (CR 110.2)");
        aura.AttachedTo.Should().BeSameAs(giant,
            "Animate Dead auto-attaches to the creature it reanimates (CR 303.4f)");
    }

    [Fact]
    public void AnimateDead_StaticMinusOneZero_AppliesViaContinuousEffects()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var aura = AnimateDeadFactory.Create(
            alice, continuousEffects: effects, zoneService: null, eventBus: bus, triggers: null);

        // Put the aura on the battlefield and attach to the bear so the
        // AttachedBoostEffect's IsActive predicate evaluates true.
        alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);

        chars.Power.Should().Be(1, "Grizzly Bears base 2 power minus 1 from Animate Dead = 1");
        chars.Toughness.Should().Be(2, "Grizzly Bears base 2 toughness; -0 leaves it unchanged");
    }

    [Fact]
    public void AnimateDead_LTB_SacrificesAttachedCreature_OnLeaveBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        // Put a creature directly on the battlefield.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var aura = AnimateDeadFactory.Create(
            alice, continuousEffects: null, zoneService: zones, eventBus: bus, triggers: null);

        // Place aura on the battlefield and attach to bear.
        alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
        aura.AttachTo(bear);

        // Resolve the LTB effect directly: AnimateDead leaves the
        // battlefield → attached creature is sacrificed (moved to its
        // owner's graveyard).
        var ltb = aura.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in ltb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "the attached creature is sacrificed when Animate Dead leaves the battlefield (CR 701.16)");
        alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }
}
