using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// Convenience builder for the handful of cards tests construct without
/// hitting the DB. Produces typed Card subclasses with the minimum
/// ability set (basic lands get a tap-for-mana ability inline; other
/// cards are vanilla).
///
/// Production code paths go through <see cref="ScryfallCardFactory"/>
/// instead — that route runs the full data-driven binders against
/// real Scryfall rows.
///
/// ## Dispatch
///
/// The bulk of the dispatch table is generated at compile time by
/// <c>Majik.Core.SourceGen.NamedCardFactoryGenerator</c>, which scans
/// every class annotated with <see cref="Factories.CardNameAttribute"/>
/// and emits a partial <c>CreateGenerated</c> method on this class.
/// Adding a new named-card factory is therefore "drop a file with
/// <c>[CardName("...")]</c>" — no edits to this file required.
///
/// Basic lands and the vanilla test-only creatures remain inline because
/// they construct the runtime card from scratch (no Factory class) and
/// share an inline mana ability attached after construction.
/// </summary>
public static partial class NamedCardFactory
{
    public static ICard Create(string name, Player owner)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        if (owner == null) throw new ArgumentNullException(nameof(owner));

        // 1) Compile-time-generated dispatch table — populated from
        //    [CardName] attributes on each *Factory class. Returns null
        //    when the name is not registered.
        ICard? card = CreateGenerated(name, owner);

        // 2) Inline fallbacks — basic lands (need a per-instance mana
        //    ability) and a few vanilla creatures the test suite leans
        //    on. Kept hand-maintained because they construct Card
        //    subclasses directly rather than dispatching to a *Factory.
        card ??= name switch
        {
            // Basic lands — given an inline mana ability so the simplest
            // tests don't need a fake repo just to pay {R}, etc.
            "Mountain" => Land(name, CardSubtype.Mountain),
            "Forest"   => Land(name, CardSubtype.Forest),
            "Plains"   => Land(name, CardSubtype.Plains),
            "Island"   => Land(name, CardSubtype.Island),
            "Swamp"    => Land(name, CardSubtype.Swamp),
            "Wastes"   => Land(name, CardSubtype.Wastes),

            // A few common vanilla creatures the test suite relies on.
            "Grizzly Bears"   => new Creature(name, "1G", 2, 2),
            "Runeclaw Bear"   => new Creature(name, "1G", 2, 2),
            "Hill Giant"      => new Creature(name, "3R", 3, 3),
            "Centaur Courser" => new Creature(name, "2G", 3, 3),

            // 3) Unknown name — vanilla shell. Mirrors the historical
            //    behaviour of the original 317-arm switch.
            _ => new Card(name, ""),
        };

        card.SetOwner(owner);

        if (card is Land && card.HasSupertype(CardSupertype.Basic))
        {
            AttachBasicLandMana(card, owner);
        }
        return card;
    }

    private static Land Land(string name, CardSubtype subtype) =>
        new(name, new[] { CardSupertype.Basic }, new[] { subtype });

    private static void AttachBasicLandMana(ICard land, Player controller)
    {
        var color = land.HasSubtype(CardSubtype.Mountain) ? "R"
                  : land.HasSubtype(CardSubtype.Forest)   ? "G"
                  : land.HasSubtype(CardSubtype.Plains)   ? "W"
                  : land.HasSubtype(CardSubtype.Island)   ? "U"
                  : land.HasSubtype(CardSubtype.Swamp)    ? "B"
                  : land.HasSubtype(CardSubtype.Wastes)   ? "C"
                  : null;

        if (color != null)
        {
            land.AddAbility(new ManaAbility(land, controller, ManaCost.Parse(color)));
        }
    }
}
