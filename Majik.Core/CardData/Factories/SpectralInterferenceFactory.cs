using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spectral Interference (Modern Horizons 3, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target artifact or creature spell unless its controller pays {4}."
///
/// ## Why a named factory (no template covers it)
/// This is the "soft counter unless pays" rider of <see cref="MiscalculationFactory"/>
/// / <see cref="ManaLeakFactory"/> but with the type-restricted target of
/// <see cref="ExcludeFactory"/> ("artifact or creature spell"). No single spell
/// template binds the type restriction and the "unless pays {4}" rider together,
/// so it gets a named factory.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue. Card shape comes from the embedded
///   JSON (<c>spectral-interference.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Counter target artifact or creature spell unless its controller pays {4}</b>:
///   at resolution the engine first checks CR 608.2b — if the chosen target is no
///   longer an artifact spell or a creature spell, the entire effect does nothing.
///   Otherwise CR 118.4: the target's controller may pay {4} to prevent the
///   counter; v1 auto-pays when able (same deterministic posture as
///   <see cref="MiscalculationFactory"/> / Mana Leak / Quench). If the controller
///   cannot pay, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moves to the
///   graveyard (CR 701.5).
///
/// ## Deferred
/// - Real "do you want to pay {4}?" agent prompt — same queue as Miscalculation /
///   Mana Leak / Censor. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Spectral Interference")]
public static class SpectralInterferenceFactory
{
    public const string CardName = "Spectral Interference";
    public const string Slug = "spectral-interference";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays {4}").</summary>
    public const int UnlessPayGeneric = 4;

    /// <summary>
    /// Construct Spectral Interference. The card shape (Instant {1}{U}, blue) is
    /// materialized from the embedded JSON definition.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Spectral
    /// Interference's "counter target artifact or creature spell unless its
    /// controller pays {4}" effect. Combines the type restriction of
    /// <see cref="ExcludeFactory"/> with the "unless pays" rider of
    /// <see cref="MiscalculationFactory"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target artifact or creature spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Spectral Interference — counter target artifact or creature spell unless its controller pays {4}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — if the target is no longer an artifact spell
                        // or a creature spell at resolution, the entire effect
                        // does nothing (no counter).
                        if (!spell.Card.HasType(CardType.Artifact)
                            && !spell.Card.HasType(CardType.Creature))
                            return;

                        // CR 118.4 — the target's controller may pay {4} to prevent
                        // the counter. v1 auto-pays when able (same posture as
                        // Miscalculation / Mana Leak / Quench).
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            return;
                        }

                        // Controller couldn't pay — counter the spell (CR 701.5).
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
