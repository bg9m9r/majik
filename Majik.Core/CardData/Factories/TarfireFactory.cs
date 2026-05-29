using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tarfire (Lorwyn / Modern Masters, {R}).
///
/// Kindred Instant — Goblin. Oracle text (verified against Scryfall):
///   "Tarfire deals 2 damage to any target."
///
/// ## Implementation
///
/// Vanilla "any target" burn — the same Shock-shaped spell at {R} → 2
/// damage (CR 115.3 — "any target" = creature, player, planeswalker, or
/// battle). Routed through <see cref="Fx.DealDamageAny"/> so all four
/// legal target classes resolve correctly (CR 306.7 — damage to a
/// planeswalker becomes loyalty removal). Resolve shape mirrors
/// <see cref="ShockFactory"/> exactly.
///
/// The only thing distinguishing Tarfire from Shock is its type line:
/// it is a "Kindred Instant — Goblin". CR 312 — <b>Kindred</b> is the
/// 2023 rename of the legacy "Tribal" card type, modelled here by
/// <see cref="CardType.Tribal"/>. The Goblin creature subtype rides on a
/// non-creature card so Goblin-tribal "matters" effects (e.g. Goblin
/// Chieftain's cost bonus, name/subtype tutors) can see it. These are
/// pure card-shape concerns expressed in the embedded JSON
/// (<c>tarfire.json</c>); they don't affect resolution.
///
/// Card shape comes from the embedded JSON via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a
/// <see cref="SpellDefinition"/> needs a target resolver supplied by the
/// caller's <see cref="GameContext"/> (not expressible in the data-only
/// JSON schema). The JSON lists <c>Instant</c> first so
/// <see cref="CardDefinitionFactory"/> builds the concrete
/// <see cref="Instant"/> class and adds <see cref="CardType.Tribal"/> as
/// a secondary type.
/// </summary>
[CardName("Tarfire")]
public static class TarfireFactory
{
    public const string CardName = "Tarfire";
    public const string Slug = "tarfire";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — fixed 2 damage to any target.</summary>
    public const int Damage = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Tarfire is cast.
    /// Single 1..1 "any target" request, no X; on resolution deals
    /// <see cref="Damage"/> (2) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Tarfire: 2 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
