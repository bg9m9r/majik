using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

internal static class DestroySpellFactory
{
    internal static SpellDefinition DestroyCreatureSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy creature", () =>
            {
                if (target is Creature c) OracleSpellBinder.MoveToGraveyard(c);
            }) };
        });

    /// <summary>
    /// Fatal Push template (v1 — base clause only, revolt deferred).
    /// Destroys target creature only if its mana value is ≤ maxCmc.
    /// The card's <see cref="Card.ManaCostValue"/> drives the CMC check (Rule 202.3).
    /// </summary>
    internal static SpellDefinition DestroyCreatureCmcLimitSpell(Func<object, object> resolver, int maxCmc) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"destroy if cmc<={maxCmc}", () =>
            {
                if (target is Creature crt)
                {
                    var cmc = crt.ManaCostValue.TotalValue;
                    if (cmc <= maxCmc) OracleSpellBinder.MoveToGraveyard(crt);
                }
            }) };
        });

    internal static SpellDefinition DestroyArtifactOrEnchantmentSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target artifact or enchantment", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy artifact/enchantment", () =>
            {
                if (target is ICard card) OracleSpellBinder.MoveToGraveyard(card);
            }) };
        });

    /// <summary>
    /// "Destroy up to N target artifacts and/or enchantments." (Force of Vigor
    /// template). MinTargets = 0 so the spell is legal with no targets chosen.
    /// CR 601.2c — "up to N" allows 0 through N legal targets.
    /// </summary>
    internal static SpellDefinition DestroyUpToArtifactEnchantmentSpell(
        Func<object, object> resolver, int maxN) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[]
        {
            new TargetRequest(
                $"up to {maxN} target artifacts and/or enchantments",
                MinTargets: 0,
                MaxTargets: maxN,
                LegalCandidates: Array.Empty<object>()),
        },
        EffectFactory: p =>
        {
            // Resolve all target references eagerly before returning the effect.
            var targets = p.Targets[0]
                .Select(t => resolver(t))
                .ToList();
            return new IEffect[] { new Effect($"destroy up to {maxN} artifact/enchantment", () =>
            {
                foreach (var resolved in targets)
                {
                    if (resolved is ICard card
                        && (card.HasType(CardType.Artifact)
                            || card.HasType(CardType.Enchantment)))
                    {
                        OracleSpellBinder.MoveToGraveyard(card);
                    }
                }
            }) };
        });

    internal static SpellDefinition DestroyTargetSpell(
        Func<object, object> resolver, string label, Func<ICard, bool> filter) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy target", () =>
            {
                if (target is ICard card && filter(card)) OracleSpellBinder.MoveToGraveyard(card);
            }) };
        });
}
