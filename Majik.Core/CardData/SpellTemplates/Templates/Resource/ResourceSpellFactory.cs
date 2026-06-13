using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

internal static class ResourceSpellFactory
{
    internal static SpellDefinition DrawNSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"draw {n}", () => DrawCards_(caster, n)) });

    internal static SpellDefinition DiscardNSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"discard {n}", () =>
            {
                if (target is Player pl) DiscardCards(pl, n);
            }) };
        });

    internal static SpellDefinition GainLifeSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"gain {n} life", () =>
            {
                if (target is Player player) player.GainLife(n);
            }) };
        });

    internal static SpellDefinition YouGainLifeSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"you gain {n}", () => caster.GainLife(n)) });

    internal static SpellDefinition YouLoseLifeSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"you lose {n}", () => caster.LoseLife(n)) });

    internal static SpellDefinition EachPlayerDrawsSpell(int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"each player draws {n}", () => { }) });

    internal static SpellDefinition TargetPlayerLosesLifeSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"target player loses {n}", () =>
            {
                if (target is Player pl) pl.LoseLife(n);
            }) };
        });

    // ---------- Primitives ----------

    // CR 121.1 — route the shared "draw N cards" cantrip path through the
    // centralised Fx.DrawCards primitive (cantrip-factory-harvest pay-down).
    // Fx.DrawCards applies the CR 614 draw-replacement bus per draw AND, on a
    // draw past an empty library, flags the draw-from-empty state-based loss
    // (CR 120.3 / 704.5b) via Player.MarkTriedToDrawFromEmptyLibrary — both of
    // which the prior hand-rolled loop silently skipped (it `return`ed without
    // marking the flag). This is the same primitive the JSON `draw_card` verb
    // and Opt / Serum Visions already resolve through.
    private static void DrawCards_(Player player, int n)
        => Majik.Core.Primitives.Fx.DrawCards(player, n);

    private static void DiscardCards(Player player, int n)
        // CR 701.8 — route through Fx.Discard (effect discard, wasCost: false)
        // so a DiscardedEvent fires per card and "Whenever you discard …"
        // triggers see it (Mind Rot et al.).
        => Majik.Core.Primitives.Fx.Discard(player, n);
}
