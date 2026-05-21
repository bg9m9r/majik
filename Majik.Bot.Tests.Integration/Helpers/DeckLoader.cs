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
