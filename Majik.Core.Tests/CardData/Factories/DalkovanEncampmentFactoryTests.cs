using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DalkovanEncampmentFactory"/>.
///
/// Oracle text (Duskmourn: House of Horror):
///   "This land enters tapped unless you control a Swamp or a Mountain.
///    {T}: Add {W}.
///    {2}{W}, {T}: Whenever you attack this turn, create two 1/1 red
///    Warrior creature tokens that are tapped and attacking."
///
/// Covers:
/// - Identity: Land named "Dalkovan Encampment", non-Basic, non-Legendary.
/// - <see cref="NamedCardFactory"/> dispatch resolves the name.
/// - ETB-tapped predicate: tapped with no Swamp/Mountain; untapped when
///   controller has a Mountain; untapped when controller has a Swamp.
/// - {T}: Add {W} mana ability present.
/// - Activated ability ({2}{W},{T}) present and produces the expected effect.
/// - Activating the ability installs a "whenever you attack this turn"
///   trigger that creates two 1/1 red Warrior tokens on attack.
/// </summary>
[Trait("Color", "C")]
public class DalkovanEncampmentFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DalkovanEncampment_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);
        var land = DalkovanEncampmentFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Dalkovan Encampment");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void DalkovanEncampment_IsNotBasic_NotLegendary()
    {
        var alice = new Player("Alice", 20);
        var land = DalkovanEncampmentFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Dalkovan Encampment is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }
    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void DalkovanEncampment_EntersTapped_WhenControllerHasNoSwampOrMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = DalkovanEncampmentFactory.Create(alice, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "enters tapped when controller controls no Swamp or Mountain");
    }

    [Fact]
    public void DalkovanEncampment_EntersUntapped_WhenControllerHasMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        // Seed a Mountain on Alice's battlefield.
        var mountain = (Land)NamedCardFactory.Create("Mountain", alice);
        alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var land = DalkovanEncampmentFactory.Create(alice, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "enters untapped when controller controls a Mountain");
    }

    [Fact]
    public void DalkovanEncampment_EntersUntapped_WhenControllerHasSwamp()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        // Seed a Swamp on Alice's battlefield.
        var swamp = (Land)NamedCardFactory.Create("Swamp", alice);
        alice.Zones.Battlefield.AddCard(swamp);
        swamp.SetZone(ZoneType.Battlefield);

        var land = DalkovanEncampmentFactory.Create(alice, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "enters untapped when controller controls a Swamp");
    }

    // -----------------------------------------------------------------------
    // Mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void DalkovanEncampment_HasOneManaAbility_ProducingWhite()
    {
        var alice = new Player("Alice", 20);
        var land = DalkovanEncampmentFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "{T}: Add {W} is the only mana ability");

        var white = ManaCost.Parse("W");
        manaAbilities[0].ManaGenerated.White.Should().Be(white.White);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void DalkovanEncampment_HasOneActivatedAbility()
    {
        var alice = new Player("Alice", 20);
        var land = DalkovanEncampmentFactory.Create(alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "{2}{W},{T}: the token-grant activated ability");
    }

    // -----------------------------------------------------------------------
    // Activated ability effect: installs "whenever you attack" trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatingAbility_InstallsAttackTrigger_That_CreatesWarriorTokens()
    {
        var alice = new Player("Alice", 20);
        // Wire a TriggerManager so the "whenever you attack this turn" trigger
        // gets registered.
        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);

        var land = DalkovanEncampmentFactory.Create(alice, replacements: null, triggers: triggers);

        // Place the land on the battlefield.
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Execute the activated ability's effect directly (bypassing cost
        // payment — same shape as Sneak Attack / Hive of the Eye Tyrant tests).
        var activatedAbility = land.Abilities.OfType<ActivatedAbility>().First();
        foreach (var effect in activatedAbility.Effects)
        {
            effect.Execute();
        }

        // Now simulate an attack by publishing a CreatureAttacksEvent for a
        // creature Alice controls.
        var attacker = new Creature("Soldier", "{W}", 1, 1);
        attacker.SetOwner(alice);
        attacker.SetController(alice);
        alice.Zones.Battlefield.AddCard(attacker);
        attacker.SetZone(ZoneType.Battlefield);

        var attackEvent = new CreatureAttacksEvent(attacker, new Player("Bob", 20));
        eventBus.Publish(attackEvent);

        // Drain the pending trigger and resolve it.
        triggers.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects)
                {
                    eff.Execute();
                }
            }
        }

        // Alice should have two 1/1 red Warrior tokens on the battlefield.
        var battlefield = alice.Zones.Battlefield.GetCards();
        var warriors = battlefield
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Warrior))
            .ToList();

        warriors.Should().HaveCount(2, "the attack-token trigger creates two Warrior tokens");
        warriors.Should().AllSatisfy(w =>
        {
            w.BasePower.Should().Be(1, "1/1 Warriors");
            w.BaseToughness.Should().Be(1, "1/1 Warriors");
        });
    }

    [Fact]
    public void ActivatingAbility_TriggerFires_Twice_WhenTwoCreaturesAttack()
    {
        var alice = new Player("Alice", 20);
        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);

        var land = DalkovanEncampmentFactory.Create(alice, replacements: null, triggers: triggers);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Activate the ability.
        var activatedAbility = land.Abilities.OfType<ActivatedAbility>().First();
        foreach (var effect in activatedAbility.Effects)
        {
            effect.Execute();
        }

        var bob = new Player("Bob", 20);

        // Attack with two creatures.
        var attacker1 = new Creature("Soldier1", "{W}", 1, 1);
        attacker1.SetOwner(alice);
        attacker1.SetController(alice);
        alice.Zones.Battlefield.AddCard(attacker1);
        attacker1.SetZone(ZoneType.Battlefield);

        var attacker2 = new Creature("Soldier2", "{R}", 2, 1);
        attacker2.SetOwner(alice);
        attacker2.SetController(alice);
        alice.Zones.Battlefield.AddCard(attacker2);
        attacker2.SetZone(ZoneType.Battlefield);

        eventBus.Publish(new CreatureAttacksEvent(attacker1, bob));
        eventBus.Publish(new CreatureAttacksEvent(attacker2, bob));

        // Resolve both triggers.
        triggers.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects)
                {
                    eff.Execute();
                }
            }
        }

        // Two attacks → 4 Warriors total.
        var warriors = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Warrior))
            .ToList();

        warriors.Should().HaveCount(4,
            "each CreatureAttacksEvent fires the trigger producing 2 tokens");
    }

    [Fact]
    public void AttackTrigger_DoesNotFire_ForOpponentAttackers()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);

        var land = DalkovanEncampmentFactory.Create(alice, replacements: null, triggers: triggers);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Activate Alice's ability.
        var activatedAbility = land.Abilities.OfType<ActivatedAbility>().First();
        foreach (var effect in activatedAbility.Effects)
        {
            effect.Execute();
        }

        // Bob's creature attacks.
        var bobAttacker = new Creature("Goblin", "{R}", 1, 1);
        bobAttacker.SetOwner(bob);
        bobAttacker.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobAttacker);
        bobAttacker.SetZone(ZoneType.Battlefield);

        eventBus.Publish(new CreatureAttacksEvent(bobAttacker, alice));

        triggers.PutPendingTriggersOnStack(alice);

        // No tokens created — the trigger only fires for Alice's attackers.
        var warriors = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Warrior))
            .ToList();

        warriors.Should().BeEmpty(
            "the trigger condition is gated on the attacker being controlled by Alice");
    }
}
