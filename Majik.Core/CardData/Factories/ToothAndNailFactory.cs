using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tooth and Nail (Mirrodin, {5}{G}{G}).
///
/// Sorcery. Oracle text:
///   "Entwine {2}{G}{G}.
///    Choose one —
///     • Search your library for up to two creature cards, reveal them,
///       put them into your hand, then shuffle.
///     • Put up to two creature cards from your hand onto the battlefield."
///
/// CR 700.2d — modal "Choose one —" with two modes; CR 702.41 — entwine
/// is an additional cost that lets the caster choose both modes when
/// paid (CR 700.2e). The premier Modern-era "win with two creatures
/// onto the battlefield" finisher: Mindslaver + Inkwell Leviathan,
/// Emrakul + something, Eldrazi titan packages, etc.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {5}{G}{G}.
/// - Modal "Choose one —" with two modes (CR 700.2d). Multi-pick honoured
///   via <see cref="ChosenSpellParams.ModeIndexes"/> (entwine path
///   supplies both indices); legacy single-pick honoured via
///   <see cref="ChosenSpellParams.ModeIndex"/>. CR 700.2d caps each mode
///   at one selection.
/// - <b>Mode 0 — tutor up to two creature cards to hand</b>: prompts the
///   controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for the first
///   creature card, then again for the second (excluding the first
///   pick). Each pick is optional ("up to two" — agent may decline).
///   Picked cards move Library → Hand. The library is shuffled once at
///   the end via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a — a single search effect performs one shuffle, even
///   when zero or one card is found). Deterministic first-match
///   fallback when no agent is registered (mirrors
///   <see cref="MastermindsAcquisitionFactory"/> /
///   <see cref="EladamrisCallFactory"/>).
/// - <b>Mode 1 — put up to two creature cards from hand onto the
///   battlefield</b>: prompts the controller's agent for the first
///   creature card from hand, then again for the second (excluding the
///   first pick). Each pick is optional. Routes through
///   <see cref="ZoneService.MoveCard"/> when a <see cref="ZoneService"/>
///   is registered via <see cref="ZoneServiceRegistry"/> so ETB triggers
///   fire (CR 603.6a) — Emrakul's annihilator, Inkwell Leviathan's
///   shroud, etc. Falls back to raw zone mutation when no service is
///   wired (shape-only test path).
///
/// ## Deferred (v1 gaps)
/// - <b>Entwine additional cost</b>: <see cref="EntwineAdditionalCost"/>
///   does not exist in the engine yet. Until then, the modal definition
///   exposes both modes via <see cref="SpellDefinition.Modes"/> and the
///   <see cref="EffectFactory"/> honours
///   <see cref="ChosenSpellParams.ModeIndexes"/> for both-mode resolves,
///   but the cost flow does NOT automatically charge {2}{G}{G} extra
///   when the caster opts into both modes — the caller is responsible
///   for stapling the additional cost when bumping the pick count past
///   one. This is the same gap as every other entwine card in the
///   codebase (zero shipped at v1).
/// - <b>Reveal event</b>: mode 0 moves picks Library → Hand without
///   publishing a <c>CardRevealedEvent</c> — same gap as every other
///   tutor factory (Eladamri's Call / Mystical Tutor).
/// - <b>Mode 1 sorcery-speed</b>: not separately enforced — Tooth and
///   Nail is itself a sorcery, so the spell-speed gate at cast time
///   already restricts mode 1 to sorcery speed.
/// - <b>Mode 1 creature-card-type gate</b>: the picker filters to
///   <c>HasType(CardType.Creature)</c> at resolve time. A card that
///   stops being a creature in hand (none currently in the engine)
///   would be filtered out correctly.
/// </summary>
[CardName("Tooth and Nail")]
public static class ToothAndNailFactory
{
    public const string CardName = "Tooth and Nail";
    public const string PrintedManaCost = "{5}{G}{G}";

    /// <summary>Entwine additional cost (CR 702.41). Not enforced in v1
    /// — see class header.</summary>
    public const string EntwineAdditionalCost = "{2}{G}{G}";

    /// <summary>Mode 0 — tutor up to two creature cards to hand.</summary>
    public const int ModeTutor = 0;
    /// <summary>Mode 1 — put up to two creature cards from hand onto the battlefield.</summary>
    public const int ModeReanimateFromHand = 1;

