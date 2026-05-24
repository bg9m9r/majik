using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Karn, Scion of Urza (Dominaria, {4}).
///
/// Legendary Planeswalker — Karn, starting loyalty 5. Oracle text:
///   "+1: Reveal the top two cards of your library. An opponent separates
///        those cards into two piles. Put one pile into your hand and the
///        other on the bottom of your library in any order.
///    -1: Put a card you own exiled with Karn, Scion of Urza into your hand.
///    -2: Create a 0/0 colorless Construct artifact creature token with
///        'This creature gets +1/+1 for each artifact you control.'"
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 5, Karn subtype, mana cost {4}.
/// - <b>+1</b>: reveals the top two cards of the controller's library
///   ("reveal" is informational — the engine has no live "revealed" zone
///   surface, so the cards are simply read off the top of the library).
///   The opponent's separate-into-two-piles + you-choose-which-pile
///   prompt is collapsed in v1 to a deterministic split: the two cards
///   form two singleton piles; the higher-mana-value card goes to hand
///   and the other is placed on the bottom of the library (ties: first
///   card to hand). With one card available only that card is drawn;
///   with an empty library the effect no-ops (CR 701.13 / CR 121.x for
///   the empty-library SBA path elsewhere).
/// - <b>-1</b>: DEFERRED to a no-op body (loyalty change still applies
///   per CR 606.3). The engine has no "exiled with this source" tagging
///   surface yet — see "Deferred" below.
/// - <b>-2</b>: creates a 0/0 colorless Construct artifact creature
///   token under the controller via <see cref="TokenFactory.CreateOnBattlefield"/>.
///   The token is additively flagged with <see cref="CardType.Artifact"/>
///   so HasType-based lookups match both Creature + Artifact (CR 301.1 /
///   302.1 — same multi-type pattern as Wurmcoil Engine's Phyrexian Wurm
///   tokens and Esika's Chariot's vehicle shell). The "+1/+1 for each
///   artifact you control" rider is wired as a per-token
///   <see cref="CdaPowerToughnessEffect"/> (CR 613.7a) registered on the
///   supplied <see cref="ContinuousEffectsService"/> — evaluators read
///   the controller's battlefield at compute time so the token's P/T
///   tracks artifact-count dynamically. With no effects service wired
///   the token still enters the battlefield as a 0/0 (SBA 704.5f will
///   put it into the graveyard on the next SBA pass — caller should
///   provide a <see cref="ContinuousEffectsService"/> in production
///   paths).
///
/// ## Deferred (v1 gaps)
/// - <b>+1 pile-split prompt</b>: the opponent's separate-into-two-piles
///   choice + the controller's pick-which-pile choice are auto-resolved
///   to "higher-mv card to hand, other to bottom of library". This is
///   the same auto-pick posture as the rest of the planeswalker family
///   (Wrenn and Realmbreaker, Liliana of the Veil, Karn the Great
///   Creator). When the engine grows hidden-information separator
///   prompts the pile-split path can be lifted into an agent decision.
/// - <b>-1 exile recall</b>: requires "exiled with Karn" tag tracking on
///   exiled cards plus an exile-scan keyed to source identity. Same
///   queue as Lurrus of the Dream-Den's "from your graveyard" reach
///   and Karn the Great Creator's wishboard selector. v1 ships as a
///   no-op loyalty -1 ability so the loyalty change still pays
///   (CR 606.3) and the ability appears in the activatable shape.
/// - <b>Targeting prompts</b>: <see cref="LoyaltyAbility"/> doesn't
///   declare <see cref="Majik.Core.Targeting.TargetRequest"/>s yet, so
///   the opponent picker for the +1 isn't agent-driven (same gap as
///   <see cref="WrennAndRealmbreakerFactory"/>).
/// - <b>Token colour</b>: Construct token is created with no colour-set
///   primitive (matches Wurmcoil Engine + Crashing Footfalls token v1
///   gap — `CardColors.GetColors` reads mana cost; an empty mana cost
///   already implies colourless).
/// </summary>
[CardName("Karn, Scion of Urza")]
public static class KarnScionOfUrzaFactory
{
    public const string CardName = "Karn, Scion of Urza";
    public const string Cost = "{4}";
    public const int StartingLoyalty = 5;

    /// <summary>
    /// Construct Karn, Scion of Urza with no live runtime services. The
    /// +1 still operates on the controller's library directly; the -2
    /// still spawns a Construct token (without the dynamic P/T effect
    /// wired). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, zoneService: null, effects: null);

