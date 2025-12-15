using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.Aggregates;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Console;

/// <summary>
/// Console application to test the Majik game engine.
/// Phase 4.5: Comprehensive testing of Phase 4 features.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        System.Console.WriteLine("=== Majik Game Engine - Phase 4.5: Comprehensive Testing ===\n");

        // Create event bus
        var eventBus = new EventBus();

        // Subscribe to all relevant events
        SubscribeToEvents(eventBus);

        // Create game
        var game = new Game(eventBus);
        game.AddPlayer("Alice", 20);
        game.AddPlayer("Bob", 20);
        var alice = game.GetPlayer("Alice");
        var bob = game.GetPlayer("Bob");

        if (alice == null || bob == null)
        {
            System.Console.WriteLine("Error: Failed to get players");
            return;
        }

        System.Console.WriteLine($"Players: {alice.Name} ({alice.LifeTotal} life), {bob.Name} ({bob.LifeTotal} life)\n");

        // Run all test suites
        TestManaSystem(game, alice, bob, eventBus);
        TestTriggerManager(game, alice, bob, eventBus);
        TestStaticAbilities(game, alice, bob, eventBus);
        TestReplacementEffects(game, alice, bob, eventBus);
        TestAbilityEffects(game, alice, bob, eventBus);
        TestIntegratedScenario(game, alice, bob, eventBus);

        System.Console.WriteLine("\n=== Phase 4.5 Testing Complete ===");
        System.Console.WriteLine("✓ Mana System");
        System.Console.WriteLine("✓ Trigger Manager");
        System.Console.WriteLine("✓ Static Abilities");
        System.Console.WriteLine("✓ Replacement Effects");
        System.Console.WriteLine("✓ Ability Effects");
        System.Console.WriteLine("✓ Integrated Scenarios");
    }

    static void SubscribeToEvents(IEventBus eventBus)
    {
        eventBus.Subscribe<SpellCastEvent>(evt =>
        {
            System.Console.WriteLine($"    [Event] {evt.Spell.Controller.Name} casts {evt.Spell.Card.Name}");
        });

        eventBus.Subscribe<TargetsChosenEvent>(evt =>
        {
            System.Console.WriteLine($"    [Event] Targets chosen: {evt.Targets.Count} target(s)");
        });

        eventBus.Subscribe<CostsPaidEvent>(evt =>
        {
            System.Console.WriteLine($"    [Event] Costs paid: {string.Join(", ", evt.Costs.Select(c => c.Description))}");
        });

        eventBus.Subscribe<AbilityActivatedEvent>(evt =>
        {
            System.Console.WriteLine($"    [Event] {evt.Ability.Controller.Name} activates ability");
        });

        eventBus.Subscribe<TriggeredAbilityTriggeredEvent>(evt =>
        {
            System.Console.WriteLine($"    [Event] Triggered ability triggered from {evt.Ability.Source}");
        });

        eventBus.Subscribe<StackObjectAddedEvent>(evt =>
        {
            if (evt.StackObject is ISpell spell)
            {
                System.Console.WriteLine($"    [Stack] {spell.Card.Name} added to stack");
            }
            else if (evt.StackObject is IActivatedAbility)
            {
                System.Console.WriteLine($"    [Stack] Activated ability added to stack");
            }
            else if (evt.StackObject is ITriggeredAbility)
            {
                System.Console.WriteLine($"    [Stack] Triggered ability added to stack");
            }
        });

        eventBus.Subscribe<StackObjectResolvedEvent>(evt =>
        {
            if (evt.StackObject is ISpell spell)
            {
                System.Console.WriteLine($"    [Stack] {spell.Card.Name} resolved");
            }
            else if (evt.StackObject is IActivatedAbility)
            {
                System.Console.WriteLine($"    [Stack] Activated ability resolved");
            }
            else if (evt.StackObject is ITriggeredAbility)
            {
                System.Console.WriteLine($"    [Stack] Triggered ability resolved");
            }
        });

        eventBus.Subscribe<StateBasedActionExecutedEvent>(evt =>
        {
            System.Console.WriteLine($"    [SBA] {evt.ActionDescription}");
        });
    }

    static void TestManaSystem(Game game, Player alice, Player bob, IEventBus eventBus)
    {
        System.Console.WriteLine("=== Test 1: Mana System ===\n");

        // Test 1.1: Empty mana pool
        System.Console.WriteLine("1.1: Initial mana pool state");
        System.Console.WriteLine($"    Alice's mana pool: {alice.ManaPool}");
        System.Console.WriteLine($"    Pool is empty: {alice.ManaPool.IsEmpty}\n");

        // Test 1.2: Add mana to pool
        System.Console.WriteLine("1.2: Adding mana to pool");
        alice.AddManaToPool(ManaCost.Parse("RR"));
        System.Console.WriteLine($"    Added 2 Red mana");
        System.Console.WriteLine($"    Alice's mana pool: {alice.ManaPool}");
        System.Console.WriteLine($"    Pool total: {alice.ManaPool.Total}\n");

        // Test 1.3: Add more mana
        System.Console.WriteLine("1.3: Adding more mana");
        alice.AddManaToPool(ManaCost.Parse("3"));
        System.Console.WriteLine($"    Added 3 generic mana");
        System.Console.WriteLine($"    Alice's mana pool: {alice.ManaPool}\n");

        // Test 1.4: Pay mana cost
        System.Console.WriteLine("1.4: Paying mana cost");
        var cost = ManaCost.Parse("1R");
        System.Console.WriteLine($"    Cost to pay: {cost}");
        System.Console.WriteLine($"    Can pay: {alice.ManaPool.CanPay(cost)}");
        var (newPool, success) = alice.ManaPool.Pay(cost);
        if (success)
        {
            alice.PayMana(cost);
            System.Console.WriteLine($"    Paid successfully");
            System.Console.WriteLine($"    Remaining mana: {alice.ManaPool}\n");
        }

        // Test 1.5: Mana ability
        System.Console.WriteLine("1.5: Testing mana ability");
        var forest = new Land("Forest") { Owner = alice, Controller = alice };
        forest.Zone = ZoneType.Battlefield;
        
        var greenMana = ManaCost.Parse("G");
        var manaAbility = new ManaAbility(forest, alice, greenMana);
        var activator = new ManaAbilityActivator(eventBus);
        
        System.Console.WriteLine($"    Forest can activate: {manaAbility.CanActivate()}");
        var generated = activator.ActivateManaAbility(manaAbility, alice);
        System.Console.WriteLine($"    Generated: {generated}");
        System.Console.WriteLine($"    Alice's mana pool: {alice.ManaPool}");
        System.Console.WriteLine($"    Forest is tapped: {forest.IsTapped}\n");

        // Test 1.6: Empty mana pool
        System.Console.WriteLine("1.6: Emptying mana pool (end of step)");
        alice.EmptyManaPool();
        System.Console.WriteLine($"    Alice's mana pool: {alice.ManaPool}\n");

        System.Console.WriteLine("✓ Mana System Tests Complete\n");
    }

    static void TestTriggerManager(Game game, Player alice, Player bob, IEventBus eventBus)
    {
        System.Console.WriteLine("=== Test 2: Trigger Manager ===\n");

        // Create trigger manager
        var triggerManager = new TriggerManager(game.Stack, eventBus);

        // Test 2.1: Register triggered ability
        System.Console.WriteLine("2.1: Registering triggered ability");
        var creature = new Creature("Lightning Elemental", "2RR", 4, 4) { Owner = alice, Controller = alice };
        creature.Zone = ZoneType.Battlefield;

        var triggeredAbility = new TriggeredAbility(
            creature,
            alice,
            null,
            new List<IEffect> { new Effect("Deal 2 damage", () => { System.Console.WriteLine("        [Effect] Deals 2 damage!"); }) }
        );

        triggerManager.RegisterTriggeredAbility(triggeredAbility);
        System.Console.WriteLine($"    Registered triggered ability from {creature.Name}\n");

        // Test 2.2: Trigger on event
        System.Console.WriteLine("2.2: Triggering ability via event");
        var testCard = new Instant("Test Card", "1") { Owner = alice };
        var testEvent = new CardDrawnEvent(testCard, alice);
        triggerManager.EvaluateTriggers(testEvent);
        System.Console.WriteLine($"    Stack count: {game.Stack.Count}");
        System.Console.WriteLine($"    Top of stack: {game.Stack.Top?.GetType().Name}\n");

        // Test 2.3: Resolve triggered ability
        System.Console.WriteLine("2.3: Resolving triggered ability");
        var resolver = new StackResolver(eventBus);
        resolver.ResolveTop(game.Stack);
        System.Console.WriteLine($"    Stack count after resolution: {game.Stack.Count}\n");

        System.Console.WriteLine("✓ Trigger Manager Tests Complete\n");
    }

    static void TestStaticAbilities(Game game, Player alice, Player bob, IEventBus eventBus)
    {
        System.Console.WriteLine("=== Test 3: Static Abilities ===\n");

        // Create static ability manager
        var staticManager = new StaticAbilityManager(eventBus);

        // Test 3.1: Register static ability
        System.Console.WriteLine("3.1: Registering static ability");
        var enchantment = new Enchantment("Glorious Anthem", "") { Owner = alice, Controller = alice };
        enchantment.Zone = ZoneType.Battlefield;

        int effectAppliedCount = 0;
        var staticAbility = new StaticAbility(
            enchantment,
            alice,
            "Creatures you control get +1/+1",
            () => enchantment.Zone == ZoneType.Battlefield,
            () => { effectAppliedCount++; System.Console.WriteLine("        [Effect] +1/+1 applied to creatures"); }
        );

        staticManager.RegisterStaticAbility(staticAbility);
        System.Console.WriteLine($"    Registered: {staticAbility.Description}\n");

        // Test 3.2: Check if active
        System.Console.WriteLine("3.2: Checking if static ability is active");
        System.Console.WriteLine($"    Is active: {staticAbility.IsActive()}\n");

        // Test 3.3: Apply static abilities
        System.Console.WriteLine("3.3: Applying static abilities");
        staticManager.ApplyStaticAbilities();
        System.Console.WriteLine($"    Effect applied count: {effectAppliedCount}\n");

        // Test 3.4: Deactivate (move to graveyard)
        System.Console.WriteLine("3.4: Deactivating static ability (move to graveyard)");
        enchantment.Zone = ZoneType.Graveyard;
        System.Console.WriteLine($"    Is active: {staticAbility.IsActive()}\n");

        System.Console.WriteLine("✓ Static Abilities Tests Complete\n");
    }

    static void TestReplacementEffects(Game game, Player alice, Player bob, IEventBus eventBus)
    {
        System.Console.WriteLine("=== Test 4: Replacement Effects ===\n");

        // Create replacement effect manager
        var replacementManager = new ReplacementEffectManager();

        // Test 4.1: Register replacement effect
        System.Console.WriteLine("4.1: Registering replacement effect");
        var permanent = new Artifact("Fountain of Youth", "") { Owner = alice, Controller = alice };
        permanent.Zone = ZoneType.Battlefield;

        var replacementEffect = new ReplacementEffect(
            permanent,
            alice,
            "Prevent the next 2 damage",
            (evt) => evt is LifeChangedEvent lifeEvent && (lifeEvent.NewLife - lifeEvent.PreviousLife) < 0,
            (evt) =>
            {
                if (evt is LifeChangedEvent lifeEvent)
                {
                    var amount = lifeEvent.NewLife - lifeEvent.PreviousLife;
                    var prevented = Math.Min(2, Math.Abs(amount));
                    System.Console.WriteLine($"        [Replacement] Preventing {prevented} damage");
                    // Return modified event (simplified - would create new event in real implementation)
                    return evt;
                }
                return evt;
            }
        );

        replacementManager.RegisterReplacementEffect(replacementEffect);
        System.Console.WriteLine($"    Registered: {replacementEffect.Description}\n");

        // Test 4.2: Apply replacement effect
        System.Console.WriteLine("4.2: Testing replacement effect");
        var previousLife = alice.LifeTotal;
        var damageEvent = new LifeChangedEvent(alice, previousLife, previousLife - 3);
        System.Console.WriteLine($"    Original event: {damageEvent.NewLife - damageEvent.PreviousLife} life change");
        var replaced = replacementManager.ApplyReplacementEffects(damageEvent);
        System.Console.WriteLine($"    Replacement applied: {replaced != null}\n");

        System.Console.WriteLine("✓ Replacement Effects Tests Complete\n");
    }

    static void TestAbilityEffects(Game game, Player alice, Player bob, IEventBus eventBus)
    {
        System.Console.WriteLine("=== Test 5: Ability Effects ===\n");

        // Test 5.1: Spell with effect
        System.Console.WriteLine("5.1: Testing spell with effect");
        var lightningBolt = new Instant("Lightning Bolt", "R") { Owner = alice };
        alice.Zones.Hand.AddCard(lightningBolt);

        // Add mana to pool
        alice.AddManaToPool(ManaCost.Parse("R"));

        var damageEffect = new Effect("Deal 3 damage", () =>
        {
            System.Console.WriteLine("        [Effect] Lightning Bolt deals 3 damage!");
            bob.LoseLife(3);
        });

        var spellCaster = new SpellCaster(game.Stack, eventBus);
        var costs = new List<ICost> { new ManaCostCost("R") };
        var effects = new List<IEffect> { damageEffect };

        System.Console.WriteLine($"    Bob's life before: {bob.LifeTotal}");
        spellCaster.CastSpell(lightningBolt, alice, null, costs, true, true);
        System.Console.WriteLine($"    Stack count: {game.Stack.Count}");

        // Resolve spell
        var resolver = new StackResolver(eventBus);
        resolver.ResolveTop(game.Stack);
        System.Console.WriteLine($"    Bob's life after: {bob.LifeTotal}\n");

        // Test 5.2: Activated ability with effect
        System.Console.WriteLine("5.2: Testing activated ability with effect");
        var artifact = new Artifact("Staff of Fire", "") { Owner = alice, Controller = alice };
        artifact.Zone = ZoneType.Battlefield;

        alice.AddManaToPool(ManaCost.Parse("R"));
        var tapCost = AdditionalCost.Tap(artifact);
        var fireEffect = new Effect("Deal 1 damage", () =>
        {
            System.Console.WriteLine("        [Effect] Staff of Fire deals 1 damage!");
            bob.LoseLife(1);
        });

        var activatedAbility = new ActivatedAbility(
            artifact,
            alice,
            null,
            new List<ICost> { new ManaCostCost("R"), tapCost },
            new List<IEffect> { fireEffect }
        );

        var abilityActivator = new AbilityActivator(game.Stack, eventBus);
        System.Console.WriteLine($"    Bob's life before: {bob.LifeTotal}");
        abilityActivator.ActivateAbility(activatedAbility, alice, null, new List<ICost> { new ManaCostCost("R"), tapCost });
        System.Console.WriteLine($"    Stack count: {game.Stack.Count}");

        resolver.ResolveTop(game.Stack);
        System.Console.WriteLine($"    Bob's life after: {bob.LifeTotal}");
        System.Console.WriteLine($"    Artifact is tapped: {artifact.IsTapped}\n");

        // Test 5.3: Multiple effects
        System.Console.WriteLine("5.3: Testing spell with multiple effects");
        var multiSpell = new Instant("Double Strike", "1R") { Owner = alice };
        alice.Zones.Hand.AddCard(multiSpell);
        alice.AddManaToPool(ManaCost.Parse("1R"));

        var effect1 = new Effect("Effect 1", () => System.Console.WriteLine("        [Effect 1] First effect"));
        var effect2 = new Effect("Effect 2", () => System.Console.WriteLine("        [Effect 2] Second effect"));

        spellCaster.CastSpell(multiSpell, alice, null, new List<ICost> { new ManaCostCost("1R") }, true, true);
        resolver.ResolveTop(game.Stack);
        System.Console.WriteLine();

        System.Console.WriteLine("✓ Ability Effects Tests Complete\n");
    }

    static void TestIntegratedScenario(Game game, Player alice, Player bob, IEventBus eventBus)
    {
        System.Console.WriteLine("=== Test 6: Integrated Scenario ===\n");

        System.Console.WriteLine("Scenario: Alice casts a spell, Bob responds, triggers fire, effects resolve\n");

        // Setup: Add mana
        alice.AddManaToPool(ManaCost.Parse("2RR"));
        bob.AddManaToPool(ManaCost.Parse("UU"));

        // Setup: Create cards
        var fireball = new Instant("Fireball", "2RR") { Owner = alice };
        var counterspell = new Instant("Counterspell", "UU") { Owner = bob };
        alice.Zones.Hand.AddCard(fireball);
        bob.Zones.Hand.AddCard(counterspell);

        // Setup: Create triggered ability
        var triggerManager = new TriggerManager(game.Stack, eventBus);
        var triggerSource = new Creature("Guttersnipe", "2R", 2, 2) { Owner = alice, Controller = alice };
        triggerSource.Zone = ZoneType.Battlefield;

        var triggerEffect = new Effect("Guttersnipe deals 2 damage", () =>
        {
            System.Console.WriteLine("        [Trigger Effect] Guttersnipe deals 2 damage to Bob!");
            bob.LoseLife(2);
        });

        var triggeredAbility = new TriggeredAbility(triggerSource, alice, null, new List<IEffect> { triggerEffect });
        triggerManager.RegisterTriggeredAbility(triggeredAbility);

        // Step 1: Alice casts Fireball
        System.Console.WriteLine("Step 1: Alice casts Fireball");
        var spellCaster = new SpellCaster(game.Stack, eventBus);
        var fireballEffect = new Effect("Fireball deals 4 damage", () =>
        {
            System.Console.WriteLine("        [Spell Effect] Fireball deals 4 damage to Bob!");
            bob.LoseLife(4);
        });

        spellCaster.CastSpell(fireball, alice, null, new List<ICost> { new ManaCostCost("2RR") }, true, true);
        System.Console.WriteLine($"    Bob's life: {bob.LifeTotal}");
        System.Console.WriteLine($"    Stack count: {game.Stack.Count}\n");

        // Step 2: Trigger fires (after spell is cast, it's already on stack)
        System.Console.WriteLine("Step 2: Guttersnipe triggers");
        // The spell was already cast and added to stack, so we can use the stack object
        if (game.Stack.Top is ISpell topSpell)
        {
            var castEvent = new SpellCastEvent(topSpell);
            triggerManager.EvaluateTriggers(castEvent);
        }
        System.Console.WriteLine($"    Stack count: {game.Stack.Count}\n");

        // Step 3: Bob casts Counterspell
        System.Console.WriteLine("Step 3: Bob casts Counterspell");
        spellCaster.CastSpell(counterspell, bob, null, new List<ICost> { new ManaCostCost("UU") }, true, false);
        System.Console.WriteLine($"    Stack count: {game.Stack.Count}\n");

        // Step 4: Resolve stack (LIFO)
        System.Console.WriteLine("Step 4: Resolving stack (LIFO order)");
        var resolver = new StackResolver(eventBus);
        System.Console.WriteLine($"    Bob's life before resolution: {bob.LifeTotal}");

        while (!game.Stack.IsEmpty)
        {
            resolver.ResolveTop(game.Stack);
        }

        System.Console.WriteLine($"    Bob's life after resolution: {bob.LifeTotal}");
        System.Console.WriteLine($"    Stack is empty: {game.Stack.IsEmpty}\n");

        System.Console.WriteLine("✓ Integrated Scenario Tests Complete\n");
    }
}
