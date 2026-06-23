using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lunar Insight (Duskmourn: House of Horror, {2}{U}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Draw a card for each different mana value among nonland permanents you
///    control."
///
/// ## Implementation
/// Card shape (name, Sorcery, {2}{U}) comes from the embedded JSON
/// (<c>lunar-insight.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> — same JSON-backed posture as
/// <see cref="InspiringCallFactory"/>. No new mechanic: variable card-draw
/// (draw-N over a counted set, like <see cref="InspiringCallFactory"/>) where
/// the count is the number of <em>distinct</em> mana values across the caster's
/// nonland permanents.
///
/// The spell has no targets (CR 601 — "each different mana value among nonland
/// permanents you control" is a counted set evaluated at resolution, not a
/// target).
///
/// On resolve (CR 608.3 — one-shot resolution):
///   1. Enumerate the caster's battlefield permanents that are not lands
///      (CR 305.1 — a land is any permanent with the land card type). A
///      permanent's mana value (CR 202.3 / 202.3b) is read via
///      <see cref="Permanent.GetEffectiveManaValue"/> so a copy is measured by
///      its copied identity.
///   2. Count the <em>distinct</em> mana values among that set (CR 107.18 —
///      "different mana value" means each numeric value counts once no matter
///      how many permanents share it). Tokens with no mana cost have mana value
///      0 (CR 202.3a) and contribute the value 0 to the distinct set like any
///      other 0-cost nonland permanent.
///   3. Caster draws one card per distinct mana value (CR 121.1). The draw
///      routes through <see cref="Fx.DrawCards"/> (replacement bus per draw;
///      empty library stamps the CR 704.5b SBA loss flag without throwing).
///      Drawing zero (no nonland permanents) is a no-op.
/// </summary>
[CardName("Lunar Insight")]
public static class LunarInsightFactory
{
    public const string CardName = "Lunar Insight";
    public const string Slug = "lunar-insight";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Lunar Insight. No modes, no
    /// X, no target requests — the body resolves entirely on the caster's own
    /// board (CR 601 — counted set, not a target).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Count the distinct mana values among the caster's nonland battlefield
    /// permanents (CR 107.18 / CR 202.3). Shared so the test asserts the count
    /// directly and the resolve effect reuses it.
    /// </summary>
    public static int CountDistinctManaValues(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return caster.Zones.Battlefield
            .GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield && !p.HasType(CardType.Land))
            .Select(p => p.GetEffectiveManaValue())
            .Distinct()
            .Count();
    }

    /// <summary>
    /// Build the resolve effect: draw one card per distinct mana value among
    /// the caster's nonland permanents.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw one card per different mana value among nonland permanents you control.",
                () =>
                {
                    // CR 107.18 / CR 202.3 — count distinct mana values across
                    // nonland permanents; CR 121.1 — draw one per value.
                    // Fx.DrawCards no-ops on count 0 and routes the replacement
                    // bus per draw; an empty library stamps the SBA loss flag
                    // (CR 704.5b) without throwing.
                    Fx.DrawCards(caster, CountDistinctManaValues(caster));
                }),
        };
    }
}
