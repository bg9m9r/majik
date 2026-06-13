using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.Tests.Combo;

/// <summary>
/// Phase B2 — a two-player <see cref="GameFacade"/> harness for the combo-line
/// engine-correctness tests (plan 2026-06-13). It stacks the Belcher seat's
/// library deterministically (the opening hand is the TOP N cards — the London
/// mulligan draws from the library top and the line agent always Keeps), runs a
/// passive opponent (<see cref="Majik.Core.Players.Agents.DeterministicBotAgent"/>),
/// drives the Belcher seat with a <see cref="ScriptedLineAgent"/>, and runs the
/// real engine to the kill or a turn cap.
///
/// <para>Determinism: a seeded <see cref="GameRandom"/> + a fixed library order
/// make every run reproducible. The opponent never acts, so the only agency in
/// the game is the scripted line; if the line reaches an unscripted prompt the
/// scripted agent throws and the test fails loudly.</para>
/// </summary>
public sealed class ComboLineHarness
{
    private static readonly EmbeddedCardRepository Repo = new();

    public GameFacade Facade { get; }
    public Player Belcher { get; }
    public Player Opponent { get; }
    public ScriptedLineAgent Line { get; }

    private ComboLineHarness(GameFacade facade, ScriptedLineAgent line)
    {
        Facade = facade;
        Belcher = facade.Alice;
        Opponent = facade.Bob;
        Line = line;
    }

    /// <summary>
    /// Build the harness. <paramref name="belcherLibraryOrder"/> is the Belcher
    /// seat's full library top → bottom; the first 7 become the opening hand
    /// (London draw from the top, line Keeps). <paramref name="opponentLife"/>
    /// sets a low life total so a kill is observable inside the turn cap.
    /// <paramref name="battlefield"/> names are built and placed directly on the
    /// Belcher battlefield BEFORE the game starts (e.g. a resolved Lotus Bloom
    /// or a pre-deployed Charbelcher) — these are NOT drawn.
    /// </summary>
    public static ComboLineHarness Build(
        IReadOnlyList<string> belcherLibraryOrder,
        ScriptedLineAgent line,
        int opponentLife = 20,
        IReadOnlyList<string>? battlefield = null,
        IReadOnlyList<string>? opponentLibrary = null)
    {
        // The opponent runs a vanilla, non-combo deck of basic Islands so it
        // never does anything interesting and never decks out inside the cap.
        var oppNames = opponentLibrary ?? Enumerable.Repeat("Island", 40).ToList();

        var belcherShells = belcherLibraryOrder
            .Select(BuildShell).ToList();
        var oppShells = oppNames.Select(BuildShell).ToList();

        var facade = GameFacade.Create(
            aliceName: "Belcher",
            bobName: "Opponent",
            aliceDeck: belcherShells,
            bobDeck: oppShells,
            cardRepo: Repo);

        // Lower the opponent's life so the burst kill is observable within the
        // small turn cap (the deck normally burns the whole library, but the
        // harness library is small).
        SetLife(facade.Bob, opponentLife);

        facade.ReplaceAliceAgent(line);
        facade.ReplaceBobAgent(new Majik.Core.Players.Agents.DeterministicBotAgent());

        var harness = new ComboLineHarness(facade, line);

        if (battlefield is { Count: > 0 })
        {
            foreach (var name in battlefield)
            {
                harness.PlaceOnBattlefield(name);
            }
        }

        return harness;
    }

    /// <summary>
    /// Build a live card and place it on the Belcher battlefield before the
    /// game starts (pre-resolved combo pieces — Charbelcher already cast, Lotus
    /// Bloom suspend resolved, an Island for mana). Returns the live card.
    /// </summary>
    public ICard PlaceOnBattlefield(string name)
    {
        // Build through the SAME deck-build path the facade uses (routed
        // through named factories so e.g. Charbelcher carries its real
        // activated ability), wired to the facade's live services.
        var live = DeckCardBuilder.BuildFromShell(
            shell: BuildShell(name),
            owner: Belcher,
            cardRepo: Repo,
            replacements: Facade.Replacements,
            effects: Facade.ContinuousEffects,
            routeThroughNamedFactories: true,
            triggers: Facade.Triggers,
            zones: null,
            eventBus: Facade.EventBus);
        live.SetOwner(Belcher);
        live.SetController(Belcher);
        live.SetZone(ZoneType.Battlefield);
        Belcher.Zones.GetZone(ZoneType.Battlefield).AddCard(live);
        return live;
    }

    /// <summary>
    /// Run the full game to the turn cap (or a player loss). Returns the
    /// <see cref="GameDriver.GameResult"/>. Belcher is on the play (slot 0).
    /// </summary>
    public async Task<GameDriver.GameResult> RunAsync(int maxTurns = 6, int seed = 1234)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await Facade.StartFullGameAsync(
            firstPlayerSlot: 0,
            maxTurns: maxTurns,
            ct: cts.Token,
            rng: new GameRandom(seed));
        return await Facade.FullGameTask!;
    }

    // -----------------------------------------------------------------------
    // Scripted-line step builders (shared across B3 / B4 lines)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Script "tap an untapped permanent named <paramref name="sourceName"/> for
    /// mana" (CR 605 — mana ability, implicit hold-priority). Picks the first
    /// untapped matching source with a mana ability at execution time.
    /// </summary>
    public ComboLineHarness TapForMana(string sourceName)
    {
        Line.Then(ctx =>
        {
            var src = ctx.Self.Zones.Battlefield.GetCards()
                .First(c => c.Name == sourceName
                    && c is Permanent p && !p.IsTapped
                    && c.Abilities.OfType<IManaAbility>().Any());
            var mana = src.Abilities.OfType<IManaAbility>().First();
            return new PriorityAction.ActivateManaAbility(src, mana);
        });
        return this;
    }

    /// <summary>
    /// Script the Goblin Charbelcher belch ({3},{T}) targeting the opponent.
    /// The {3} must already be floated (TapForMana) — the engine's activated-
    /// ability dispatch pays the mana cost from the FLOATING pool only.
    /// </summary>
    public ComboLineHarness ActivateCharbelcher()
    {
        Line.Then(ctx =>
        {
            var belcher = ctx.Self.Zones.Battlefield.GetCards()
                .Single(c => c.Name == "Goblin Charbelcher");
            var belch = belcher.Abilities.OfType<IActivatedAbility>().Single();
            return new PriorityAction.ActivateAbility(
                belch, new object[] { ctx.Opponents[0] });
        });
        return this;
    }

    private static ICard BuildShell(string name) =>
        DeckCardShellBuilder.Build(
            Repo.GetByName(name)
            ?? throw new InvalidOperationException($"'{name}' not in embedded seed"));

    private static void SetLife(Player p, int life)
    {
        // Player.LifeTotal has no public setter; adjust via the damage/gain
        // surface. Players start at 20; nudge to the target.
        var delta = life - p.LifeTotal;
        if (delta < 0) p.LoseLife(-delta);
        else if (delta > 0) p.GainLife(delta);
    }
}
