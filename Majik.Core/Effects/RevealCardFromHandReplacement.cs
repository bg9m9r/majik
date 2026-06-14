using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.10 — "as this enters, you may reveal a [subtype] card from your hand"
/// replacement (Temple of the Dragon Queen — "you may reveal a Dragon card from
/// your hand"). Watches the permanent's own ETB <see cref="ZoneMoveIntent"/> and,
/// on the async production path, prompts the controller's
/// <see cref="IPlayerAgent"/> via
/// <see cref="IPlayerAgent.ChooseRevealCardFromHandAsync"/> when the controller
/// holds a matching card, stamping the result onto the shared
/// <see cref="RevealedFromHandFlag"/> that the paired
/// <see cref="ConditionalEntersTappedReplacement"/> reads.
///
/// <para>
/// This replacement never transforms the intent (it doesn't tap, doesn't cancel)
/// — it purely records "a [subtype] card was revealed this way" as a side effect
/// and passes the intent through unchanged, so it composes cleanly with the
/// enters-tapped chain. It must be registered <em>before</em> the conditional
/// tapped replacement so the flag is stamped first (CR 614.10 — the reveal is
/// part of the same "as this enters" event the tapped clause checks). Mirrors
/// <see cref="ChooseColorReplacement"/>'s side-effect-only, prompt-on-async-only
/// shape.
/// </para>
///
/// <para>
/// The synchronous <see cref="Replace"/> path (shape-only / no
/// <see cref="ResolutionContext"/> to prompt on) leaves the flag at its default
/// <see langword="false"/> — no card was revealed "this way", so the gating
/// condition falls back to its other half ("or you control a [subtype]").
/// </para>
/// </summary>
public sealed class RevealCardFromHandReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;
    private readonly CardSubtype _subtype;
    private readonly string _matchLabel;
    private readonly RevealedFromHandFlag _flag;

    /// <param name="card">The land/permanent this replacement is bound to.</param>
    /// <param name="subtype">The subtype a revealed hand card must carry
    /// (Temple of the Dragon Queen → <see cref="CardSubtype.Dragon"/>).</param>
    /// <param name="matchLabel">Human-readable label for the prompt ("a Dragon
    /// card").</param>
    /// <param name="flag">Shared holder stamped when a matching card is revealed,
    /// read by the paired <see cref="ConditionalEntersTappedReplacement"/>.</param>
    public RevealCardFromHandReplacement(
        ICard card, CardSubtype subtype, string matchLabel, RevealedFromHandFlag flag)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _subtype = subtype;
        _matchLabel = matchLabel ?? throw new ArgumentNullException(nameof(matchLabel));
        _flag = flag ?? throw new ArgumentNullException(nameof(flag));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    /// <summary>
    /// Synchronous path — no <see cref="ResolutionContext"/> to prompt on. A
    /// player choice must be <c>await</c>ed, never bridged sync-over-async, so
    /// the prompt lives exclusively on <see cref="ReplaceAsync"/>. Here the flag
    /// keeps its default <see langword="false"/>; the intent passes through.
    /// </summary>
    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) => intent;

    /// <summary>
    /// Async path — genuinely <c>await</c>s the controller's agent so a human's
    /// reveal choice never blocks a thread-pool thread. Only prompts when the
    /// controller actually holds a matching card (CR 614.10 — "you may reveal a
    /// [subtype] card from your hand"; no prompt when there's nothing to reveal).
    /// Stamps the flag on a "yes" and passes the intent through unchanged.
    /// </summary>
    public async ValueTask<ZoneMoveIntent?> ReplaceAsync(
        ZoneMoveIntent intent, IReadOnlyList<object> history, ResolutionContext ctx)
    {
        var controller = intent.Controller ?? _card.Owner;
        if (controller is null) return intent;

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent is null) return intent;

        // Engine pre-filters the hand to the matching cards (CR 614.10 — the
        // reveal is from the controller's hand; exclude this card, which is
        // mid-ETB and not in hand, defensively by reference equality).
        var matching = controller.Zones.Hand.GetCards()
            .Where(c => !ReferenceEquals(c, _card) && c.HasSubtype(_subtype))
            .ToList();
        if (matching.Count == 0) return intent;

        try
        {
            var revealed = await agent.ChooseRevealCardFromHandAsync(
                ctx: ctx.Game,
                matching: matching,
                matchLabel: _matchLabel,
                ct: ctx.Ct).ConfigureAwait(false);
            if (revealed is not null) _flag.MarkRevealed();
        }
        catch
        {
            // Defensive: any agent fault → treat as "not revealed".
        }

        return intent;
    }
}
