using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stone Rain (Alpha / reprints, {2}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy target land."
///
/// ## Implementation
///
/// Pure land-destruction sorcery. The resolve-time body is exactly what the
/// data-driven <see cref="SpellTemplates.Templates.Destroy.DestroyLandTemplate"/>
/// produces from the oracle text — i.e. a single 1..1 "target land" request
/// whose resolution moves the chosen land to its owner's graveyard
/// (CR 701.7 destroy; an illegal pick does nothing per CR 608.2b). That
/// template is what binds Stone Rain in prod via
/// <see cref="OracleSpellBinder.Bind(CardEntity, Player, System.Func{object, object}, Majik.Core.Stack.Stack?)"/>;
/// no bespoke effect is needed here.
///
/// This factory therefore exists only to:
///   1. Flip <c>IsImplemented</c> on for Stone Rain — the flag is derived
///      from the <c>[CardName]</c> registry at load time, so the presence of
///      this attribute is the whole point.
///   2. Supply the card shape from the embedded JSON definition
///      (<c>stone-rain.json</c>) via
///      <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///      <see cref="CardDefinitionFactory"/>, matching the
///      <see cref="PlayWithFireFactory"/> shape.
///
/// <see cref="BuildSpellDefinition"/> mirrors the prod resolve path by
/// delegating straight to <see cref="OracleSpellBinder"/> with a synthetic
/// <see cref="CardEntity"/> carrying the oracle text, so the factory's
/// behaviour can never drift from the live <see cref="OracleSpellBinder"/>
/// binding.
/// </summary>
[CardName("Stone Rain")]
public static class StoneRainFactory
{
    public const string CardName = "Stone Rain";
    public const string Slug = "stone-rain";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>Oracle text — the only behaviour-bearing line.</summary>
    public const string OracleText = "Destroy target land.";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Stone Rain is cast.
    /// Delegates to <see cref="OracleSpellBinder"/> (the live prod binding via
    /// <see cref="SpellTemplates.Templates.Destroy.DestroyLandTemplate"/>), so
    /// this factory shares the exact "destroy target land" resolution used in
    /// game. Single 1..1 "target land" request; resolution moves the chosen
    /// land to its owner's graveyard (CR 701.7), and an illegal pick does
    /// nothing (CR 608.2b).
    /// </summary>
    /// <param name="caster">The player casting Stone Rain.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        var entity = new CardEntity
        {
            Name = CardName,
            ManaCost = PrintedManaCost,
            OracleText = OracleText,
        };

        return OracleSpellBinder.Bind(entity, caster, resolver, stack: null)
            ?? throw new InvalidOperationException(
                "OracleSpellBinder failed to bind Stone Rain's 'Destroy target land.' " +
                "Expected the DestroyLandTemplate to match.");
    }
}
