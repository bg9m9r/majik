namespace Majik.Core.Cards;

/// <summary>
/// Strategic intent attached to a spell template, modal-spell mode, or
/// target request. Read by <c>HeuristicBotAgent</c> to score and pick
/// without parsing oracle text. Flags compose for spells whose effect
/// crosses categories (e.g. Searing Blaze = Burn | CombatTrick).
///
/// Source of truth: <c>ISpellTemplate.Intent</c>. Persisted on
/// <c>CompiledSpellTemplateEntity.Intent</c>. Bubbled through
/// <c>SpellDefinition.ModeIntents</c> and <c>TargetRequest.Intent</c>.
///
/// See <c>docs/superpowers/specs/2026-05-22-bot-intent-classifier-design.md</c>
/// for the per-directory template→intent mapping table.
/// </summary>
[Flags]
public enum BotIntent : ulong
{
    None        = 0,

    Removal     = 1UL << 0,
    Burn        = 1UL << 1,
    Counter     = 1UL << 2,
    Bounce      = 1UL << 3,
    Discard     = 1UL << 4,
    Wrath       = 1UL << 5,

    Buff        = 1UL << 6,
    CombatTrick = 1UL << 7,
    Protection  = 1UL << 8,
    Heal        = 1UL << 9,

    Draw        = 1UL << 10,
    Tutor       = 1UL << 11,
    Cantrip     = 1UL << 12,
    Ramp        = 1UL << 13,

    Mill        = 1UL << 14,
    Reanimate   = 1UL << 15,
    Token       = 1UL << 16,

    Reach       = 1UL << 17,

    // ---- Agent-prompt sub-intents (ChooseYesNoAsync / ChooseFromHandAsync) ----
    // Coarse-grained classifiers used by HeuristicBotAgent to decide
    // optional-action prompts ("may" clauses) without parsing oracle text.
    // Independent flag bits so a single prompt can compose multiple
    // qualifiers (e.g. CardAdvantage | LoseLife for a "draw 2, lose 2 life"
    // optional rider).

    /// <summary>Optional action that nets +1 or more cards (draw, tutor,
    /// create-Clue, recur from grave).</summary>
    CardAdvantage = 1UL << 18,

    /// <summary>Optional action whose downside is life loss to the actor.
    /// Heuristic bot declines by default.</summary>
    LoseLife      = 1UL << 19,

    /// <summary>Optional action whose cost is a discard. Heuristic bot
    /// declines when hand is otherwise valuable.</summary>
    DiscardCost   = 1UL << 20,

    /// <summary>"Unless you pay X" rider (Esper Sentinel / Daze / Mana Leak).
    /// Heuristic bot pays only when affordable and the tax is small.</summary>
    CostToDecline = 1UL << 21,

    /// <summary>"Put a permanent onto the battlefield from hand without
    /// paying its mana cost" (Sneak Attack / Through the Breach / Show and
    /// Tell). Heuristic bot accepts when the candidate is high-impact.</summary>
    CheatIntoPlay = 1UL << 22,

    /// <summary>Optional "you may shuffle your library" rider on a library
    /// reorder (Ponder, Brainstorm-style cantrips, Sensei's Divining Top).
    /// Decision is neutral (depends on whether the just-seen top is
    /// keepable), so the default heuristic falls through to the
    /// neutral-accept branch — matches the legacy "auto-shuffle" posture
    /// used before this prompt shipped.</summary>
    LibraryReorder = 1UL << 23,

    /// <summary>Look at / exile a card from an opponent's hand
    /// (Thought-Knot Seer's ETB pick, Cabal Therapy's revealed-name picker
    /// from the casting controller's perspective, future Hymn-to-Tourach
    /// style "reveal X cards, you pick" surfaces). Distinct from
    /// <see cref="Discard"/> because the CHOOSER is not the card's owner —
    /// heuristic bots score these as removal of the opponent's best card.</summary>
    HandHate     = 1UL << 24,
}

public static class BotIntentExtensions
{
    public static bool HasAny(this BotIntent i, BotIntent mask) => (i & mask) != 0;
    public static bool HasAll(this BotIntent i, BotIntent mask) => (i & mask) == mask;
}
