using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Augur of Autumn (Innistrad: Midnight Hunt, {1}{G}{G}).
///
/// Creature — Human Druid 2/3. Oracle text:
///   "You may look at the top card of your library any time.
///    You may play lands from the top of your library.
///    Coven — As long as you control three or more creatures with different
///    powers, you may cast creature spells from the top of your library."
///
/// ## Shape source
/// Card identity (name, {1}{G}{G}, 2/3, Creature — Human Druid) is loaded from
/// <c>Majik.Core/CardData/Cards/augur-of-autumn.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The three oracle riders are attached in
/// code below as description-only <see cref="StaticAbility"/> entries.
///
/// ## Implemented (v1 — simplified)
/// Augur's three riders all operate off the top card of the controller's
/// library via continuous "permission to play/cast from a zone other than
/// hand" effects. The engine has no live "may play lands from library" or "may
/// cast creature spells from library" primitive — confirmed via grep over
/// Majik.Core/Rules + Majik.Core/Services + Majik.Core/Effects; the only other
/// reference to "play from top of library" in the codebase is
/// <see cref="ConspicuousSnoopFactory"/>, which ships the same documented-gap
/// posture. There is likewise no Coven keyword/mechanic in the engine.
///
/// Rather than half-build that cast-from-zone permission infrastructure, v1
/// ships the card shape with three <see cref="StaticAbility"/> riders carrying
/// their printed descriptions (so audits, dispatcher tests, and bot decision
/// surfaces see Augur as a top-of-library-value creature) plus pure-read
/// controller-side helpers:
///   - <see cref="LookAtTopOfLibrary"/> — the "look at the top card any time"
///     peek (CR 401.4 lets a player look at the top card whenever an effect
///     grants it). Returns the top card or null when the library is empty.
///   - <see cref="HasCoven"/> — the Coven condition (control three or more
///     creatures with different powers). Used to gate the "cast creature spells
///     from the top" rider once cast-from-zone permission exists.
///
/// This mirrors <see cref="ConspicuousSnoopFactory"/> ("play with the top card
/// revealed" + "may cast top if Goblin") exactly: card shape + description
/// riders + controller-side peek, with the continuous cast-from-zone wiring
/// deferred.
///
/// ## Deferred (v1 gaps — documented)
/// - <b>Play lands from the top of your library</b>: needs a "may play lands
///   from library" permission primitive (none exists — same gap that blocks
///   Courser of Kruphix, Oracle of Mul Daya, Conspicuous Snoop).
/// - <b>Cast creature spells from the top (Coven-gated)</b>: needs a
///   continuous "permission to cast from a zone other than hand" primitive plus
///   a live Coven static-condition evaluator. Same cast-from-zone gap blocks
///   Bolas's Citadel, Vivien (Champion of the Wilds), Conspicuous Snoop.
/// - <b>Coven as a live conditional grant</b>: <see cref="HasCoven"/> evaluates
///   the condition on demand, but it is not wired as a continuous static
///   ability that toggles the cast-from-top permission as the board changes.
/// </summary>
[CardName("Augur of Autumn")]
public static class AugurOfAutumnFactory
{
    public const string CardName = "Augur of Autumn";

    public const string LookAtTopDescription =
        "You may look at the top card of your library any time.";

    public const string PlayLandsFromTopDescription =
        "You may play lands from the top of your library.";

    public const string CovenCastFromTopDescription =
        "Coven — As long as you control three or more creatures with different powers, you may cast creature spells from the top of your library.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("augur-of-autumn");

    /// <summary>
    /// Construct Augur of Autumn. Identity comes from the embedded JSON
    /// definition; the three oracle riders are attached as description-only
    /// <see cref="StaticAbility"/> entries (CR 604.1 — static abilities function
    /// while the permanent is on the battlefield). See class doc for the v1
    /// scope vs. deferred items.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 604.1 — static abilities. Three riders, each carrying its printed
        // description for audit / bot-surface visibility. No live
        // continuous-effect wiring yet (see class doc).
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: LookAtTopDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: PlayLandsFromTopDescription));

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: CovenCastFromTopDescription));

        return card;
    }

    /// <summary>
    /// Augur's "look at the top card of your library any time" rider as a
    /// controller-side peek (CR 401.4). Returns the top card of
    /// <paramref name="controller"/>'s library, or null when the library is
    /// empty. Pure read — no zone mutation, no event publish.
    /// </summary>
    public static ICard? LookAtTopOfLibrary(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }

    /// <summary>
    /// The Coven condition: <paramref name="controller"/> controls three or
    /// more creatures with different powers (the printed Coven definition).
    /// Counts the distinct effective <see cref="Creature.Power"/> values among
    /// creatures the player controls on the battlefield; Coven is active when
    /// that count is three or more. Returns false when fewer than three
    /// distinct powers are present.
    /// </summary>
    public static bool HasCoven(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var distinctPowers = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => ReferenceEquals(c.Controller, controller))
            .Select(c => c.Power)
            .Distinct()
            .Count();

        return distinctPowers >= 3;
    }
}
