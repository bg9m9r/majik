using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skewer the Critics (Ravnica Allegiance, {2}{R}).
///
/// Sorcery. Oracle text:
///   "Spectacle {R} (You may cast this spell for its spectacle cost rather
///    than its mana cost if an opponent lost life this turn.)
///    Skewer the Critics deals 3 damage to any target."
///
/// ## Implemented
/// - Sorcery shape, printed mana cost {2}{R} (mana value 3).
/// - <b>Resolve</b> (via <see cref="BuildSpellDefinition"/>): 1..1 "any target"
///   request; on resolution deals 3 damage through <see cref="Fx.DealDamageAny"/>
///   (CR 115.3 — creature / player / planeswalker / battle targets all handled).
///   Mirrors <see cref="LightningStrikeFactory"/> body exactly (same 3 damage,
///   same any-target shape; the only differences are Sorcery type and mana cost).
/// - <b>Spectacle {R}</b> alternative cost (CR 702.118): exposed via
///   <see cref="BuildSpectacleCost"/>, which routes through
///   <see cref="SpectacleBinder.TryBind"/> against <see cref="OracleText"/>.
///   Mirrors <see cref="LightUpTheStageFactory.BuildSpectacleCost"/> exactly.
/// </summary>
[CardName("Skewer the Critics")]
public static class SkewerTheCriticsFactory
{
    public const string CardName = "Skewer the Critics";
    public const string PrintedManaCost = "{2}{R}";
    public const int Damage = 3;

    /// <summary>
    /// Oracle text used by <see cref="BuildSpectacleCost"/> to derive the
    /// spectacle cost via <see cref="SpectacleBinder.TryBind"/>. Kept on
    /// the factory so the production load path (Scryfall row → oracle text
    /// → binder) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "Spectacle {R} (You may cast this spell for its spectacle cost rather than its mana cost if an opponent lost life this turn.)\n"
        + "Skewer the Critics deals 3 damage to any target.";

    /// <summary>
    /// Construct Skewer the Critics as a Sorcery owned/controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Skewer the Critics
    /// is cast. Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (3) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/> (CR 115.3).
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
                    Fx.Inline("Skewer the Critics: 3 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }

    /// <summary>
    /// Build the Spectacle {R} alternative cost (CR 702.118) by routing
    /// <see cref="OracleText"/> through <see cref="SpectacleBinder.TryBind"/>.
    /// Returns <c>null</c> when no opponent has lost life this turn — the
    /// caller falls back to the printed mana cost. Mirrors
    /// <see cref="LightUpTheStageFactory.BuildSpectacleCost"/>.
    /// </summary>
    public static SpectacleAlternativeCost? BuildSpectacleCost(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        return SpectacleBinder.TryBind(OracleText, caster, allPlayers);
    }
}
