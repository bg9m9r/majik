using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Shared composer for the scry / surveil / draw CANTRIP family (Opt, Serum
/// Visions, Preordain, Consider, …) — the "cantrip-factory-harvest" pay-down.
///
/// <para>
/// Each cantrip is just an ORDERED array of the untargeted
/// <see cref="ScrySelfEffectDef"/> / <see cref="SurveilSelfEffectDef"/> /
/// <see cref="DrawCardEffectDef"/> verbs. The production instant/sorcery cast
/// path resolves a cantrip via its declarative <c>BuildDefinition</c>
/// (<see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>); this composer
/// serves the legacy <c>BuildResolveEffect</c> helper shape (a single
/// <see cref="IEffect"/> the per-card tests call <c>.Single()</c> on) by wrapping
/// the same verb array into ONE composite effect that executes each verb's
/// resolve closure in printed order.
/// </para>
///
/// <para>
/// Both shapes build the identical per-verb closures off
/// <see cref="EffectDefinition.ToResolveEffect()"/>, so the two paths are
/// byte-identical: agent scry/surveil decisions flow through the shared
/// <see cref="Majik.Core.Players.Agents.AgentRegistry"/> seam, and an
/// empty-library draw flags the draw-from-empty state-based loss (CR 120.3 /
/// 704.5b) via <see cref="Majik.Core.Primitives.Fx.DrawCards"/> inside the
/// <c>draw_card</c> verb.
/// </para>
/// </summary>
internal static class CantripEffectComposer
{
    /// <summary>
    /// Compose <paramref name="effectDefs"/> (in printed order) into a single
    /// sequenced <see cref="IEffect"/> bound to <paramref name="caster"/>. Each
    /// verb is built untargeted (target-request index -1) — cantrips never
    /// target.
    /// </summary>
    public static IReadOnlyList<IEffect> Compose(
        string cardName, Player caster, IReadOnlyList<EffectDefinition> effectDefs)
    {
        ArgumentNullException.ThrowIfNull(cardName);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(effectDefs);

        // A bare name-only card: the scry/surveil/draw verbs read it only for
        // their Description string, never for resolve behaviour (the caster is
        // the resolution subject) — same posture as BuildSpellDefinitionFromEffects.
        var nameCard = new Instant(cardName, "");
        var built = new IEffect[effectDefs.Count];
        for (var i = 0; i < effectDefs.Count; i++)
        {
            // targetRequestIndex -1 = untargeted (cantrip verbs declare no target).
            built[i] = effectDefs[i].ToResolveEffect()(nameCard, caster, null, -1);
        }

        return new IEffect[]
        {
            new Effect($"{cardName}: cantrip ({effectDefs.Count} verb(s)).", async ctx =>
            {
                foreach (var e in built)
                {
                    await e.ExecuteAsync(ctx).ConfigureAwait(false);
                }
            }),
        };
    }
}
