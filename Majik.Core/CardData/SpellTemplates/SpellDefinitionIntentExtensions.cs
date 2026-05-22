using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Stamps a producing template's <see cref="BotIntent"/> onto every
/// <see cref="TargetRequest"/> in a <see cref="SpellDefinition"/>. Run
/// once at each "template bind boundary" — the registry's top-level
/// <c>TryBind</c>, and the composer / modal templates' per-sub-template
/// rehydrate calls — so per-target intent is populated without each of
/// the ~58 emit sites needing to pass it explicitly.
///
/// Only overwrites <see cref="TargetRequest.Intent"/> when it is
/// <see cref="BotIntent.None"/>, letting individual templates pre-set a
/// sub-intent (e.g. a per-target subset of the template's own union) and
/// preserving it through the stamp.
/// </summary>
internal static class SpellDefinitionIntentExtensions
{
    public static SpellDefinition WithIntentStamp(this SpellDefinition def, BotIntent intent)
    {
        if (intent == BotIntent.None) return def;
        if (def.TargetRequests.Count == 0) return def;

        var stamped = new List<TargetRequest>(def.TargetRequests.Count);
        var changed = false;
        foreach (var req in def.TargetRequests)
        {
            if (req.Intent == BotIntent.None)
            {
                stamped.Add(req with { Intent = intent });
                changed = true;
            }
            else
            {
                stamped.Add(req);
            }
        }
        return changed ? def with { TargetRequests = stamped } : def;
    }
}
