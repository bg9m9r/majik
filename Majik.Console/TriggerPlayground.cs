using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Console;

/// <summary>
/// Functional playground for the Rule 603 triggered-ability + stack
/// resolution system. Runs a handful of scenarios end-to-end with a
/// verbose event trace so you can eyeball the timing.
/// </summary>
internal static class TriggerPlayground
{
    public static void Run(string scenario)
    {
        switch (scenario.ToLowerInvariant())
        {
            case "etb": RunEtb(); break;
            case "apnap": RunApnap(); break;
            case "intervening-if": RunInterveningIf(); break;
            case "delayed": RunDelayed(); break;
            case "priority-loop": RunPriorityLoop().GetAwaiter().GetResult(); break;
            case "play-game-db": RunPlayGameDb(); break;
            case "play-full-game": RunPlayFullGame().GetAwaiter().GetResult(); break;
            case "play-heuristic": RunPlayHeuristic().GetAwaiter().GetResult(); break;
            case "play-modern-faceoff": RunModernFaceoff().GetAwaiter().GetResult(); break;
            case "all":
                RunEtb();
                RunApnap();
                RunInterveningIf();
                RunDelayed();
                RunPriorityLoop().GetAwaiter().GetResult();
                RunPlayGameDb();
                RunPlayFullGame().GetAwaiter().GetResult();
                RunPlayHeuristic().GetAwaiter().GetResult();
                break;
            default:
                System.Console.WriteLine($"Unknown scenario '{scenario}'.");
                PrintScenarios();
                break;
        }
    }

    public static void PrintScenarios()
    {
        System.Console.WriteLine("Scenarios:");
        System.Console.WriteLine("  etb             Self ETB trigger ('When ~ enters, gain 1 life')");
        System.Console.WriteLine("  apnap           Two Soul Wardens, APNAP ordering of pending triggers");
        System.Console.WriteLine("  intervening-if  Trigger countered on resolution when 'if' clause flips false");
        System.Console.WriteLine("  delayed         Delayed triggered ability fires once and auto-unregisters");
        System.Console.WriteLine("  priority-loop   Async PriorityLoop driven by DeterministicBotAgent vs bot");
        System.Console.WriteLine("  play-game-db    Load Lightning Bolt/Mountain from Scryfall DB, play one slice");
        System.Console.WriteLine("  play-full-game  Bot vs bot through GameDriver, multiple full turns");
        System.Console.WriteLine("  play-heuristic  HeuristicBotAgent — plays lands + attacks + blocks");
        System.Console.WriteLine("  play-modern-faceoff  Boros Energy vs Eldrazi-Affinity, HeuristicBot vs HeuristicBot");
        System.Console.WriteLine("  all             Run every scenario in sequence");
    }

    // -------------------------------------------------------------------

    private static void RunEtb()
    {
        Banner("Scenario: Soul Warden self-ETB");
        var ctx = NewContext(out var alice, out _);

        var warden = new Creature("Soul Warden", "W", 1, 1) { Owner = alice, Zone = ZoneType.Hand };
        var ability = new TriggeredAbility(
            warden, alice,
            Triggers.OnEnterBattlefieldSelf(warden),
            effects: new IEffect[] { new Effect("gain 1 life", () => alice.GainLife(1)) });
        warden.AddAbility(ability);
        ctx.Triggers.BindCard(warden);

        Log($"Alice life before: {alice.LifeTotal}");
        Log("Casting Soul Warden — moving Hand → Battlefield");
        ctx.Zones.MoveCardTo(warden, ZoneType.Battlefield, controller: alice);
        Log($"PendingCount after move: {ctx.Triggers.PendingCount}  (queued, not on stack yet)");

        Log("Active player Alice would receive priority — drain pending");
        ctx.Priority.InitializeForPhase(alice);
        Log($"Stack count after drain: {ctx.Stack.Count}");

        Log("Both players pass priority — resolver pops + resolves top");
        ctx.Resolver.ResolveTop(ctx.Stack);
        Log($"Alice life after: {alice.LifeTotal}");
    }

