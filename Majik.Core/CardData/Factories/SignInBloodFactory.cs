using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sign in Blood (Tenth Edition / many reprints, {B}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Target player draws two cards and loses 2 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}{B}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target player"
///   request. On resolution the TARGET player (not necessarily the caster):
///     1. <b>Draws 2 cards</b> (CR 121.1) — routed through
///        <see cref="Fx.DrawCards"/> so the replacement bus (Dredge etc.)
///        gets a shot per draw. An empty library stamps the SBA loss flag
///        (CR 704.5b) without throwing.
///     2. <b>Loses 2 life</b> (CR 119.3) — single life-loss event for the
///        resolved amount; not 2 × 1-life loss (matches Read the Bones /
///        Night's Whisper — important for Blood Artist / Cruel Celebrant
///        triggers that count loss events, not points).
/// - CR 608.2b guard: if the resolved target is not a <see cref="Player"/>
///   (illegal targeting, game object changed zone), the effect is a no-op.
///   Caster's life total is not affected unless they targeted themselves.
///
/// ## Order matters
/// The oracle text is a single sentence — "draws two cards AND loses 2
/// life" — which CR 101.4 / 700.2 treats as one event. The engine
/// sequences draw before life-loss (replacement bus fires per-draw before
/// the single life-loss tick). This matches the standard Read the Bones /
/// Painful Truths / Night's Whisper treatment.
/// </summary>
[CardName("Sign in Blood")]
public static class SignInBloodFactory
{
    public const string CardName = "Sign in Blood";
    public const string PrintedManaCost = "{B}{B}";
    public const int DrawAmount = 2;

    /// <summary>CR 119.3 — printed life loss on resolution.</summary>
    public const int LifeLoss = 2;

    /// <summary>
    /// Construct Sign in Blood as a Sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + draw/life-loss
    /// body is built on demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Sign in Blood is
    /// cast. Single 1..1 "target player" request; on resolution the target
    /// draws 2 cards (<see cref="Fx.DrawCards"/>) and loses 2 life
    /// (<see cref="Fx.LoseLife"/>). CR 608.2b guard no-ops on non-Player
    /// targets.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).
    /// Tests may pass the identity function <c>x =&gt; x</c>.</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: target player draws 2 cards and loses 2 life.", () =>
                    {
                        // CR 608.2b — illegal-target check. If the resolved
                        // object is not a Player (e.g. the targeted player
                        // left the game), the spell is a no-op.
                        if (raw is not Player target) return;

                        // CR 121.1 — draw 2. Routes through Fx.DrawCards so
                        // the replacement bus (Dredge etc.) gets a shot per
                        // draw, and empty-library stamps the SBA loss flag
                        // (CR 704.5b) without throwing.
                        Fx.DrawCards(target, DrawAmount);

                        // CR 119.3 — "loses 2 life." Single life-loss event;
                        // not 2 × 1 (matches Read the Bones / Night's Whisper
                        // posture — Blood Artist counts loss events, not
                        // points).
                        Fx.LoseLife(target, LifeLoss);
                    }),
                };
            });
    }
}
