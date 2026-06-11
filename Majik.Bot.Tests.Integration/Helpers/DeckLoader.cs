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

    // Delegates to the shared DeckCardShellBuilder so the audit/test shell is
    // the same shape the server's RealDeckLoader produces — including all
    // printed card types (CR 205.1b: an artifact land is an Artifact) and the
    // printed color indicator (CR 202.2c). GameFacade rebinds abilities after.
    private static ICard MaterializeReal(string name, ICardRepository repo)
    {
        var entity = repo.GetByName(name)
            ?? throw new InvalidOperationException(
                $"bot-deck card not in embedded seed: '{name}'");

        return DeckCardShellBuilder.Build(entity);
    }

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
