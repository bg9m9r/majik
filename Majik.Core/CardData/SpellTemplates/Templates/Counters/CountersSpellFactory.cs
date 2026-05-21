using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

internal static class CountersSpellFactory
{
    internal static SpellDefinition PutPlusOnePlusOneSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"+{n} counters", () =>
            {
                if (target is Permanent perm)
                    perm.Counters.Add(CounterType.PlusOnePlusOne, n);
            }) };
        });

    internal static SpellDefinition PutMinusOneMinusOneSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"-{n} counters", () =>
            {
                if (target is Permanent perm)
                    perm.Counters.Add(CounterType.MinusOneMinusOne, n);
            }) };
        });

    internal static SpellDefinition CreaturesGetPlusCounterSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("+1/+1 counter to each", () =>
        {
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                c.Counters.Add(CounterType.PlusOnePlusOne, 1);
            }
        }) });

    internal static SpellDefinition PumpSpell(int p, int t, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: param =>
        {
            var target = resolver(param.Targets[0][0]);
            return new IEffect[] { new Effect($"+{p}/+{t} EOT", () =>
            {
                if (target is Creature c && c.ActiveEffects != null)
                {
                    c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, p, t));
                }
            }) };
        });

    internal static SpellDefinition GrantKeywordSpell(string keyword, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: param =>
        {
            var target = resolver(param.Targets[0][0]);
            return new IEffect[] { new Effect($"grants {keyword} EOT", () =>
            {
                if (target is Creature c && c.ActiveEffects != null)
                {
                    c.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(c, keyword));
                }
            }) };
        });

    // "All creatures get +P/+T (or -P/-T) until end of turn" — symmetrical
    // pump/debuff. v1 stub registers per-creature PumpUntilEndOfTurnEffect
    // for every creature on the caster's view of the battlefield. Sign-agnostic.
    // Opponents' creatures are out of reach until SpellCastFlow exposes
    // AllPlayers (same TODO as the wrath templates).
    internal static SpellDefinition AllCreaturesPumpSpell(
        int p, int t, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"all creatures {p:+#;-#;0}/{t:+#;-#;0} EOT", () =>
        {
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                if (c.ActiveEffects != null)
                {
                    c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, p, t));
                }
            }
        }) });

    internal static SpellDefinition CreaturesYouControlPumpSpell(
        int p, int t, Player caster,
        ContinuousEffectsService effects) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"creatures +{p}/+{t} EOT", () =>
        {
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                effects.Register(new GlobalPumpEffect(c, p, t));
            }
        }) });

    internal static SpellDefinition CreaturesYouControlGainKeywordSpell(
        string keyword, Player caster,
        ContinuousEffectsService effects) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"creatures gain {keyword} EOT", () =>
        {
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                effects.Register(new GrantKeywordUntilEndOfTurnEffect(c, keyword));
            }
        }) });

    internal static string NormaliseKeyword(string raw) =>
        // Collapse multi-word "first strike" / "double strike"; preserve casing
        // canonical to engine ("First strike" matches CombatAbilities check).
        raw.ToLowerInvariant() switch
        {
            "first strike" => "First strike",
            "double strike" => "Double strike",
            _ => char.ToUpperInvariant(raw[0]) + raw[1..].ToLowerInvariant(),
        };

    /// <summary>Layer 7c pump-this-creature effect, EOT.</summary>
    private sealed class GlobalPumpEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        public GlobalPumpEffect(Creature target, int p, int t)
        { _target = target; _p = p; _t = t; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _t; }
    }

    /// <summary>Layer 7c +P/+T effect with end-of-turn expiry.</summary>
    private sealed class PumpUntilEndOfTurnEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        public PumpUntilEndOfTurnEffect(Creature target, int p, int t)
        { _target = target; _p = p; _t = t; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _t; }
    }

    /// <summary>Layer 6 keyword grant with end-of-turn expiry.</summary>
    private sealed class GrantKeywordUntilEndOfTurnEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly string _kw;
        public GrantKeywordUntilEndOfTurnEffect(Creature target, string kw)
        { _target = target; _kw = kw; }
        public override Layer Layer => Layer.Abilities;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Keywords.Add(_kw); }
    }
}