    private static void RunApnap()
    {
        Banner("Scenario: APNAP ordering of two Soul Wardens");
        var ctx = NewContext(out var alice, out var bob);

        var aliceW = MakeAnyEtbLifeGainer("Alice's Warden", alice);
        var bobW = MakeAnyEtbLifeGainer("Bob's Warden",  bob);
        aliceW.SetZone(ZoneType.Battlefield);
        bobW.SetZone(ZoneType.Battlefield);
        ctx.Triggers.BindCard(aliceW);
        ctx.Triggers.BindCard(bobW);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = alice, Zone = ZoneType.Hand };
        Log("Alice casts Grizzly Bears — moving Hand → Battlefield");
        ctx.Zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        Log($"PendingCount: {ctx.Triggers.PendingCount}  (both Wardens fired)");
        Log("Drain at next priority — APNAP order, active player's trigger pushed first");
        ctx.Priority.InitializeForPhase(alice);

        Log($"Stack top is {((ITriggeredAbility)ctx.Stack.Top!).Controller.Name}'s trigger (resolves first)");
        ctx.Resolver.ResolveTop(ctx.Stack);
        ctx.Resolver.ResolveTop(ctx.Stack);
        Log($"Alice life: {alice.LifeTotal}  Bob life: {bob.LifeTotal}");
    }

    private static void RunInterveningIf()
    {
        Banner("Scenario: intervening-if countering on resolution");
        var ctx = NewContext(out var alice, out _);

        var lethalThreshold = true;
        var ran = false;
        var source = new Creature("Watcher", "1W", 1, 1)
        {
            Owner = alice, Zone = ZoneType.Battlefield,
        };
        var ability = new TriggeredAbility(
            source, alice,
            Triggers.OnEnterBattlefieldSelf(source),
            effects: new IEffect[] { new Effect("would gain 5", () => { ran = true; alice.GainLife(5); }) },
            interveningIf: () => lethalThreshold);
        source.AddAbility(ability);
        ctx.Triggers.BindCard(source);

        Log("Resimulating: source already on battlefield; manually firing ETB event");
        ctx.Bus.Publish(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));
        Log($"PendingCount: {ctx.Triggers.PendingCount}  (intervening-if was true at trigger time)");

        ctx.Priority.InitializeForPhase(alice);
        Log($"Stack count: {ctx.Stack.Count}");

        Log("BEFORE resolution: flip intervening-if false");
        lethalThreshold = false;

        ctx.Resolver.ResolveTop(ctx.Stack);
        Log($"Effect ran? {ran}   (false = countered per Rule 603.4)");
        Log($"Stack count after resolve attempt: {ctx.Stack.Count}");
    }

    private static void RunDelayed()
    {
        Banner("Scenario: delayed triggered ability");
        var ctx = NewContext(out var alice, out _);

        var card = new Instant("Source", "1") { Owner = alice };
        var fires = 0;
        var delayed = new DelayedTriggeredAbility(
            card, alice,
            Triggers.OnCardDrawnByPlayer(alice),
            effects: new IEffect[] { new Effect("count", () => fires++) });
        ctx.Triggers.RegisterDelayed(delayed);

        Log("Alice draws — delayed trigger should fire once");
        ctx.Bus.Publish(new CardDrawnEvent(card, alice));
        ctx.Priority.InitializeForPhase(alice);
        ctx.Resolver.ResolveTop(ctx.Stack);
        Log($"Fires after 1st draw: {fires}");
        Log($"IsRegistered after firing: {ctx.Triggers.IsRegistered(delayed)}");

        Log("Alice draws again — should NOT fire");
        ctx.Bus.Publish(new CardDrawnEvent(card, alice));
        Log($"PendingCount: {ctx.Triggers.PendingCount}   Fires total: {fires}");
    }

    private static async Task RunPlayHeuristic()
    {
        Banner("Scenario: Heuristic bot vs heuristic bot — lands + attacks + blocks");

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new Majik.Core.Services.StackResolver(bus, zones);
        var sba = new Majik.Core.Rules.StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, bus, triggers);
        var combat = new Majik.Core.Combat.CombatFlow(bus, sba);

        // Seed: 20 mountains + 10 grizzly bears each.
        var rng = new Majik.Core.Random.GameRandom(1234);
        foreach (var p in new[] { alice, bob })
        {
            for (var i = 0; i < 20; i++)
            {
                var m = Majik.Core.CardData.NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(m); m.SetZone(ZoneType.Library);
            }
            for (var i = 0; i < 10; i++)
            {
                var bear = Majik.Core.CardData.NamedCardFactory.Create("Grizzly Bears", p);
                p.Zones.Library.AddCard(bear); bear.SetZone(ZoneType.Library);
            }
        }

        var driver = new GameDriver(
            new[] { alice, bob },
            new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new HeuristicBotAgent(new RuntimeFlashbackAltCostProbe()),
                [bob] = new HeuristicBotAgent(new RuntimeFlashbackAltCostProbe()),
            },
            stack, zones, triggers, resolver, sba, priority, combat, rng);

        Log("Running 8 turns…");
        var result = await driver.RunGameAsync(maxTurns: 8);

        Log($"Turns: {result.TurnsPlayed}   Winner: {result.Winner?.Name ?? "(none)"}");
        Log($"Alice life: {alice.LifeTotal}, hand={alice.Zones.Hand.Count}, battlefield={alice.Zones.Battlefield.Count}");
        Log($"Bob life: {bob.LifeTotal}, hand={bob.Zones.Hand.Count}, battlefield={bob.Zones.Battlefield.Count}");
    }

    private static async Task RunPlayFullGame()
    {
        Banner("Scenario: Full game — bot vs bot, multiple turns");

        var bus = new EventBus();
        // Suppress per-event noise (lots of priority events per turn).
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new Majik.Core.Services.StackResolver(bus, zones);
        var sba = new Majik.Core.Rules.StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, bus, triggers);
        var combat = new Majik.Core.Combat.CombatFlow(bus, sba);

        for (var i = 0; i < 30; i++)
        {
            var ac = Majik.Core.CardData.NamedCardFactory.Create("Mountain", alice);
            alice.Zones.Library.AddCard(ac); ac.SetZone(ZoneType.Library);
            var bc = Majik.Core.CardData.NamedCardFactory.Create("Mountain", bob);
            bob.Zones.Library.AddCard(bc); bc.SetZone(ZoneType.Library);
        }

        var driver = new GameDriver(
            players: new[] { alice, bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new DeterministicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack: stack,
            zoneService: zones,
            triggerManager: triggers,
            stackResolver: resolver,
            stateBasedActions: sba,
            priorityManager: priority,
            combatFlow: combat);

        Log("Running until game ends (no turn cap; bots pass priority)…");
        var result = await driver.RunGameAsync(maxTurns: int.MaxValue);

        Log($"Turns played: {result.TurnsPlayed}");
        Log($"Winner: {(result.Winner?.Name ?? "(none — turn cap reached)")}");
        Log($"Alice life: {alice.LifeTotal} hand={alice.Zones.Hand.Count} library={alice.Zones.Library.Count}");
        Log($"Bob life: {bob.LifeTotal} hand={bob.Zones.Hand.Count} library={bob.Zones.Library.Count}");
    }

    private static void RunPlayGameDb()
    {
        Banner("Scenario: Load real Scryfall cards + play one slice");

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Majik", "cards.db");
        if (!File.Exists(dbPath))
        {
            Log($"Scryfall DB missing at {dbPath} — skipping.");
            return;
        }

        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));

        var bus = new EventBus();
        AttachTrace(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var resolver = new Majik.Core.Services.StackResolver(bus, zones);
        var manaResolver = new Majik.Core.Costs.ManaPaymentResolver();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var mountain = factory.Create("Mountain", alice);
        mountain.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(mountain);

        var bolt = factory.Create("Lightning Bolt", alice);
        bolt.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(bolt);

        Log($"Alice life before: {alice.LifeTotal}   Bob life before: {bob.LifeTotal}");
        Log($"Loaded from DB: {mountain.Name} ({mountain.GetType().Name}), {bolt.Name} ({bolt.GetType().Name} cost={bolt.ManaCost})");

        manaResolver.Pay(alice, Majik.Core.ValueObjects.ManaCost.Parse("R"),
            new ManaPayment(new[] { mountain }));
        Log($"Paid R from Mountain. Mountain tapped: {((Permanent)mountain).IsTapped}");

        var castFlow = new SpellCastFlow(stack, zones, bus);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bob });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1, PhaseStateType.Main, stack);
        var def = factory.LookupSpellDefinition("Lightning Bolt", alice, raw => raw, stack)!;
        castFlow.CastAsync(alice, bolt, def, agent, ctx).GetAwaiter().GetResult();

        Log("Bolt on stack — resolving");
        resolver.ResolveTop(stack);
        Log($"Bob life after Bolt resolves: {bob.LifeTotal}");
    }

    private static async Task RunPriorityLoop()
    {
        Banner("Scenario: PriorityLoop driven by bot agents");
        var ctx = NewContext(out var alice, out var bob);

        var warden = MakeAnyEtbLifeGainer("Soul Warden", alice);
        warden.SetZone(ZoneType.Battlefield);
        ctx.Triggers.BindCard(warden);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = alice, Zone = ZoneType.Hand };
        Log("Alice's Soul Warden on battlefield; she 'plays' a Bear via Zone move");
        ctx.Zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);
        Log($"PendingCount after move: {ctx.Triggers.PendingCount}");

        var loop = new PriorityLoop(
            players: new[] { alice, bob },
            priority: ctx.Priority,
            stack: ctx.Stack,
            stackResolver: ctx.Resolver,
            zoneService: ctx.Zones,
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new DeterministicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            turnNumberAccessor: () => 1,
            phaseAccessor: () => PhaseStateType.Main);

        Log("RunUntilRoundEndsAsync — both bots pass, stack drains");
        await loop.RunUntilRoundEndsAsync(alice);
        Log($"Final stack empty: {ctx.Stack.IsEmpty}   Alice life: {alice.LifeTotal}");
    }

    // -------------------------------------------------------------------

    private sealed record Context(
        EventBus Bus,
        Majik.Core.Stack.Stack Stack,
        TriggerManager Triggers,
        ZoneService Zones,
        PriorityManager Priority,
        StackResolver Resolver);

    private static Context NewContext(out Player alice, out Player bob)
    {
        var bus = new EventBus();
        AttachTrace(bus);

        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        alice = new Player("Alice", 20);
        bob = new Player("Bob", 20);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, bus, triggers);
        var resolver = new StackResolver(bus, zones);

        return new Context(bus, stack, triggers, zones, priority, resolver);
    }

    private static Creature MakeAnyEtbLifeGainer(string name, Player controller)
    {
        var card = new Creature(name, "W", 1, 1) { Owner = controller };
        var ability = new TriggeredAbility(
            card, controller,
            Triggers.OnAnyCreatureEntersBattlefield(),
            effects: new IEffect[] { new Effect("gain 1", () => controller.GainLife(1)) });
        card.AddAbility(ability);
        return card;
    }

    private static void AttachTrace(EventBus bus)
    {
        bus.SubscribeAll(e =>
        {
            var detail = e switch
            {
                CardMovedEvent m => $"{m.Card.Name}: {m.FromZone} → {m.ToZone}",
                CardDrawnEvent d => $"{d.Player.Name} drew {d.Card.Name}",
                StackObjectAddedEvent s => $"pushed {Describe(s.StackObject)}",
                StackObjectResolvedEvent s => $"resolved {Describe(s.StackObject)}",
                TriggeredAbilityTriggeredEvent t => $"ability triggered (ctrl={t.Ability.Controller.Name})",
                TriggeredAbilityCounteredEvent c => $"ability COUNTERED — {c.Reason}",
                PriorityReceivedEvent p => $"priority → {p.Player.Name}",
                _ => string.Empty,
            };
            var tag = e.GetType().Name;
            System.Console.WriteLine(detail.Length == 0
                ? $"    [event] {tag}"
                : $"    [event] {tag}: {detail}");
        });
    }

    private static string Describe(object obj) => obj switch
    {
        ITriggeredAbility t => $"trigger({t.Controller.Name})",
        _ => obj.GetType().Name,
    };

    private const string BorosEnergyDeck = @"
