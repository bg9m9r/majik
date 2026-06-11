using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.CardData;

/// <summary>
/// Single source of truth for materializing a typed, owner-less
/// <see cref="ICard"/> SHELL from a <see cref="CardEntity"/> seed row —
/// the shape every deck-loading path needs BEFORE the binder/factory chain
/// (run later by <see cref="Majik.Core.Api.GameFacade.Create"/>) attaches
/// abilities. Used by the production deck loader (<c>RealDeckLoader</c>) and
/// by the bot/audit test materializers so they cannot drift apart.
///
/// <para><b>CR 205.1b — a card can have more than one card type.</b> The
/// concrete <see cref="Card"/> subclasses (<see cref="Land"/>,
/// <see cref="Creature"/>, …) each register only their OWN single type, so a
/// dual-type printed card (artifact land, enchantment land, artifact
/// creature, Vehicle) would silently lose its secondary type if we only
/// constructed the primary shell. We pick a primary type for the concrete
/// subclass, then additively flag every OTHER parsed type via
/// <see cref="Card.AddCardType"/>. Without this, Darksteel Citadel /
/// Urza's Saga / Esper Sentinel were NOT artifacts/enchantments in real
/// games, breaking every artifact-matters / enchantment-matters interaction
/// (Affinity, Mox Opal metalcraft, Cranial Plating, Stoneforge, the card's
/// own "artifact" identity).</para>
/// </summary>
public static class DeckCardShellBuilder
{
    /// <summary>
    /// Build the typed shell for <paramref name="entity"/>, preserving ALL
    /// printed card types (CR 205.1b) and stamping the printed color
    /// indicator (CR 202.2c). Does NOT set an owner — callers assign
    /// ownership at game start — and does NOT bind abilities; that happens in
    /// the GameFacade binder/factory chain.
    /// </summary>
    public static ICard Build(CardEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var parsed = TypeLineParser.Parse(entity.TypeLine);
        var manaCost = entity.ManaCost ?? "";

        ICard card = PickPrimaryType(parsed.Types) switch
        {
            CardType.Creature => new Creature(
                entity.Name, manaCost,
                ParseStat(entity.Power), ParseStat(entity.Toughness),
                parsed.Supertypes, parsed.Subtypes),
            CardType.Land => new Land(entity.Name, parsed.Supertypes, parsed.Subtypes),
            CardType.Instant => new Instant(entity.Name, manaCost),
            CardType.Sorcery => new Sorcery(entity.Name, manaCost),
            CardType.Enchantment => new Enchantment(entity.Name, manaCost, parsed.Supertypes, parsed.Subtypes),
            CardType.Artifact => new Artifact(entity.Name, manaCost, parsed.Supertypes, parsed.Subtypes),
            CardType.Planeswalker => new Planeswalker(
                entity.Name, manaCost,
                startingLoyalty: entity.Loyalty ?? 0,
                parsed.Supertypes, parsed.Subtypes),
            _ => new Card(entity.Name, manaCost, parsed.Types, parsed.Supertypes, parsed.Subtypes),
        };

        // CR 205.1b — preserve EVERY other parsed card type. The concrete
        // subclass above only registered its primary; an artifact land's
        // Artifact, Urza's Saga's Enchantment, Esper Sentinel's Artifact, a
        // Vehicle's Creature, etc. must be added so HasType reports them.
        // The fallthrough Card(parsed.Types) branch already carries the full
        // set; AddCardType is idempotent there.
        if (card is Card concrete)
        {
            foreach (var type in parsed.Types)
            {
                concrete.AddCardType(type);
            }

            // CR 202.2c — stamp the printed color indicator (parsed from the
            // seed's `colors` JSON) so CardColors.GetColors yields the right
            // answer for Dryad Arbor and any other indicator-only card. Plain
            // mana-cost colors are duplicate-safe; the indicator is unioned
            // with the mana-cost pip scan, not substituted for it.
            var colors = CardColors.ParseScryfallColors(entity.Colors);
            if (colors.Count > 0)
            {
                concrete.SetColorIndicator(colors);
            }
        }

        return card;
    }

    private static CardType? PickPrimaryType(IReadOnlyList<CardType> types)
    {
        foreach (var p in new[]
        {
            CardType.Creature, CardType.Land, CardType.Instant, CardType.Sorcery,
            CardType.Enchantment, CardType.Artifact, CardType.Planeswalker,
        })
        {
            if (types.Contains(p)) return p;
        }
        return null;
    }

    private static int ParseStat(string? s)
        => int.TryParse(s, out var v) ? v : 0;
}
