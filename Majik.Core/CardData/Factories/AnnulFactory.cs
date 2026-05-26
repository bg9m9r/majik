using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Annul (Urza's Saga / Onslaught / Magic Origins, {U}).
///
/// Instant. Oracle text:
///   "Counter target artifact or enchantment spell."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - 1..1 target spell request. At resolution the engine checks whether
///   the target spell has <see cref="CardType.Artifact"/> or
///   <see cref="CardType.Enchantment"/>; if not, the effect does nothing
///   (CR 608.2b — illegal target check, same defensive-resolve posture as
///   <see cref="NegateFactory"/>). Otherwise the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move
///   (CR 701.5).
/// </summary>
[CardName("Annul")]
public static class AnnulFactory
{
    public const string CardName = "Annul";
    public const string PrintedManaCost = "{U}";

    /// <summary>CardDef DSL — card shape only. The artifact/enchantment-
    /// spell counter SpellDefinition is built via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target artifact or enchantment spell"
    /// SpellDefinition. CR 608.2b: if the chosen target is no longer an
    /// artifact or enchantment spell at resolution time, the effect does
    /// nothing — same defensive posture as <see cref="NegateFactory"/>
    /// (the filter is applied at resolve time rather than at choose-time;
    /// <see cref="TargetRequest.LegalCandidates"/> left empty).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target artifact or enchantment spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Annul — counter target artifact or enchantment spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — if the target is no longer an artifact or
                        // enchantment spell at resolution, the effect does
                        // nothing for it.
                        if (!spell.Card.HasType(CardType.Artifact)
                            && !spell.Card.HasType(CardType.Enchantment))
                        {
                            return;
                        }

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
