using Majik.Bot.Heuristic;
using Majik.Bot.Search;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot;

/// <summary>
/// IPlayerAgent implementation that dispatches every prompt through an
/// IBotStrategy. v1 ships HeuristicStrategy chosen via BotConfig.Strategy.
/// </summary>
public sealed class BotPlayerAgent : IPlayerAgent
{
    private readonly Player _self;
    private readonly IBotStrategy _strategy;
    private readonly Action<bool>? _onThinking;

    public BotPlayerAgent(Player self, BotConfig config, Action<bool>? onThinking = null)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _onThinking = onThinking;
        // "frozen-fb1" — FB1, the frozen-baseline ladder's permanent reference
        // opponent: a byte-identical snapshot of the live heuristic vendored
        // under Majik.Bot.Frozen.FB1, cut 2026-06-12 at commit 38547ffb3.
        // Maintenance contract: FB1 may be patched ONLY mechanically for engine
        // API renames (behavior-preserving); FB1CharacterizationTests pins its
        // decisions and fails loudly on any behavioral drift. When FB1 is
        // consistently stomped, cut FB2 alongside it — old rungs stay.
        _strategy = config.Strategy switch
        {
            "heuristic"  => new HeuristicStrategy(config),
            "mcts"       => new SearchStrategy(config),
            "frozen-fb1" => new Majik.Bot.Frozen.FB1.HeuristicStrategy(config),
            _ => throw new ArgumentException($"Unknown strategy: {config.Strategy}", nameof(config)),
        };
    }

    /// <summary>
    /// Test seam: the strategy instance the ctor switch installed. Lets the
    /// wiring tests assert the <c>BotConfig.Strategy</c> → implementation
    /// mapping without reflection. Internal — not part of the public surface.
    /// </summary>
    internal IBotStrategy InstalledStrategy => _strategy;

    /// <summary>
    /// Internal test-seam constructor. Accepts a prebuilt <see cref="IBotStrategy"/>
    /// so probes can inject a custom <see cref="Search.SearchStrategy"/> (built with
    /// an explicit deck-strategy override) without going through the config path.
    /// This is the correct injection point for controlled experiments that must
    /// isolate a single variable (deck strategy ON vs OFF) while keeping all other
    /// strategy configuration identical.
    /// </summary>
    internal BotPlayerAgent(Player self, IBotStrategy strategy, Action<bool>? onThinking = null)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _onThinking = onThinking;
    }

    /// <summary>
    /// Wraps a synchronous policy call with the optional thinking callback.
    /// Fires <c>onThinking(true)</c> before, <c>onThinking(false)</c> after.
    /// Observer exceptions are swallowed so a faulty subscriber cannot abort
    /// the engine.
    /// </summary>
    private Task<T> WrapAsync<T>(Func<T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { _onThinking?.Invoke(true); }
        catch { /* observer fault must not abort engine */ }
        try
        {
            return Task.FromResult(work());
        }
        finally
        {
            try { _onThinking?.Invoke(false); }
            catch { /* observer fault must not abort engine */ }
        }
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickPriorityAction(ctx, _self), ct);

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMulligan(hand, mulligansTaken), ct);

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickCardsToBottom(hand, countToBottom), ct);

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickTargets(ctx, _self, request), ct);

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => WrapAsync(() => PickXForSource(ctx, source), ct);

    /// <summary>
    /// GAP 2 — X policy. SPELLS keep the legacy <see cref="IBotStrategy.PickX"/>
    /// posture (land count) UNCHANGED, so existing decks' flip / masking
    /// decisions don't move. A variable-X ACTIVATED ABILITY (its source is a
    /// permanent we control on the battlefield) instead gets a sensible non-zero
    /// default that is always LEGAL — clamped to the mana actually available
    /// (floating pool + untapped sources, minus the cost's fixed pips) so the
    /// expanded {X} payment can't fail. Simple, not optimal:
    /// <list type="bullet">
    ///   <item>Tameshi, Reality Architect — X = mana value of the best
    ///   artifact/enchantment card in our graveyard, clamped to affordable.</item>
    ///   <item>Steel Hellkite / Lair of the Hydra / any other {X} ability —
    ///   X = the affordable amount (a non-zero useful size / sweep mv).</item>
    /// </list>
    /// </summary>
    private int PickXForSource(GameContext ctx, ICard source)
    {
        // Only battlefield permanents WE control are activated-ability X sources;
        // spells (in hand / on the stack) keep the legacy policy untouched so the
        // bot's existing spell-X decisions (and the flip / masking surface that
        // depends on them) are unchanged.
        if (source is not Majik.Core.Cards.Permanent perm
            || perm.Zone != Majik.Core.Zones.ZoneType.Battlefield
            || !ReferenceEquals(perm.Controller, _self))
        {
            return _strategy.PickX(ctx, _self);
        }

        // Affordable X = total available mana minus the {X} ability's fixed
        // (non-X) pips. The variable-X ability is the one whose ManaCostCost has
        // HasX; fall back to PickX when none is found (defensive).
        var xAbility = perm.Abilities
            .OfType<Majik.Core.Abilities.ActivatedAbility>()
            .FirstOrDefault(a => a.Costs
                .OfType<Majik.Core.Costs.ManaCostCost>()
                .Any(m => m.Cost.HasX));
        if (xAbility is null) return _strategy.PickX(ctx, _self);

        var fixedPips = xAbility.Costs
            .OfType<Majik.Core.Costs.ManaCostCost>()
            .Where(m => m.Cost.HasX)
            .Sum(m => m.Cost.TotalValue); // {X} contributes 0; counts the {W}/{G}/… base.
        var available = Majik.Bot.Search.LegalActionEnumerator.UntappedManaSources(_self);
        var affordableX = Math.Max(0, available - fixedPips);

        // Tameshi — aim X at the best reanimation target's mana value.
        if (string.Equals(source.Name, "Tameshi, Reality Architect", StringComparison.Ordinal))
        {
            var bestTargetMv = _self.Zones.Graveyard.GetCards()
                .Where(c => c.HasType(Majik.Core.Cards.Types.CardType.Artifact)
                            || c.HasType(Majik.Core.Cards.Types.CardType.Enchantment))
                .Select(c => Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost).TotalValue)
                .DefaultIfEmpty(0)
                .Max();
            return Math.Min(affordableX, bestTargetMv);
        }

        // Steel Hellkite / Lair of the Hydra / generic {X} ability — spend the
        // affordable amount (a non-zero sweep mv / creature size).
        return affordableX;
    }

    public Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMode(ctx, _self, modes), ct);

    /// <summary>
    /// CR 614.12 / CR 201.4 — "choose a card name" heuristic. The engine
    /// surveys the chooser's known information and hands us a
    /// <paramref name="suggested"/> pool ranked most-threatening-first (cards
    /// the opponent has visible on the battlefield / stack / revealed hand).
    /// The simple, robust posture: name the TOP-ranked suggested card — the
    /// most-threatening known name, which is exactly what a name-a-card hate
    /// piece (Meddling Mage / Pithing Needle / Sanctum Prelate) wants to shut
    /// off. When the engine surfaced no known threats we fall back to the
    /// supplied <paramref name="fallback"/> (the pre-agent inert default — name
    /// nothing rather than guess blindly), which keeps existing bot games byte-
    /// identical on boards with no visible opposing cards.
    /// </summary>
    public Task<string> ChooseCardNameAsync(
        GameContext? ctx,
        IReadOnlyList<string> suggested,
        string constraintLabel,
        string fallback = "",
        CancellationToken ct = default)
        => WrapAsync(() =>
            suggested is { Count: > 0 } pool ? pool[0] : fallback, ct);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => WrapAsync(() => _strategy.OrderTriggers(ctx, mine), ct);

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMana(ctx, _self, cost), ct);

    // Deferral animated-noncreature-as-combatant (4B) — the eligible lists are
    // now Permanent-typed; IBotStrategy still keys off Creature, so project to
    // the real-Creature subset (the heuristic strategy doesn't proactively swing
    // animated manlands in v1 — the live engine still allows it).
    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickAttackers(ctx, _self, eligibleAttackers.OfType<Creature>().ToList()), ct);

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickBlockers(ctx, _self, attackers.OfType<Creature>().ToList(), eligibleBlockers.OfType<Creature>().ToList()), ct);

    public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickScry(ctx, _self, peeked), ct);

    public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickSurveil(ctx, _self, peeked), ct);

    public Task<ICard?> ChooseLibraryPickAsync(GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickLibraryCard(ctx, _self, candidates, kindLabel), ct);

    /// <summary>
    /// CR 117.x / 605.1 — wire-shaped Yes/No prompt. Heuristic posture:
    /// always accept. Shock-land "pay 2 life to enter untapped?" is the
    /// only current caller and bots want to curve out, so paying is the
    /// strictly better choice in nearly every game state (the alternative
    /// is a tapped land, which delays the next-turn curve). Smarter
    /// per-context overrides can land later (e.g. decline at low life
    /// or under specific aggro pressure); the simple "yes" baseline keeps
    /// the bot's mana on schedule and matches the way real ladder players
    /// play untapped shocks by default.
    /// </summary>
    public Task<bool> ChooseYesNoAsync(
        GameContext? ctx,
        string question,
        string? sourceCardName,
        CancellationToken ct = default)
        => WrapAsync(() => true, ct);

    /// <summary>
    /// PLAN 01 (Slice C) — declarative choice sink. Yes/No routes through this
    /// bot's wire Yes/No posture (always accept); PickOne/PickN return the
    /// first candidate(s) (or decline when optional with no candidates),
    /// matching the bot's first-pick posture on bespoke pick prompts.
    /// </summary>
    public Task<IReadOnlyList<object>> ChooseAsync(
        GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        => WrapAsync<IReadOnlyList<object>>(() =>
        {
            var candidates = req.Candidates ?? Array.Empty<object>();

            // CR 712.3 — MDFC face prompt: route the face choice through
            // MdfcFacePolicy (deliberate land-vs-spell pick) instead of the
            // first-candidate default, which always picks the front face and so
            // leaves MDFC-land hands mana-locked (Belcher trace, 2026-06-12).
            if (MdfcFacePolicy.TryPick(ctx, _self, candidates, out var face))
                return new object[] { face };

            if (req.Kind == ChoiceKind.YesNo)
            {
                // Bot always accepts (mirrors the wire Yes/No posture above).
                return candidates.Count > 0 ? new[] { candidates[0] } : new object[] { true };
            }
            if (req.Optional && candidates.Count == 0)
                return Array.Empty<object>();
            var take = Math.Max(req.Min, candidates.Count > 0 ? 1 : 0);
            return candidates.Take(Math.Min(take, candidates.Count)).ToList();
        }, ct);
}
