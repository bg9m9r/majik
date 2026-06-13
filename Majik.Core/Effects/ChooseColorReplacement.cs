using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.12 — "as this enters, choose a color" replacement. Watches the
/// permanent's own ETB <see cref="ZoneMoveIntent"/> and, on the async
/// production path, prompts the controller's <see cref="IPlayerAgent"/> via
/// <see cref="IPlayerAgent.ChooseColorAsync"/> and stamps the pick onto the
/// shared <see cref="ColorChoice"/> that the permanent's synthesized
/// <see cref="ManaAbility"/> reads at activation time.
///
/// <para>
/// The choice is made "as this enters" (CR 614.12), so it resolves on the same
/// ETB intent that an "enters tapped" replacement sees — both fire in the same
/// <see cref="ReplacementBus.ApplyAsync{TIntent}"/> pass. This replacement does
/// NOT transform the intent (it doesn't tap, doesn't add counters, never
/// cancels) — it purely records the chosen colour as a side effect and passes
/// the intent through unchanged, so it composes cleanly with the enters-tapped
/// chain (the binder registers both for Sunken Citadel / Temple of the Dragon
/// Queen).
/// </para>
///
/// <para>
/// The synchronous <see cref="Replace"/> path (shape-only / non-cast moves with
/// no <see cref="ResolutionContext"/> to prompt on) leaves the
/// <see cref="ColorChoice"/> at its seeded default — exactly one colour is
/// producible, which is strictly narrower than the old over-permissive
/// five-WUBRG binding and never auto-suicides. This mirrors
/// <see cref="ShockLandReplacement"/>'s "prompt only on the async path" posture.
/// </para>
/// </summary>
public sealed class ChooseColorReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;
    private readonly ColorChoice _choice;

    public ChooseColorReplacement(ICard card, ColorChoice choice)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _choice = choice ?? throw new ArgumentNullException(nameof(choice));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    /// <summary>
    /// Synchronous path — no <see cref="ResolutionContext"/> to prompt on
    /// (shape-only callers, non-cast zone moves). A player choice must be
    /// <c>await</c>ed, never bridged sync-over-async, so the prompt lives
    /// exclusively on <see cref="ReplaceAsync"/>. Here the
    /// <see cref="ColorChoice"/> keeps its seeded default; the intent passes
    /// through unchanged.
    /// </summary>
    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) => intent;

    /// <summary>
    /// Async path (<see cref="ReplacementBus.ApplyAsync{TIntent}"/>) — genuinely
    /// <c>await</c>s the controller's agent so a human's colour choice never
    /// blocks a thread-pool thread on a sync-over-async bridge. Stamps the pick
    /// onto the shared <see cref="ColorChoice"/> and passes the intent through
    /// unchanged (CR 614.12 — choosing a colour doesn't change how the
    /// permanent enters; it only sets what its mana abilities later produce).
    /// </summary>
    public async ValueTask<ZoneMoveIntent?> ReplaceAsync(
        ZoneMoveIntent intent, IReadOnlyList<object> history, ResolutionContext ctx)
    {
        var controller = intent.Controller ?? _card.Owner;
        var agent = ctx.Agent
            ?? (controller is not null ? AgentRegistry.Get(controller) : null);
        if (agent is null)
        {
            // No agent to prompt — keep the seeded default colour (one
            // producible colour, never the old five-WUBRG over-permissiveness).
            return intent;
        }

        try
        {
            var color = await agent.ChooseColorAsync(
                ctx: ctx.Game,
                sourceLabel: $"{_card.Name} — choose a color",
                fallback: _choice.Chosen,
                ct: ctx.Ct).ConfigureAwait(false);
            _choice.Choose(color);
        }
        catch
        {
            // Defensive: any agent fault → keep the seeded default colour.
        }

        return intent;
    }
}
