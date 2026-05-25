using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conspicuous Snoop (Jumpstart, {R}).
///
/// Creature — Goblin Rogue 1/1. Oracle text:
///   "Play with the top card of your library revealed.
///    You may cast the top card of your library if it's a Goblin card.
///    Conspicuous Snoop has all activated abilities of the top card of
///    your library if that card's a Goblin card. (You still pay their costs.)"
///
/// ## Implemented (v1 — simplified)
/// Snoop's full oracle interleaves three continuous effects that operate
/// off the top card of the controller's library. The engine's existing
/// continuous-effects layering, "play from a zone other than hand"
/// permission system, and "copy abilities from another card" Layer 6
/// primitives are not yet wired to the data side (no "may play from
/// library" primitive exists today — confirmed via grep over
/// Majik.Core/Rules + Majik.Core/Services). Rather than block Goblin
/// tribal coverage on those infrastructure pieces, v1 ships the card
/// shape with three <see cref="StaticAbility"/> entries describing the
/// oracle text and a <see cref="LookAtTopOfLibrary"/> helper for the
/// "revealed top of library" semantics. Each static ability carries its
/// printed description so audits, dispatcher tests, and bot decision
/// surfaces see Snoop as a three-rider Goblin lord without the live
/// continuous-effect wiring.
///
/// What ships:
/// - 1/1 Creature — Goblin Rogue at {R}, owner/controller wired.
/// - Three description-only <see cref="StaticAbility"/> entries
///   corresponding to the three oracle clauses. Each is active only on
///   the battlefield (CR 604.1 — static abilities function while the
///   permanent is on the battlefield).
/// - <see cref="LookAtTopOfLibrary"/> helper exposes Snoop's "play with
///   the top card of your library revealed" effect as a controller-side
///   peek — returns the top card or null when the library is empty.
///   Mirrors how other "top-card visibility" cards (Future Sight, Magus
///   of the Future, Lurrus etiquette gates) expose the peek without
///   plumbing it through an agent-prompt.
///
/// What's deferred (v1 gaps — documented):
/// - <b>Top of library revealed</b>: today the engine doesn't publicly
///   broadcast top-card visibility through the event bus / agent layer.
///   The static ability description is wired so audits see it, but
///   opponents' agents won't see Snoop's top card as a public reveal —
///   only controller-side <see cref="LookAtTopOfLibrary"/> works.
/// - <b>May cast top of library if Goblin</b>: there is no
///   "MayPlayFromLibrary" primitive in the engine (verified via grep).
///   This needs a cast-from-zone permission infrastructure pass (similar
///   to <see cref="EscapeAlternativeCost"/>'s zone-of-origin gate, but
///   for a continuous "permission to cast" rather than per-cast
///   alternative-cost wiring). Same gap blocks Bolas's Citadel, Magus of
///   the Future, Vivien, Champion of the Wilds, etc.
/// - <b>Has all activated abilities of top if Goblin</b>: needs Layer 6
///   <c>GrantAbilityEffect</c> dynamic-source plumbing — copy ability list
///   from a non-self card and re-target each ability's <c>source</c> /
///   <c>controller</c> to Snoop. Engine has <see cref="LordStaticEffect"/>
///   for keyword grants (Goblin Warchief / Goblin Chieftain) but no
///   activated-ability copy. Same gap blocks Vesuvan Doppelganger,
///   Mirrorpool, etc.
///
/// Per the project's coverage-strategy memo, shipping the card shape with
/// documented gaps unblocks downstream tribal coverage (Goblin lord audits
/// can reference Snoop as present in the pool) while leaving the
/// infrastructure work to a follow-up. Same posture as
/// <see cref="GoblinPiledriverFactory"/>'s "live combat-attackers
/// provider" deferral.
/// </summary>
[CardName("Conspicuous Snoop")]
public static class ConspicuousSnoopFactory
{
    public const string CardName = "Conspicuous Snoop";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    public const string PlayRevealedDescription =
        "Play with the top card of your library revealed.";

    public const string MayCastGoblinDescription =
        "You may cast the top card of your library if it's a Goblin card.";

    public const string CopyActivatedAbilitiesDescription =
        "Conspicuous Snoop has all activated abilities of the top card of your library if that card's a Goblin card. (You still pay their costs.)";

    /// <summary>
    /// Construct Conspicuous Snoop. The three oracle riders are attached
    /// as description-only <see cref="StaticAbility"/> entries (active on
    /// the battlefield only per CR 604.1) — see class doc for the v1
    /// scope vs. deferred items.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 604.1 — static abilities. Three riders, each with its printed
        // description for audit / bot-surface visibility. No live
        // continuous-effect wiring yet (see class doc). IsActiveCheck
        // defaults to "active on the battlefield" via StaticAbility's
        // permanent-on-battlefield convention.
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: PlayRevealedDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: MayCastGoblinDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: CopyActivatedAbilitiesDescription));

        return card;
    }

    /// <summary>
    /// Helper exposing Snoop's "play with the top card of your library
    /// revealed" rider as a controller-side peek. Returns the top card of
    /// <paramref name="controller"/>'s library, or null when the library
    /// is empty. Pure read — no zone mutation, no event publish. Bot /
    /// decision surfaces use this to consult Snoop's revealed top card
    /// when computing "is the top card a Goblin? can I cast it?" lines.
    /// </summary>
    public static ICard? LookAtTopOfLibrary(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }

    /// <summary>
    /// True when the top card of <paramref name="controller"/>'s library is
    /// a Goblin card. Predicate used by both the "may cast top if Goblin"
    /// rider and the "copy activated abilities if Goblin" rider. Returns
    /// false when the library is empty.
    /// </summary>
    public static bool IsTopOfLibraryGoblin(Player controller)
    {
        var top = LookAtTopOfLibrary(controller);
        return top != null && top.HasSubtype(CardSubtype.Goblin);
    }
}