    /// <summary>
    /// Construct Karn, Scion of Urza with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service for routing the
    /// -2 token's battlefield entry through <see cref="ZoneService.MoveCard"/>
    /// (so ETB triggers like Soul Warden fire). May be null — token
    /// still enters via raw zone mutation.</param>
    /// <param name="effects">Optional continuous-effects service for
    /// registering the Construct token's CDA-style "+1/+1 for each
    /// artifact you control" P/T effect. May be null — token is created
    /// as a 0/0 with no live dynamic P/T (a same-turn SBA 704.5f pass
    /// will then send it to the graveyard).</param>
    public static Planeswalker Create(
        Player owner,
        ZoneService? zoneService,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var karn = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Karn });

        karn.SetOwner(owner);
        karn.SetController(owner);

        // -----------------------------------------------------------------
        // +1: Reveal the top two cards of your library. An opponent
        //     separates those cards into two piles. Put one pile into
        //     your hand and the other on the bottom of your library in
        //     any order.
        // v1: deterministic split — with 2+ cards, the higher-mv card
        // goes to hand; the other goes to the bottom of the library
        // (ties: first card to hand). With 1 card the card goes to
        // hand (single-pile case). With 0 cards the effect no-ops.
        // -----------------------------------------------------------------
        karn.AddAbility(new LoyaltyAbility(karn, +1, () =>
        {
            var top = owner.Zones.Library.GetCards().Take(2).ToList();
            if (top.Count == 0) return;

            if (top.Count == 1)
            {
                MoveToHand(owner, top[0]);
                return;
            }

            // Two cards: pick the higher mana-value card for hand;
            // bottom the other. Ties → first card (top of library) to hand.
            var a = top[0];
            var b = top[1];
            var aMv = ManaValueOf(a);
            var bMv = ManaValueOf(b);

            ICard toHand;
            ICard toBottom;
            if (aMv >= bMv)
            {
                toHand = a;
                toBottom = b;
            }
            else
            {
                toHand = b;
                toBottom = a;
            }

            MoveToHand(owner, toHand);
            MoveToLibraryBottom(owner, toBottom);
        }));

        // -----------------------------------------------------------------
        // -1: Put a card you own exiled with Karn, Scion of Urza into
        //     your hand.
        // DEFERRED — requires "exiled with this source" tag tracking on
        // exiled cards. v1 ships as a no-op so the loyalty change still
        // applies (CR 606.3) and the ability appears in the activatable
        // shape. See class xmldoc.
        // -----------------------------------------------------------------
        karn.AddAbility(new LoyaltyAbility(karn, -1, () =>
        {
            // Intentional no-op pending exile-source tagging.
        }));

        // -----------------------------------------------------------------
        // -2: Create a 0/0 colorless Construct artifact creature token
        //     with "This creature gets +1/+1 for each artifact you
        //     control."
        // The token is flagged Artifact + Creature (CR 301.1 / 302.1)
        // and the +1/+1 rider is registered on the supplied
        // ContinuousEffectsService as a Layer 7a CDA-style P/T effect
        // counting artifacts on the controller's battlefield at
        // compute time.
        // -----------------------------------------------------------------
        karn.AddAbility(new LoyaltyAbility(karn, -2, () =>
        {
            CreateConstructToken(owner, zoneService, effects);
        }));

        return karn;
    }

    /// <summary>
    /// Spawn the 0/0 Construct token. Exposed for tests; production
    /// callers go through the -2 loyalty body.
    /// </summary>
    public static Creature CreateConstructToken(
        Player controller,
        ZoneService? zoneService,
        ContinuousEffectsService? effects)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var spec = new TokenFactory.TokenSpec(
            Name: "Construct",
            Power: 0,
            Toughness: 0,
            Subtypes: new[] { CardSubtype.Construct });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 301.1 / 302.1 — "0/0 colorless Construct artifact creature
        // token". The base Creature constructor stamps CardType.Creature
        // only; additively flag Artifact so HasType lookups match both
        // types (mirrors Wurmcoil Engine's Phyrexian Wurm tokens).
        token.AddCardType(CardType.Artifact);

        if (effects != null)
        {
            // Wire the token to consult the layer system for P/T so the
            // dynamic +1/+1-per-artifact rider surfaces at GetPower /
            // GetToughness.
            token.ActiveEffects = effects;
            effects.Register(new CdaPowerToughnessEffect(
                token,
                _ => ArtifactCount(controller),
                _ => ArtifactCount(controller)));
        }

        return token;
    }

    /// <summary>Count the artifacts on <paramref name="controller"/>'s
    /// battlefield (CR 109.1 / 301.1). Includes artifact creatures and
    /// artifact lands.</summary>
    private static int ArtifactCount(Player controller)
    {
        var count = 0;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is Permanent perm && perm.HasType(CardType.Artifact))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Mana value used by the +1 split-pick. Reads the card's
    /// mana cost via <see cref="Card.ManaCostValue"/> when available;
    /// non-Card ICard instances (rare in the engine) fall back to 0.</summary>
    private static int ManaValueOf(ICard card)
    {
        if (card is Card c) return c.ManaCostValue.TotalValue;
        return 0;
    }

    private static void MoveToHand(Player owner, ICard card)
    {
        owner.Zones.Library.RemoveCard(card);
        owner.Zones.Hand.AddCard(card);
        if (card is Card c) c.SetZone(ZoneType.Hand);
    }

    private static void MoveToLibraryBottom(Player owner, ICard card)
    {
        // Library is stored top-at-index-0; Zone.AddCard appends → bottom.
        owner.Zones.Library.RemoveCard(card);
        owner.Zones.Library.AddCard(card);
        if (card is Card c) c.SetZone(ZoneType.Library);
    }
}
