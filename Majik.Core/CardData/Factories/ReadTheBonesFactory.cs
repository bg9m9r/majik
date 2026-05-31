using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Read the Bones (Theros, {2}{B}).
///
/// Sorcery. Oracle text:
///   "Scry 2, then draw two cards. You lose 2 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}.
/// - Resolve effect via <see cref="BuildResolveEffect"/>:
///     1. Scry 2 — peek top 2 of caster's library (CR 701.20). When an
///        <see cref="IPlayerAgent"/> is registered via
///        <see cref="AgentRegistry"/> the controller decides the
///        bottom/top partition; otherwise the pre-agent default sends
///        all peeked cards to the bottom (same posture as
///        <see cref="SerumVisionsFactory"/> / <see cref="PreordainFactory"/>).
///        Empty library short-circuits cleanly.
///     2. Draw 2 — routed through <see cref="Fx.DrawCards"/> so the
///        replacement bus (Dredge etc.) gets a shot per draw and
///        empty-library stamps the SBA loss flag (CR 704.5b).
///     3. Lose 2 life — <see cref="Fx.LoseLife"/> (CR 119.3).
///
/// ## Order matters
/// Read the Bones explicitly sequences scry-before-draw — the scry
/// inspects the pre-draw top of the library. The "you lose 2 life"
/// clause is a separate sentence; CR 700.2 sequences it after the draw.
///
/// ## Notes
/// Theros-era staple grindy black card draw. Pairs with Blood Artist /
/// Zulaport Cutthroat (the life-loss is a single ticked event, not 2 x 1,
/// so it does NOT double those triggers — same shape as Sign in Blood /
/// Painful Truths / Night's Whisper).
/// </summary>
[CardName("Read the Bones")]
public static class ReadTheBonesFactory
{
    public const string CardName = "Read the Bones";
    public const string PrintedManaCost = "{2}{B}";
    public const int ScryAmount = 2;
    public const int DrawAmount = 2;
    public const int LifeLoss = 2;

    /// <summary>
    /// Construct Read the Bones as a Sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve closure is produced by
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> for Read the Bones. No
    /// target requests — the body resolves entirely on the caster.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build the resolve effect: scry 2, then draw 2, then lose 2 life.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: scry 2, then draw 2, then lose 2 life.",
                async ctx =>
                {
                    // CR 701.20 — Scry 2. Look at top two cards; controller
                    // partitions bottom-bound vs top-ordered. Agent-driven
                    // when registered; pre-agent default sends both to the
                    // bottom (matches SerumVisionsFactory / PreordainFactory).
                    var peeked = ScryAction.Peek(caster, ScryAmount);
                    if (peeked.Count > 0)
                    {
                        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                        ScryAction.ScryDecision decision;
                        if (agent != null)
                        {
                            // TODO: drop sync-over-async once IEffect.Execute becomes async.
                            decision = (await agent.ChooseScryDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                        }
                        else
                        {
                            decision = new ScryAction.ScryDecision(
                                ToBottom: peeked.ToList(),
                                TopOrder: Array.Empty<ICard>());
                        }
                        ScryAction.Apply(caster, peeked.Count, decision);
                    }

                    // CR 121.1 — draw 2. Replacement bus per-draw; empty
                    // library stamps the SBA loss flag (CR 704.5b).
                    Fx.DrawCards(caster, DrawAmount);

                    // CR 119.3 — lose 2 life. Single life-loss event for
                    // the resolved life total (not 2 x 1).
                    Fx.LoseLife(caster, LifeLoss);
                }),
        };
    }
}
