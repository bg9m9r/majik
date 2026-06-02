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
        => Create(name, owner, effects: null);

    /// <summary>
    /// Effects-aware dispatch. When <paramref name="effects"/> is supplied and
    /// the named factory exposes a <c>Create(Player, ContinuousEffectsService)</c>
    /// overload (lord / anthem / equipment / aura cards), the card is built
    /// through that overload so its continuous <c>LordStaticEffect</c> /
    /// <c>AttachedBoostEffect</c> is registered against the live per-game
    /// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> (CR 613.7c).
    /// Cards without such an overload — and any call with a null
    /// <paramref name="effects"/> — fall back to the single-arg dispatch, which
    /// is behaviourally identical for them. This is the entry point the
    /// production <c>GameFacade</c> routed (instance-swap) build uses so static
    /// effects are no longer dropped in live matches.
    /// </summary>
    public static ICard Create(
        string name,
        Player owner,
        Majik.Core.Effects.ContinuousEffectsService? effects)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        if (owner == null) throw new ArgumentNullException(nameof(owner));

        // 0) Effects-aware dispatch — only for factories that expose a
        //    Create(Player, ContinuousEffectsService) overload. Returns null
        //    when the name has no such overload, so the single-arg path below
        //    takes over (identical result for non-static cards). Basic-land
        //    mana is reattached here for symmetry with the single-arg path
        //    (a manland factory's effects-aware overload could be dispatched
        //    through this general entrypoint, even though GameFacade's routed
        //    build skips lands).
        if (effects != null)
        {
            ICard? withEffects = CreateGeneratedWithEffects(name, owner, effects);
            if (withEffects != null)
            {
                withEffects.SetOwner(owner);
                if (withEffects is Land && withEffects.HasSupertype(CardSupertype.Basic))
                {
                    AttachBasicLandMana(withEffects, owner);
                }
                return withEffects;
            }
        }

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
            // Grizzly Bears / Runeclaw Bear / Hill Giant ALSO have real
            // [CardName] factories (GrizzlyBearsFactory etc.), but they are
            // kept here AND in ImplementedCardNames.InlineFallbackNames on
            // purpose: that keeps HasRealFactory() returning false for them,
            // so GameFacade does NOT do its "instance swap" rebuild for these
            // shells (which would replace a directly-constructed test
            // Creature with a JSON-built one mid-cast). Same posture as the
            // merged HillGiantFactory PR — see that factory's doc comment.
            "Grizzly Bears"   => new Creature(name, "1G", 2, 2),
            "Runeclaw Bear"   => new Creature(name, "1G", 2, 2),
            "Hill Giant"      => new Creature(name, "3R", 3, 3),

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
        // Idempotent: if the card already carries a mana ability (e.g. it was
        // built through the JSON-driven CardDefinitionFactory route, which
        // emits the intrinsic "{T}: Add {C}" ability from the card def), do
        // not attach a second identical one. A basic land has exactly one
        // intrinsic mana ability per its subtype (CR 305.6).
        if (land.Abilities.OfType<ManaAbility>().Any())
        {
            return;
        }

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
