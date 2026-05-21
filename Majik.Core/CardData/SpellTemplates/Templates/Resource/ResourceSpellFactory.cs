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

    private static void DrawCards_(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(Majik.Core.Zones.ZoneType.Hand);
        }
    }

    private static void DiscardCards(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Hand.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Hand.RemoveCard(top);
            player.Zones.Graveyard.AddCard(top);
            top.SetZone(Majik.Core.Zones.ZoneType.Graveyard);
        }
    }
}