    /// <summary>CR 700.2d — printed "Choose one —" pick count (without entwine).</summary>
    public const int PickCount = 1;
    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;
    /// <summary>Maximum creatures per mode ("up to two").</summary>
    public const int MaxCreaturesPerMode = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Search your library for up to two creature cards, reveal them, put them into your hand, then shuffle.",
        "Put up to two creature cards from your hand onto the battlefield.",
    };

    /// <summary>
    /// Construct Tooth and Nail as a Sorcery owned by
    /// <paramref name="owner"/>. Card shape only — the resolve body is
    /// produced by <see cref="BuildDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Tooth and Nail. Both
    /// modes are wired. No target requests — both modes resolve via
    /// internal pickers (library agent prompt + hand agent prompt).
    /// </summary>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            // CR 601.2c — no per-mode target requests; both modes resolve
            // their picker against zones the caster owns (library / hand)
            // at resolve time without a cast-time target list.
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Tutor,         // library tutor — strict card advantage
                BotIntent.CheatIntoPlay, // hand → battlefield, classic finisher mode
            },
            EffectFactory: p => BuildEffectsForModes(p, caster));
    }

    private static IReadOnlyList<IEffect> BuildEffectsForModes(ChosenSpellParams p, Player caster)
    {
        // Honor either the multi-pick list (entwine path supplies both
        // indices) or the legacy scalar ModeIndex.
        var indices = p.ModeIndexes is { Count: > 0 } list
            ? list
            : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

        var effectsOut = new List<IEffect>();
        var seen = new HashSet<int>();
        foreach (var raw in indices)
        {
            if (raw < 0 || raw >= TotalModes) continue;
            if (!seen.Add(raw)) continue; // CR 700.2d — each mode at most once
            // Note: no PickCount cap here. Entwine (CR 702.41 / 700.2e)
            // lets both modes resolve when the additional cost is paid.
            var effect = BuildEffectForMode(raw, caster);
            if (effect != null) effectsOut.Add(effect);
        }
        return effectsOut;
    }

    private static IEffect? BuildEffectForMode(int modeIndex, Player caster) => modeIndex switch
    {
        ModeTutor => BuildTutorEffect(caster),
        ModeReanimateFromHand => BuildReanimateFromHandEffect(caster),
        _ => null,
    };

    /// <summary>
    /// Mode 0 effect: prompt the caster's agent for up to two creature
    /// cards from the library, move them to hand, then shuffle once.
    /// CR 701.19a + CR 701.20a.
    /// </summary>
    private static IEffect BuildTutorEffect(Player caster) =>
        new Effect("Tooth and Nail — search library for up to two creatures", () =>
        {
            // CR 701.19a — library search. Use LibrarySearch.PromptOnly so
            // the first prompt fires even with zero candidates (a human
            // searcher must SEE the failed search rather than the spell
            // silently no-opping).
            var picks = new List<ICard>(capacity: MaxCreaturesPerMode);
            for (var slot = 0; slot < MaxCreaturesPerMode; slot++)
            {
                var candidates = caster.Zones.Library.GetCards()
                    .Where(IsCreature)
                    .Where(c => !picks.Contains(c))
                    .ToList();
                // First slot always prompts (even when empty); subsequent
                // slots short-circuit on empty candidates since the player
                // has already acknowledged the search.
                if (candidates.Count == 0 && slot > 0) break;

                var pick = Majik.Core.Zones.LibrarySearch.PromptOnly(
                    caster, candidates, "creature card");
                if (pick == null) break; // CR 701.19a — decline / nothing found.
                picks.Add(pick);
            }

            foreach (var pick in picks)
            {
                caster.Zones.Library.RemoveCard(pick);
                caster.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }

            // CR 701.20a — shuffle once after the search effect resolves,
            // even when zero / one card was found.
            LibraryShuffle.ShuffleLibrary(caster, "tooth-and-nail/tutor");
        });

    /// <summary>
    /// Mode 1 effect: prompt the caster's agent for up to two creature
    /// cards from hand, put them onto the battlefield under the caster's
    /// control. CR 603.6a — routed through <see cref="ZoneService"/>
    /// (when registered) so ETB triggers fire.
    /// </summary>
    private static IEffect BuildReanimateFromHandEffect(Player caster) =>
        new Effect("Tooth and Nail — put up to two creatures from hand onto the battlefield", async ctx =>
        {
            var picks = await PickUpToTwoCreaturesAsync(caster.Zones.Hand, caster,
                "creature card from hand to put onto the battlefield", ctx).ConfigureAwait(false);
            PutCreaturesOntoBattlefield(picks, caster);
        });

    // --- Picker / mover helpers -------------------------------------------

    private static bool IsCreature(ICard c) => c.HasType(CardType.Creature);

    private static async ValueTask<List<ICard>> PickUpToTwoCreaturesAsync(IZone zone, Player caster, string kindLabel, ResolutionContext ctx)
    {
        var picks = new List<ICard>(capacity: MaxCreaturesPerMode);
        var agent = ctx.Agent ?? AgentRegistry.Get(caster);

        // First pick — agent may decline (return null) for "up to two".
        var first = await PickOneAsync(zone, agent, kindLabel, excluded: null, ctx).ConfigureAwait(false);
        if (first != null) picks.Add(first);

        // Second pick — exclude the first.
        if (picks.Count > 0)
        {
            var second = await PickOneAsync(zone, agent, kindLabel, excluded: picks[0], ctx).ConfigureAwait(false);
            if (second != null) picks.Add(second);
        }
        return picks;
    }

    private static async ValueTask<ICard?> PickOneAsync(IZone zone, IPlayerAgent? agent, string kindLabel, ICard? excluded, ResolutionContext ctx)
    {
        var candidates = zone.GetCards()
            .Where(c => IsCreature(c) && (excluded == null || !ReferenceEquals(c, excluded)))
            .ToList();
        if (candidates.Count == 0) return null;

        if (agent == null) return candidates[0];
        return await agent.ChooseLibraryPickAsync(ctx.Game, candidates, kindLabel)
            .ConfigureAwait(false);
    }

    private static void PutCreaturesOntoBattlefield(List<ICard> picks, Player caster)
    {
        var zones = ZoneServiceRegistry.Get(caster);
        foreach (var pick in picks)
        {
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Hand, ZoneType.Battlefield, caster);
            }
            else
            {
                caster.Zones.Hand.RemoveCard(pick);
                caster.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
            }
        }
    }
}