4 Ajani, Nacatl Pariah
4 Guide of Souls
4 Ocelot Pride
4 Phlage, Titan of Fire's Fury
4 Ragavan, Nimble Pilferer
3 Seasoned Pyromancer
3 Voice of Victory
4 Galvanic Discharge
2 Thraben Charm
2 Erode
3 Goblin Bombardment
3 Arena of Glory
3 Sacred Foundry
3 Plains
2 Elegant Parlor
3 Marsh Flats
4 Flooded Strand
4 Arid Mesa
1 Mountain";

    private const string EldraziAffinityDeck = @"
1 Haywire Mite
4 Kappa Cannoneer
4 Pinnacle Emissary
3 Emry, Lurker of the Loch
4 Claws of Gix
1 Skateboard
4 Engineered Explosives
4 Mishra's Bauble
3 Tormod's Crypt
4 Mox Opal
1 Shadowspear
1 Welding Jar
3 Metallic Rebuke
4 Weapons Manufacturing
4 Urza's Saga
1 Thundering Falls
4 Polluted Delta
3 Steam Vents
1 Breeding Pool
1 Scalding Tarn
4 Flooded Strand
1 Island";

    private static async Task RunModernFaceoff()
    {
        Banner("Scenario: Modern face-off — Boros Energy vs Eldrazi/Affinity");

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Majik", "cards.db");
        if (!File.Exists(dbPath))
        {
            Log($"Scryfall DB missing at {dbPath} — skipping.");
            Log("Run `dotnet run --project Majik.Console -- import <scryfall-all-cards.json>` first.");
            return;
        }

        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var aliceStats = BuildDeck(factory, alice, BorosEnergyDeck);
        var bobStats = BuildDeck(factory, bob, EldraziAffinityDeck);

        Log($"Alice (Boros Energy): {aliceStats.total} cards, {aliceStats.bound} bound, {aliceStats.vanilla} vanilla shells");
        if (aliceStats.missing.Count > 0)
            Log($"  Not in DB: {string.Join(", ", aliceStats.missing.Distinct())}");
        Log($"Bob   (Eldrazi-Affinity): {bobStats.total} cards, {bobStats.bound} bound, {bobStats.vanilla} vanilla shells");
        if (bobStats.missing.Count > 0)
            Log($"  Not in DB: {string.Join(", ", bobStats.missing.Distinct())}");

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new Majik.Core.Services.StackResolver(bus, zones);
        var sba = new Majik.Core.Rules.StateBasedActions(bus, zones, triggers);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, bus, triggers);
        var combat = new Majik.Core.Combat.CombatFlow(bus, sba);

        // SpellDefinition resolver: ScryfallCardFactory hits the oracle
        // binder for the cast card and returns a real definition when the
        // card's text matches a known template. Null = vanilla fallback.
        Majik.Core.Game.SpellDefinition? SpellResolver(
            ICard card, Player caster, Majik.Core.Stack.Stack? stk)
            => factory.LookupSpellDefinition(card.Name, caster, raw => raw, stk);

        var driver = new GameDriver(
            players: new[] { alice, bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new HeuristicBotAgent(new RuntimeFlashbackAltCostProbe()),
                [bob] = new HeuristicBotAgent(new RuntimeFlashbackAltCostProbe()),
            },
            stack: stack,
            zoneService: zones,
            triggerManager: triggers,
            stackResolver: resolver,
            stateBasedActions: sba,
            priorityManager: priority,
            combatFlow: combat,
            eventBus: bus,
            spellDefinitionResolver: SpellResolver);

        // Per-event log → /tmp/majik-faceoff.log. Filter to high-signal
        // events; full event stream is too noisy for manual reading.
        var logPath = Path.Combine(Path.GetTempPath(), "majik-faceoff.log");
        await using var logFile = new StreamWriter(logPath) { AutoFlush = true };
        bus.SubscribeAll(e =>
        {
            string? line = e switch
            {
                Majik.Core.Events.TurnStartedEvent t =>
                    $"\n=== Turn {t.TurnNumber} — {t.Player.Name} ===  "
                    + $"life A={alice.LifeTotal} B={bob.LifeTotal}  "
                    + $"bf A={alice.Zones.Battlefield.Count} B={bob.Zones.Battlefield.Count}",
                Majik.Core.Events.CardDrawnEvent d =>
                    $"  draw  {d.Player.Name} → {d.Card.Name}",
                Majik.Core.Events.CardMovedEvent m when m.ToZone == ZoneType.Battlefield =>
                    $"  enters battlefield  {m.Card.Name} (controller {GuessController(m.Card)})",
                Majik.Core.Events.CardMovedEvent m when m.ToZone == ZoneType.Graveyard
                                                     && m.FromZone == ZoneType.Battlefield =>
                    $"  dies/goes to gy  {m.Card.Name}",
                Majik.Core.Domain.DomainEvents.SpellCastEvent c =>
                    $"  cast  {c.Spell.Controller.Name}: {c.Spell.Card.Name}",
                Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent cd =>
                    $"  combat damage  {cd.Source.Name} → "
                    + (cd.TargetPlayer?.Name ?? cd.Target?.Name ?? "?")
                    + $"  for {cd.Amount}",
                Majik.Core.Domain.DomainEvents.StateBasedActionExecutedEvent s =>
                    $"  SBA  {s.ActionDescription}",
                Majik.Core.Events.PlayerLostEvent l =>
                    $"  *** {l.Player.Name} LOSES ***",
                _ => null,
            };
            if (line != null) logFile.WriteLine(line);
        });

        Log($"Per-event log → {logPath}");
        Log("Running game (no turn cap — bot vs bot)…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await driver.RunGameAsync(maxTurns: int.MaxValue);
        sw.Stop();
        Log($"Wall time: {sw.Elapsed.TotalSeconds:F2}s for {result.TurnsPlayed} turns");

        Log($"Turns played: {result.TurnsPlayed}");
        Log($"Winner: {(result.Winner?.Name ?? "(none — draw or stalled)")}");
        DumpPlayer("Alice", alice);
        DumpPlayer("Bob", bob);
        Log($"Full action log: {logPath}");
    }

    private static string GuessController(ICard card) =>
        (card as Majik.Core.Cards.Permanent)?.Controller?.Name ?? card.Owner?.Name ?? "?";

    private sealed record DeckStats(int total, int bound, int vanilla, List<string> missing);

    private static DeckStats BuildDeck(ScryfallCardFactory factory, Player owner, string decklist)
    {
        var missing = new List<string>();
        int total = 0, bound = 0, vanilla = 0;
        foreach (var rawLine in decklist.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var space = line.IndexOf(' ');
            if (space < 0 || !int.TryParse(line[..space], out var count)) continue;
            var name = line[(space + 1)..].Trim();
            for (var i = 0; i < count; i++)
            {
                var card = factory.Create(name, owner);
                if (card.GetType() == typeof(Card))
                {
                    vanilla++;
                    missing.Add(name);
                }
                else
                {
                    bound++;
                }
                total++;
                card.SetZone(ZoneType.Library);
                owner.Zones.Library.AddCard(card);
            }
        }
        return new DeckStats(total, bound, vanilla, missing);
    }

    private static void DumpPlayer(string label, Player p)
    {
        Log($"{label}: life={p.LifeTotal} hand={p.Zones.Hand.Count} library={p.Zones.Library.Count} graveyard={p.Zones.Graveyard.Count} battlefield={p.Zones.Battlefield.Count}");
    }

    private static void Banner(string title)
    {
        System.Console.WriteLine();
        System.Console.WriteLine(new string('=', title.Length + 4));
        System.Console.WriteLine($"  {title}");
        System.Console.WriteLine(new string('=', title.Length + 4));
    }

    private static void Log(string msg) => System.Console.WriteLine("> " + msg);
}
