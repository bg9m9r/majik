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

    internal static SpellDefinition ReturnAllFromGraveyardSpell(Player caster, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"return all {kindRaw} from gy", () =>
        {
            // Lossy v1: every card in the caster's graveyard returns to the
            // caster's battlefield. Type/subtype/supertype filter from the
            // oracle text is ignored — the resolver does not enforce it and
            // non-permanent cards in the graveyard will move too. Acceptable
            // because cards without a matching template have no behavior at
            // all; mass-reanimating a graveyard's worth of cards is a closer
            // approximation than nothing.
            var snapshot = caster.Zones.Graveyard.GetCards().ToList();
            foreach (var card in snapshot)
            {
                caster.Zones.Graveyard.RemoveCard(card);
                caster.Zones.Battlefield.AddCard(card);
                card.SetZone(ZoneType.Battlefield);
                card.SetController(caster);
            }
        }) });

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

    // Impulse-style: look at top N, put one of those N into hand,
    // remaining N-1 go to <restDestination> (bottom of library or graveyard).
    // v1 stub: pick the top card of those N for the hand-card slot; preserve
    // order for the rest going to bottom; graveyard order doesn't matter.
    internal static SpellDefinition LookAtTopPutOneInHandSpell(
        Player caster, int n, ZoneType restDestination) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"impulse {n} -> {restDestination}", () =>
        {
            // Take up to N cards off the top of the library.
            var library = caster.Zones.Library.GetCards().Take(n).ToList();
            if (library.Count == 0) return;
            var keep = library[0];
            caster.Zones.Library.RemoveCard(keep);
            caster.Zones.Hand.AddCard(keep);
            keep.SetZone(ZoneType.Hand);
            // The rest: remove from library and re-place.
            // For "bottom" we let the natural order push them to the back;
            // for "graveyard" we move them to graveyard.
            for (var i = 1; i < library.Count; i++)
            {
                var c = library[i];
                caster.Zones.Library.RemoveCard(c);
                if (restDestination == ZoneType.Graveyard)
                {
                    caster.Zones.Graveyard.AddCard(c);
                    c.SetZone(ZoneType.Graveyard);
                }
                else
                {
                    caster.Zones.Library.AddCard(c);
                    c.SetZone(ZoneType.Library);
                }
            }
        }) });

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
