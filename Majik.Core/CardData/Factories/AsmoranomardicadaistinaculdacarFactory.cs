using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Asmoranomardicadaistinaculdacar (Modern Horizons
/// 2, {B}{R}{G}). The longest-name legendary in Magic.
///
/// Legendary Creature — Human Shaman 4/4. Oracle text:
///   "You may look at the top card of your library any time.
///    You may cast Food spells from the top of your library.
///    {T}: Search your library for a Food card, reveal it, put it into
///    your hand, then shuffle. Activate only as a sorcery."
///
/// ## Implemented (v1)
///
/// - Legendary Creature — Human Shaman 4/4, mana cost {B}{R}{G},
///   owner / controller stamped (CR 205.4a — supertype Legendary; CR 704.5j
///   legend rule SBA fires automatically once two copies share the
///   battlefield).
/// - <b>{T}: tutor a Food card to hand. Activate only as a sorcery.</b>
///   <see cref="ActivatedAbility"/> (CR 602.1) with a single
///   <see cref="AdditionalCost.Tap"/> cost and the <c>sorcerySpeed: true</c>
///   rider (CR 117.1a / CR 307.5 — <see cref="Rules.ActionValidator"/>
///   rejects activations outside the controller's main phase or with a
///   non-empty stack). Resolution body filters the controller's library to
///   <see cref="CardSubtype.Food"/> artifact cards (CR 205.3 — Food is an
///   artifact subtype), consults the controller's agent for a pick (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) with a
///   first-candidate deterministic fallback when no agent is registered,
///   moves the picked card Library → Hand, then shuffles (CR 701.20a).
///   Empty-candidate / null-pick path is a clean no-op (CR 701.19a permits
///   declining to find).
///
/// ## Deferred (v1 gaps)
///
/// The two static abilities are NOT yet wired — both require primitives
/// the engine doesn't ship yet:
///
/// - <b>"You may look at the top card of your library any time."</b>
///   Needs a <see cref="StaticAbility"/> that grants the controller a
///   permanent "look at top" permission (CR 702.91 / similar to Lurking
///   Predators, Magus of the Future). Existing
///   <see cref="Effects.ContinuousEffectsService"/> doesn't model the
///   hidden-information permission slot — adding it requires plumbing
///   through <see cref="Players.Player.Zones.Library"/>'s reveal surface
///   and the agent's hidden-information view. Asmoran ships without it;
///   bot evaluation treats the library as opaque (no peek bonus).
///
/// - <b>"You may cast Food spells from the top of your library."</b>
///   Needs a cast-from-zone alternative (CR 117.7c) gated on
///   (a) the card being on top of the library and (b) the card being a
///   Food card. Closest sibling primitive is
///   <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/> + a
///   conspiracy-style "permission to cast from non-hand zone" hook used
///   by <see cref="Majik.Core.CardData.Factories.ConspicuousSnoopFactory"/>
///   for top-of-library casts of Goblins. Adapting that infrastructure to
///   Asmoran needs a Food-subtype predicate + the "top of library" gate
///   to live on Asmoran's static ability. Deferred. Bot evaluation will
///   not attempt to cast a Food from the top of the library; Asmoran will
///   typically pair with Underworld Cookbook + Witch's Oven where the
///   tutor + token-creation engine still functions without the cast-from-
///   library grant.
///
/// Both static abilities are surfaced as <see cref="KeywordAbility"/>
/// markers with descriptive keyword names so the card's
/// <see cref="ICard.Abilities"/> collection still advertises the printed
/// shape (helps the bot's oracle-aware scorers + makes the gap easy to
/// spot in factory diagnostics). The marker is a no-op behaviourally —
/// CombatAbilities + the engine's keyword interpreters do not consume
/// these custom strings.
/// </summary>
[CardName("Asmoranomardicadaistinaculdacar")]
public static class AsmoranomardicadaistinaculdacarFactory
{
    public const string CardName = "Asmoranomardicadaistinaculdacar";
    public const string PrintedManaCost = "{B}{R}{G}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>Descriptive marker for the "may look at top card of
    /// library" static ability (CR 702.x). Behaviourally a no-op at v1;
    /// see factory xmldoc for the deferred primitive.</summary>
    public const string MayLookAtTopOfLibraryMarker =
        "You may look at the top card of your library any time.";

    /// <summary>Descriptive marker for the "may cast Food spells from the
    /// top of your library" static ability (CR 117.7c). Behaviourally a
    /// no-op at v1; see factory xmldoc for the deferred primitive.</summary>
    public const string MayCastFoodFromLibraryMarker =
        "You may cast Food spells from the top of your library.";

    /// <summary>
    /// Construct Asmoranomardicadaistinaculdacar owned and controlled by
    /// <paramref name="owner"/>. The tutor-to-hand activated ability is
    /// attached; the two static abilities ship as descriptive
    /// <see cref="KeywordAbility"/> markers (see factory xmldoc).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Descriptive static-ability markers — see factory xmldoc for the
        // primitive gaps that block real wiring. Surfaced as
        // KeywordAbility markers so card.Abilities still advertises the
        // printed shape (matches how Insolent Neonate / Slickshot Show-Off
        // surface their printed keyword markers).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(MayLookAtTopOfLibraryMarker, card, owner));
        card.AddAbility(new KeywordAbility(MayCastFoodFromLibraryMarker, card, owner));

        // ----------------------------------------------------------------
        // {T}: Search your library for a Food card, reveal it, put it
        // into your hand, then shuffle. Activate only as a sorcery.
        // CR 602.1 — activated ability. CR 117.1a / 307.5 — sorcery-speed
        // restriction via ActivatedAbility's `sorcerySpeed: true` rider
        // (enforced by ActionValidator). CR 701.19a — search consults
        // agent; CR 701.20a — shuffle after the search.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: tutor a Food card to hand",
            () =>
            {
                var controller = card.Controller ?? owner;
                ResolveFoodTutor(controller);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { tutorEffect },
            sorcerySpeed: true);

        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// Shared resolve body for the {T} activation. Filters
    /// <paramref name="controller"/>'s library to Food-subtyped artifact
    /// cards (CR 205.3); agent-driven pick with first-candidate fallback
    /// (matches WorldlyTutorFactory's posture); moves Library → Hand and
    /// shuffles (CR 701.20a).
    /// </summary>
    private static void ResolveFoodTutor(Player controller)
    {
        bool Pred(ICard c) =>
            c.HasType(CardType.Artifact) && c.HasSubtype(CardSubtype.Food);

        var candidates = controller.Zones.Library.GetCards().Where(Pred).ToList();
        if (candidates.Count == 0)
        {
            // CR 701.19a — no Food card found. Still shuffle (per the
            // printed oracle's "then shuffle" clause executing
            // unconditionally) so the search information is washed out.
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "asmoran-tutor");
            return;
        }

        var agent = AgentRegistry.Get(controller);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(
                ctx: null,
                candidates,
                "Food card")
                .GetAwaiter().GetResult()
            : candidates[0];

        if (pick == null)
        {
            // CR 701.19a — agent may decline to find. Still shuffle.
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "asmoran-tutor");
            return;
        }

        controller.Zones.Library.RemoveCard(pick);
        controller.Zones.Hand.AddCard(pick);
        pick.SetZone(ZoneType.Hand);
        // CR 701.20a — shuffle after the search.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "asmoran-tutor");
    }
}
