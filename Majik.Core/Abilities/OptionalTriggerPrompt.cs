using Majik.Core.Cards;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 603.5 / CR 117.x — the declarative "you may" gate carried by an OPTIONAL
/// triggered ability ("whenever X, <b>you may</b> do Y"). Attached to a
/// <see cref="TriggeredAbility"/> via its <c>optionalPrompt</c> constructor
/// argument; when present, the engine prompts the controller's agent yes/no at
/// resolution (CR 603.5 — the choice to perform a "may" instruction is made as
/// the ability resolves) and skips the ability's effects on a decline.
/// </summary>
/// <param name="Question">
/// The human-readable yes/no question (e.g. "Return target permanent card to
/// the battlefield?"). Surfaced to remote UIs through the agent prompt.
/// </param>
/// <param name="Intent">
/// The <see cref="BotIntent"/> classifier the default agent heuristic reads to
/// auto-answer (upside intents — <see cref="BotIntent.Reanimate"/>,
/// <see cref="BotIntent.CardAdvantage"/>, <see cref="BotIntent.Tutor"/>, … —
/// auto-accept, preserving the legacy auto-take posture; downside intents
/// auto-decline). Human / search agents may override.
/// </param>
public readonly record struct OptionalTriggerPrompt(string Question, BotIntent Intent);
