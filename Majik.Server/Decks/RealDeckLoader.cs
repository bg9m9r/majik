using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Server.Matches;

namespace Majik.Server.Decks;

/// <summary>Real <see cref="IDeckLoader"/> impl. Resolves deck-by-Guid,
/// validates against <see cref="DeckValidationService"/>, materializes
/// each <see cref="DeckCardEntry"/> through type-line parsing and returns a
/// fresh <see cref="ICard"/> instance per count so the engine sees distinct
/// game objects. Owner is not set here — callers assign ownership at game
/// start.</summary>
public sealed class RealDeckLoader : IDeckLoader
{
    private readonly DeckRepository _decks;
    private readonly ICardRepository _cards;
    private readonly DeckValidationService _validator;

    public RealDeckLoader(DeckRepository decks, ICardRepository cards, DeckValidationService validator)
    {
        _decks = decks;
        _cards = cards;
        _validator = validator;
    }

    public async Task<IReadOnlyList<ICard>> LoadAsync(string deckId, CancellationToken ct)
    {
        if (!Guid.TryParse(deckId, out var id))
        {
            throw new DeckLoadException($"deck id not a guid: {deckId}");
        }

        var deck = await _decks.GetByIdAsync(id, ct);
        if (deck == null)
        {
            throw new DeckLoadException($"deck not found: {deckId}");
        }

        var result = _validator.Validate(deck);
        if (!result.IsValid)
        {
            throw new DeckLoadException(
                $"deck {deck.Name} invalid: {string.Join("; ", result.Errors)}");
        }

        var cards = new List<ICard>(capacity: deck.Mainboard.Sum(e => e.Count));
        foreach (var entry in deck.Mainboard)
        {
            var entity = _cards.GetByName(entry.Name)
                ?? throw new DeckLoadException($"unknown card at load time: {entry.Name}");
            for (var i = 0; i < entry.Count; i++)
            {
                cards.Add(CreateCard(entity));
            }
        }
        return cards;
    }

    /// <summary>Instantiates a typed <see cref="ICard"/> shell from a
    /// <see cref="CardEntity"/> without setting an owner. Mirrors the
    /// type-dispatch in <see cref="ScryfallCardFactory"/> but omits ability
    /// binding which requires a live <see cref="Majik.Core.Players.Player"/>.</summary>
    private static ICard CreateCard(CardEntity entity)
    {
        var parsed = TypeLineParser.Parse(entity.TypeLine);
        var manaCost = entity.ManaCost ?? "";

        return PickPrimaryType(parsed.Types) switch
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
    }

    private static CardType? PickPrimaryType(IEnumerable<CardType> types)
    {
        var priority = new[]
        {
            CardType.Creature, CardType.Land, CardType.Instant, CardType.Sorcery,
            CardType.Enchantment, CardType.Artifact, CardType.Planeswalker,
        };
        foreach (var p in priority)
            if (types.Contains(p)) return p;
        return null;
    }

    private static int ParseStat(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return int.TryParse(s, out var v) ? v : 0;
    }
}

public sealed class DeckLoadException : Exception
{
    public DeckLoadException(string message) : base(message) { }
}
