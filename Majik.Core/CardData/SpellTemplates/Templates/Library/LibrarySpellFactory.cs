using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

internal static class LibrarySpellFactory
{
    // "Preordain"-style: scry happens (default-all-bottom decision), then "draw a card"
    // tail clause fires. Cantrip portion is the substantive effect.
    private static readonly Regex ScryThenDrawTail = new(
        @"scry\s+\d+[^.]*[,.]?\s*then\s+draw\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    internal static SpellDefinition MillTargetSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"mill {n}", () =>
            {
                if (target is not Player pl) return;
                MillAction.Apply(pl, n);
            }) };
        });

    internal static SpellDefinition MillSelfSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"mill self {n}", () =>
        {
            MillAction.Apply(caster, n);
        }) });

    internal static SpellDefinition EachOpponentMillsSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[] { new Effect($"each opponent mills {n}", () =>
        {
            // Opponents are resolved via ChosenSpellParams.AllPlayers when
            // SpellCastFlow is updated to pass the full player list.
            // Until then, tests can supply players via the params.
            if (p.AllPlayers != null)
            {
                foreach (var pl in p.AllPlayers.Where(pl => !ReferenceEquals(pl, caster)))
                    MillAction.Apply(pl, n);
            }
        }) });

    internal static SpellDefinition EachPlayerMillsSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[] { new Effect($"each player mills {n}", () =>
        {
            // All players are resolved via ChosenSpellParams.AllPlayers when
            // SpellCastFlow is updated to pass the full player list.
            if (p.AllPlayers != null)
            {
                foreach (var pl in p.AllPlayers)
                    MillAction.Apply(pl, n);
            }
        }) });

    internal static SpellDefinition SurveilSelfSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"surveil {n}", () =>
        {
            var peeked = SurveilAction.Peek(caster, n);
            if (peeked.Count == 0) return;

            // Consult the registered agent when available; fall back to the
            // pre-agent default (all-to-graveyard) when none is registered.
            // TODO: remove sync-over-async once IEffect.Execute becomes async.
            var agent = AgentRegistry.Get(caster);
            SurveilAction.SurveilDecision decision;
            if (agent != null)
            {
                decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                    .GetAwaiter().GetResult();
            }
            else
            {
                decision = new SurveilAction.SurveilDecision(
                    ToGraveyard: peeked.ToList(),
                    TopOrder: Array.Empty<ICard>());
            }
            SurveilAction.Apply(caster, n, decision);
        }) });

    internal static SpellDefinition ScryNSpell(Player caster, string oracleText, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("scry+draw", () =>
        {
            var peeked = ScryAction.Peek(caster, n);
            if (peeked.Count > 0)
            {
                // Consult the registered agent when available; fall back to the
                // pre-agent default (all-to-bottom) when none is registered.
                // TODO: remove sync-over-async once IEffect.Execute becomes async.
                var agent = AgentRegistry.Get(caster);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = agent.ChooseScryDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                ScryAction.Apply(caster, n, decision);
            }

            var tail = ScryThenDrawTail.Match(oracleText);
            if (tail.Success)
            {
                DrawCards_(caster, SpellTemplateHelpers.WordToInt(tail.Groups["n"].Value));
            }
        }) });

    internal static SpellDefinition ReanimateSpell(Func<object, object> resolver, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(
            string.IsNullOrEmpty(kindRaw) ? "target card in graveyard" : $"target {kindRaw} card in graveyard",
            1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("return from gy", () =>
            {
                if (target is not ICard card) return;
                var owner = card.Owner;
                if (owner == null) return;
                if (card.Zone == ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(card);
                owner.Zones.Hand.AddCard(card);
                card.SetZone(ZoneType.Hand);
            }) };
        });

    internal static SpellDefinition ReanimateToBattlefieldSpell(
        Player caster, Func<object, object> resolver, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(
            string.IsNullOrEmpty(kindRaw) ? "target card in graveyard" : $"target {kindRaw} card in graveyard",
            1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("reanimate to battlefield", () =>
            {
                if (target is not ICard card) return;
                var owner = card.Owner;
                if (owner == null) return;
                if (card.Zone == ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(card);
                // Reanimated permanent enters under the caster's control
                // (CR 110.2) — the caster of the reanimation spell, not the
                // graveyard's owner. Owner is unchanged.
                caster.Zones.Battlefield.AddCard(card);
                card.SetZone(ZoneType.Battlefield);
                card.SetController(caster);
            }) };
        });

    internal static SpellDefinition ExileFromGraveyardSpell(Func<object, object> resolver, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(
            string.IsNullOrEmpty(kindRaw) ? "target card in graveyard" : $"target {kindRaw} card in graveyard",
            1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("exile from gy", () =>
            {
                if (target is ICard card && card.Zone == ZoneType.Graveyard)
                    OracleSpellBinder.MoveToExile(card);
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
            top.SetZone(ZoneType.Hand);
        }
    }

}
