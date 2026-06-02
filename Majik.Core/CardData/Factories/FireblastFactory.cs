using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fireblast (Visions / Tempest, {4}{R}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "You may sacrifice two Mountains rather than pay this spell's mana cost.
///    Fireblast deals 4 damage to any target."
///
/// ## Implementation
///
/// Combines two already-supported shapes:
/// - <b>Resolve body</b>: the archetypal "deal N damage to any target" burn,
///   same shape as <see cref="FlameJavelinFactory"/> / <see cref="ShockFactory"/>,
///   scaled to 4 damage. Routes through <see cref="Fx.DealDamageAny"/> so all
///   four "any target" classes resolve correctly (CR 115.3 — creature, player,
///   planeswalker, or battle). CR 306.7 — damage to a planeswalker becomes
///   loyalty removal; CR 309.5 — damage to a battle becomes defense removal;
///   both handled inside <see cref="Fx.DealDamageAny"/>.
/// - <b>"Sacrifice two Mountains rather than pay" alternative cost</b> (CR 118.9)
///   via <see cref="SacrificeTwoLandsAlternativeCost"/> — mirrors Daze's
///   bounce-land pitch and Flare of Denial's
///   <see cref="SacrificeNontokenBlueCreatureAlternativeCost"/>, specialized to
///   two Mountains (CR 305.6 basic-land subtype, CR 701.18 sacrifice). The
///   caster pre-picks the two Mountains; on resolution they move
///   Battlefield → Graveyard and no mana is paid.
///
/// Card shape comes from the embedded JSON (<c>fireblast.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema).
/// </summary>
[CardName("Fireblast")]
public static class FireblastFactory
{
    public const string CardName = "Fireblast";
    public const string Slug = "fireblast";

    /// <summary>Printed mana cost — {4}{R}{R}, mana value 6 (CR 202.3).</summary>
    public const string PrintedManaCost = "{4}{R}{R}";

    /// <summary>CR 119 — fixed 4 damage to any target.</summary>
    public const int Damage = 4;

    /// <summary>The basic-land subtype the alt cost sacrifices (CR 305.6).</summary>
    public const CardSubtype SacrificeSubtype = CardSubtype.Mountain;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "sacrifice two Mountains rather than pay this spell's mana
    /// cost" alternative cost (CR 118.9). The caller pre-picks the two
    /// Mountains the caster controls; the spell-cast flow validates via
    /// <see cref="SacrificeTwoLandsAlternativeCost.CanCastFor"/> and applies
    /// the sacrifice in <c>OnResolved</c>.
    /// </summary>
    public static SacrificeTwoLandsAlternativeCost BuildSacrificeMountainsCost(IReadOnlyList<ICard> mountains)
    {
        ArgumentNullException.ThrowIfNull(mountains);
        return new SacrificeTwoLandsAlternativeCost(SacrificeSubtype, mountains);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Fireblast is cast.
    /// Single 1..1 "any target" request, no X; on resolution deals
    /// <see cref="Damage"/> (4) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/> (CR 120.3).
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
                    Fx.Inline("Fireblast: 4 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
