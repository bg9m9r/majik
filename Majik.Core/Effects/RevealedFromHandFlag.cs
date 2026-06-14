namespace Majik.Core.Effects;

/// <summary>
/// CR 614.10 — mutable holder for an "as this enters, you may reveal a [match]
/// card from your hand" decision (Temple of the Dragon Queen — "you may reveal
/// a Dragon card from your hand … unless you revealed a Dragon card this way").
///
/// <para>
/// The reveal happens "as the permanent enters", on the same ETB intent that
/// the "enters tapped unless …" replacement sees. Because a player choice must
/// be <c>await</c>ed, the reveal prompt lives on the async
/// <see cref="RevealCardFromHandReplacement.ReplaceAsync"/> path, which stamps
/// <see cref="Revealed"/> onto this shared holder; the
/// <see cref="ConditionalEntersTappedReplacement"/> registered for the same
/// card reads the flag at evaluation time. The reveal replacement is registered
/// <em>before</em> the conditional-tapped replacement so the flag is already
/// stamped by the time the tapped predicate runs (the bus applies registered
/// effects in registration order — see <see cref="ReplacementBus.ApplyAsync"/>).
/// </para>
///
/// <para>
/// On the synchronous / no-agent path the flag stays <see langword="false"/>
/// (no card was revealed "this way"), so the gating condition falls back to its
/// other half ("or you control a [match]"). This mirrors
/// <see cref="ChooseColorReplacement"/>'s "prompt only on the async path"
/// posture.
/// </para>
/// </summary>
public sealed class RevealedFromHandFlag
{
    /// <summary><see langword="true"/> once a matching card was revealed from
    /// hand "this way" as the permanent entered (CR 614.10).</summary>
    public bool Revealed { get; private set; }

    /// <summary>Stamp that a matching card was revealed "this way".</summary>
    public void MarkRevealed() => Revealed = true;
}
