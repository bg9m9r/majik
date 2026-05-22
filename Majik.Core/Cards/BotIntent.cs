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
}

public static class BotIntentExtensions
{
    public static bool HasAny(this BotIntent i, BotIntent mask) => (i & mask) != 0;
    public static bool HasAll(this BotIntent i, BotIntent mask) => (i & mask) == mask;
}
