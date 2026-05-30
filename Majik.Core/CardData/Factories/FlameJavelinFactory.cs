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
/// Named-card factory for Flame Javelin (Shadowmoor, {(2/R)}{(2/R)}{(2/R)}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "({2/R} can be paid with any two mana or with {R}. This card's mana
///    value is 6.)
///    Flame Javelin deals 4 damage to any target."
///
/// ## Implementation
///
/// Combines two already-supported shapes:
/// - <b>Resolve body</b>: the archetypal "deal N damage to any target" burn,
///   same shape as <see cref="LightningBoltFactory"/> /
///   <see cref="PlayWithFireFactory"/>, scaled to 4 damage. Routes through
///   <see cref="Fx.DealDamageAny"/> so all four "any target" classes resolve
///   correctly (CR 115.3 — creature, player, planeswalker, or battle).
///   CR 306.7 — damage to a planeswalker becomes loyalty removal; CR 309.5 —
///   damage to a battle becomes defense removal; both handled inside
///   <see cref="Fx.DealDamageAny"/>.
/// - <b>Monocolored-hybrid cost</b>: three twobrid pips {(2/R)} (CR 107.4e /
///   CR 202.3f), exactly mirroring <see cref="SpectralProcessionFactory"/>'s
///   {2/W}{2/W}{2/W}. <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>
///   reads each {2/R} pip as a hybrid pip with GenericAlternative = 2, so
///   <c>TotalValue</c> takes the higher generic alternative per pip and Flame
///   Javelin reports a mana value of 6 (CR 202.3f). Cast-time payment can
///   satisfy each pip with either 2 generic mana or 1 red mana — the engine's
///   existing hybrid cost-payer handles this (same path as Spectral
///   Procession / Boros Reckoner).
///
/// Card shape comes from the embedded JSON (<c>flame-javelin.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
/// </summary>
[CardName("Flame Javelin")]
public static class FlameJavelinFactory
{
    public const string CardName = "Flame Javelin";
    public const string Slug = "flame-javelin";

    /// <summary>
    /// Printed mana cost — three twobrid pips {(2/R)} (CR 107.4e). Each pip
    /// pays as {R} or {2}; mana value is 6 (CR 202.3f).
    /// </summary>
    public const string PrintedManaCost = "{2/R}{2/R}{2/R}";

    /// <summary>CR 119 — fixed 4 damage to any target.</summary>
    public const int Damage = 4;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Flame Javelin is
    /// cast. Single 1..1 "any target" request, no X; on resolution deals
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
                    Fx.Inline("Flame Javelin: 4 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
