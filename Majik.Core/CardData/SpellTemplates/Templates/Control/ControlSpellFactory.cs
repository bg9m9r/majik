using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

internal static class ControlSpellFactory
{
    internal static SpellDefinition TapTargetSpell(Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("tap target", () =>
            {
                if (target is Permanent perm && !perm.IsTapped) perm.Tap();
            }) };
        });

    internal static SpellDefinition UntapTargetSpell(Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("untap target", () =>
            {
                if (target is Permanent perm && perm.IsTapped) perm.Untap();
            }) };
        });

    internal static SpellDefinition BounceTargetSpell(
        Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("bounce", () =>
            {
                if (target is ICard card) ReturnToOwnersHand(card);
            }) };
        });

    internal static SpellDefinition ExileTargetSpell(Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("exile target", () =>
            {
                if (target is ICard card) OracleSpellBinder.MoveToExile(card);
            }) };
        });

    internal static SpellDefinition GainControlSpell(
        Func<object, object> resolver, Player caster,
        ContinuousEffectsService effects) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("gain control", () =>
            {
                if (target is Permanent perm)
                    effects.Register(new ControlChangeEffect(perm, caster));
            }) };
        });

    private static void ReturnToOwnersHand(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            if (card.Zone == ZoneType.Battlefield)
                owner.Zones.Battlefield.RemoveCard(card);
            else if (card.Zone == ZoneType.Graveyard)
                owner.Zones.Graveyard.RemoveCard(card);
            else if (card.Zone == ZoneType.Exile)
                owner.Zones.Exile.RemoveCard(card);
            owner.Zones.Hand.AddCard(card);
        }
        card.SetZone(ZoneType.Hand);
    }
}
