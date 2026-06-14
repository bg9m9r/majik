using Majik.Core.Cards;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 700.2d / CR 601.2b — the declarative "choose one (or more)" mode
/// request a modal triggered ability (or activated ability) carries so the
/// engine can prompt the controller's agent for the mode(s) at STACK-ENTRY
/// time — the triggered-ability analogue of <see cref="TargetRequest"/>
/// (chosen targets, CR 603.3) and the activated-ability <c>ChosenX</c> ledger
/// (CR 601.2b).
///
/// <para>
/// A modal ETB trigger (Knight of Autumn, Charming Prince, …) declares ONE
/// of these alongside its <see cref="Abilities.TriggeredAbility.TargetRequests"/>.
/// <see cref="Abilities.TriggerManager.PutPendingTriggersOnStackAsync"/> reads
/// it, prompts the controller's agent via
/// <see cref="IPlayerAgent.ChooseModeAsync"/> (single-mode) or
/// <see cref="IPlayerAgent.ChooseModesAsync"/> (multi-mode), records the result
/// on the ability via <c>SetChosenModes</c>, and the ability threads it into
/// <see cref="Abilities.ResolutionContext.ChosenModes"/> at resolve time. The
/// effect body reads the engine-recorded mode off the live context rather than
/// a factory-captured closure — this is the "true agent-driven mode prompt"
/// the v1 modal-ETB factories deferred.
/// </para>
///
/// <para>
/// This is the announce-time selection seam the spell path already uses
/// (<c>SpellCastFlow.PromptForModesAsync</c> + <c>ChosenSpellParams.ModeIndexes</c>);
/// <see cref="ModeRequest"/> brings the same data-driven prompt to the
/// triggered/activated-ability stack-entry path so the mode is recorded on the
/// stack object the way ChosenTargets / ChosenX already are.
/// </para>
/// </summary>
/// <param name="Modes">Printed mode labels, in oracle order (CR 700.2d). The
/// number of entries is the number of modes offered.</param>
/// <param name="MinModes">Minimum number of modes to choose (CR 700.2e). 1 for
/// "Choose one"; 2 for "Choose two"; etc.</param>
/// <param name="MaxModes">Maximum number of modes to choose. 1 for "Choose
/// one"; equal to <c>Modes.Count</c> for "Choose one or more".</param>
/// <param name="ModeIntents">Per-mode <see cref="BotIntent"/> classification,
/// parallel to <paramref name="Modes"/> when populated; empty means the
/// declaration did not classify per-mode intent (intent-aware agents fall back
/// to label scoring). Mirrors the spell path's
/// <c>SpellDefinition.ModeIntents</c>.</param>
public sealed record ModeRequest(
    IReadOnlyList<string> Modes,
    int MinModes = 1,
    int MaxModes = 1,
    IReadOnlyList<BotIntent>? ModeIntents = null)
{
    /// <summary>True for a "Choose one" mode request (exactly one mode);
    /// false for "Choose two" / "Choose one or more" (CR 700.2d).</summary>
    public bool IsSingleMode => MinModes == 1 && MaxModes == 1;
}
