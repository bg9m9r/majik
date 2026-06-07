using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Bot.Tests.Integration.Helpers;

/// <summary>
/// Materialize a bot deck list (card names) into ICard instances.
/// Falls back to vanilla creatures / basic lands for cards lacking
/// real implementations - adequate for integration tests that just
/// need a 60-card pile to deal from.
/// </summary>
internal static class DeckLoader
{
    public static IReadOnlyList<ICard> Load(string archetype)
    {
        var names = Majik.Bot.Decks.BotDeckCatalog.Get(archetype);
        return names.Select(MaterializeFallback).Cast<ICard>().ToList();
    }

    /// <summary>
    /// Materialize an archetype's deck list into REAL typed shells resolved
    /// from the embedded card seed — correct types / mana / P-T / loyalty /
    /// color indicator, no abilities. This mirrors the server's
    /// <c>RealDeckLoader</c> shell path: abilities are NOT bound here; they
    /// are bound when the shells run through <see cref="Majik.Core.Api.GameFacade.Create"/>
    /// with a <c>cardRepo</c> (the same binder/factory chain production uses).
    ///
    /// <para>Throws if a deck-list name is absent from the seed — that is a
    /// real regression (every bot-deck name is vetted by <c>BotDeckValidator</c>
    /// at startup), not something to paper over with a vanilla fallback.</para>
    /// </summary>
    public static IReadOnlyList<ICard> LoadReal(string archetype, ICardRepository repo)
    {
        var names = Majik.Bot.Decks.BotDeckCatalog.Get(archetype);
        return names.Select(n => MaterializeReal(n, repo)).ToList();
    }

    /// <summary>
    /// Same as <see cref="LoadReal"/> but for the archetype's SIDEBOARD
    /// (wishboard) list — materializes <c>BotDeckCatalog.GetSideboard</c>
    /// names into real typed shells via the identical materialization path,
    /// so the audit can run them through <c>GameFacade.PopulateSideboard</c>.
    /// </summary>
    public static IReadOnlyList<ICard> LoadRealSideboard(string archetype, ICardRepository repo)
    {
        var names = Majik.Bot.Decks.BotDeckCatalog.GetSideboard(archetype);
        return names.Select(n => MaterializeReal(n, repo)).ToList();
    }

    private static ICard MaterializeReal(string name, ICardRepository repo)
    {
        var entity = repo.GetByName(name)
            ?? throw new InvalidOperationException(
                $"bot-deck card not in embedded seed: '{name}'");

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

        // CR 202.2c — stamp the printed color indicator (Dryad Arbor et al.)
        // so the shell mirrors the server loader before GameFacade rebinds.
        if (card is Card concrete)
        {
            var colors = CardColors.ParseScryfallColors(entity.Colors);
            if (colors.Count > 0) concrete.SetColorIndicator(colors);
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

    private static Card MaterializeFallback(string name)
    {
        if (Equals(name, "Island"))   return BasicLand(name, CardSubtype.Island);
        if (Equals(name, "Mountain")) return BasicLand(name, CardSubtype.Mountain);
        if (Equals(name, "Plains"))   return BasicLand(name, CardSubtype.Plains);
        if (Equals(name, "Swamp"))    return BasicLand(name, CardSubtype.Swamp);
        if (Equals(name, "Forest"))   return BasicLand(name, CardSubtype.Forest);

        // Non-basic land names (Sacred Foundry, Steam Vents, etc.) - any non-basic
        // land in the placeholder decks gets a generic Land for v1.
        if (name.Contains("Foundry") || name.Contains("Vents") || name.Contains("Sacred"))
            return new Land(name);

        // Non-land fallback: 1/1 vanilla creature so the pile can sit in a
        // library and be drawn without crashing.
        return new Creature(name, "{1}{R}", 1, 1);
    }

    private static Land BasicLand(string name, CardSubtype basicType)
        => new Land(
            name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { basicType });
}
