using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlissaSunslayerFactory"/>.
///
/// Glissa Sunslayer (Phyrexia: All Will Be One, {B}{G}). Legendary
/// Creature — Phyrexian Zombie Elf 3/3. Oracle text (verified against
/// Scryfall):
///   "First strike, deathtouch
///    Whenever Glissa Sunslayer deals combat damage to a player, choose one —
///    • You draw a card and lose 1 life.
///    • Destroy target enchantment.
///    • Remove up to three counters from target permanent."
///
/// Covers:
/// - Identity ({B}{G} Legendary Creature — Phyrexian Zombie Elf, 3/3, BG).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - First strike + deathtouch keyword markers (CR 702.7 / CR 702.2).
/// - Exactly one battlefield-active combat-damage-to-a-player trigger.
/// - Mode 0 (draw + lose 1 life).
/// - Mode 1 (destroy target enchantment).
/// - Mode 2 (remove up to three counters from target permanent).
/// </summary>
public class GlissaSunslayerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GlissaSunslayer_Identity()
    {
        var c = GlissaSunslayerFactory.Create(_alice);

        c.Name.Should().Be("Glissa Sunslayer");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Glissa is legendary");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.ManaCost.Should().Be("{B}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GlissaSunslayer_IsBlackGreen()
    {
        var c = GlissaSunslayerFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Black);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().HaveCount(2);
    }

    [Fact]
    public void GlissaSunslayer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Glissa Sunslayer", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Glissa Sunslayer");
        c.ManaCost.Should().Be("{B}{G}");
    }

    // -----------------------------------------------------------------------
    // Keywords
    // -----------------------------------------------------------------------

    [Fact]
    public void GlissaSunslayer_HasFirstStrikeAndDeathtouch()
    {
        var c = GlissaSunslayerFactory.Create(_alice);

        CombatAbilities.HasFirstStrike(c).Should().BeTrue("CR 702.7 — first strike");
        CombatAbilities.HasDeathtouch(c).Should().BeTrue("CR 702.2 — deathtouch");
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GlissaSunslayer_HasExactlyOneCombatDamageTrigger_BattlefieldActive()
    {
        var c = GlissaSunslayerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one combat-damage modal trigger");

        var trig = triggers.Single();
        trig.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "combat-damage triggers are battlefield-active (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — You draw a card and lose 1 life
    // -----------------------------------------------------------------------

    [Fact]
    public void GlissaSunslayer_Mode0_DrawsACardAndLosesOneLife()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Creature("CardA", "{G}", 1, 1);
        alice.Zones.Library.AddCard(topCard);

        var glissa = GlissaSunslayerFactory.Create(alice, mode: GlissaSunslayerFactory.ModeDrawLose);

        var trig = glissa.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trig.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard, "mode 0 draws the top card");
        alice.Zones.Library.GetCards().Should().NotContain(topCard);
        alice.LifeTotal.Should().Be(19, "mode 0 — controller loses exactly 1 life (CR 119.3)");
    }

    [Fact]
    public void GlissaSunslayer_Mode0_EmptyLibrary_FlagsDrawFromEmpty_AndStillLosesLife()
    {
        var alice = new Player("Alice", 20);
        // No cards in library.
        var glissa = GlissaSunslayerFactory.Create(alice, mode: GlissaSunslayerFactory.ModeDrawLose);

        var trig = glissa.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trig.Effects) effect.Execute();

        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library flags the player for SBA loss (CR 704.5b)");
        alice.LifeTotal.Should().Be(19, "the life loss runs regardless of the draw");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Destroy target enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void GlissaSunslayer_Mode1_DestroysTargetEnchantment()
    {
        var glissa = GlissaSunslayerFactory.Create(_alice, mode: GlissaSunslayerFactory.ModeDestroyEnchantment);

        var enchantment = new Enchantment("Pacifism", "{1}{W}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var trig = glissa.Abilities.OfType<TriggeredAbility>().Single();
        trig.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { enchantment },
        });
        foreach (var effect in trig.Effects) effect.Execute();

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            "mode 1 destroys the target enchantment (CR 701.7)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(enchantment);
    }

    [Fact]
    public void GlissaSunslayer_Mode1_NonEnchantmentTarget_IsNoOp()
    {
        var glissa = GlissaSunslayerFactory.Create(_alice, mode: GlissaSunslayerFactory.ModeDestroyEnchantment);

        // A creature is not a legal "target enchantment".
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var trig = glissa.Abilities.OfType<TriggeredAbility>().Single();
        trig.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { creature },
        });
        foreach (var effect in trig.Effects) effect.Execute();

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "a non-enchantment is not a legal target — no-op (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — Remove up to three counters from target permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void GlissaSunslayer_Mode2_RemovesUpToThreeCounters()
    {
        var glissa = GlissaSunslayerFactory.Create(_alice, mode: GlissaSunslayerFactory.ModeRemoveCounters);

        var target = new Creature("Walking Ballista", "{0}", 0, 0);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);
        target.Counters.Add(CounterType.PlusOnePlusOne, 5);

        var trig = glissa.Abilities.OfType<TriggeredAbility>().Single();
        trig.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var effect in trig.Effects) effect.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "mode 2 removes up to three counters from the target permanent");
    }

    [Fact]
    public void GlissaSunslayer_Mode2_FewerThanThreeCounters_RemovesAll()
    {
        var glissa = GlissaSunslayerFactory.Create(_alice, mode: GlissaSunslayerFactory.ModeRemoveCounters);

        var target = new Creature("Quirion Beastcaller", "{B}{G}", 1, 1);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);
        target.Counters.Add(CounterType.PlusOnePlusOne, 2);

        var trig = glissa.Abilities.OfType<TriggeredAbility>().Single();
        trig.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var effect in trig.Effects) effect.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "fewer than three counters present — 'up to three' removes all of them");
    }

    // -----------------------------------------------------------------------
    // Wired path — combat damage to a player fires the trigger end-to-end
    // -----------------------------------------------------------------------

    [Fact]
    public void GlissaSunslayer_WiredCreate_CombatDamageToPlayer_Mode0_DrawsAndLosesLife()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Creature("CardA", "{G}", 1, 1);
        alice.Zones.Library.AddCard(topCard);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var glissa = GlissaSunslayerFactory.Create(
            alice, mode: GlissaSunslayerFactory.ModeDrawLose, triggers: triggerManager);
        glissa.SetZone(ZoneType.Battlefield);

        // Glissa deals combat damage to Bob.
        bus.Publish(new CombatDamageDealtEvent(glissa, _bob, amount: 3));

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "combat damage to a player fires the modal trigger; mode 0 draws a card");
        alice.LifeTotal.Should().Be(19, "and the controller loses 1 life");
    }

    [Fact]
    public void GlissaSunslayer_WiredCreate_DamageToCreature_DoesNotFireTrigger()
    {
        var alice = new Player("Alice", 20);
        alice.Zones.Library.AddCard(new Creature("CardA", "{G}", 1, 1));

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var glissa = GlissaSunslayerFactory.Create(
            alice, mode: GlissaSunslayerFactory.ModeDrawLose, triggers: triggerManager);
        glissa.SetZone(ZoneType.Battlefield);

        // Glissa deals combat damage to a CREATURE, not a player.
        var blocker = new Creature("Wall", "{1}", 0, 4);
        bus.Publish(new CombatDamageDealtEvent(glissa, (ICard)blocker, amount: 3));

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the trigger only fires on combat damage to a PLAYER (CR 603.1)");
        alice.LifeTotal.Should().Be(20);
    }
}
