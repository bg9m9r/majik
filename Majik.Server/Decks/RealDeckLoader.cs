using Majik.Core.CardData;
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

        var names = deck.Mainboard
            .SelectMany(e => Enumerable.Repeat(e.Name, e.Count))
            .ToList();
        return Materialize(names);
    }

    public Task<IReadOnlyList<ICard>> LoadFromCardNamesAsync(IReadOnlyList<string> cardNames, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cardNames);
        // No DB lookup, no DeckValidationService — bot decks are vetted at
        // startup by BotDeckValidator. Materialize directly.
        return Task.FromResult(Materialize(cardNames));
    }

    /// <summary>Resolves each name through <see cref="ICardRepository"/> and
    /// constructs a fresh typed <see cref="ICard"/> shell per occurrence. Shared
    /// by both deck-loading paths.</summary>
    private IReadOnlyList<ICard> Materialize(IReadOnlyList<string> names)
    {
        var cards = new List<ICard>(capacity: names.Count);
        foreach (var name in names)
        {
            var entity = _cards.GetByName(name)
                ?? throw new DeckLoadException($"unknown card at load time: {name}");
            cards.Add(CreateCard(entity));
        }
        return cards;
    }

    /// <summary>Instantiates a typed <see cref="ICard"/> shell from a
    /// <see cref="CardEntity"/> without setting an owner. Delegates to the
    /// shared <see cref="DeckCardShellBuilder"/> so the prod loader and the
    /// bot/audit test materializers stay in sync. The builder preserves ALL
    /// printed card types (CR 205.1b — so an artifact land is actually an
    /// Artifact, an enchantment land an Enchantment, etc.) and stamps the
    /// Scryfall <c>colors</c> array as a <see cref="Card.ColorIndicator"/>
    /// (CR 202.2c) so color-matters tutors (Green Sun's Zenith, Summoner's
    /// Pact) match cards like Dryad Arbor whose color comes from the printed
    /// indicator rather than from mana-cost pips. Ability binding happens
    /// later in the GameFacade binder/factory chain (which needs a live
    /// <see cref="Majik.Core.Players.Player"/>).</summary>
    private static ICard CreateCard(CardEntity entity)
        => DeckCardShellBuilder.Build(entity);
}

public sealed class DeckLoadException : Exception
{
    public DeckLoadException(string message) : base(message) { }
}
