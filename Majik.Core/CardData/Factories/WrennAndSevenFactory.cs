using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wrenn and Seven (Innistrad: Midnight Hunt,
/// {4}{G}).
///
/// Legendary Planeswalker — Wrenn, starting loyalty 5. Oracle text
/// (verified against Scryfall):
///   "+1: Reveal the top four cards of your library. Put all land cards
///        revealed this way into your hand and the rest into your graveyard.
///    0: Put any number of land cards from your hand onto the battlefield
///        tapped.
///    −3: Create a green Treefolk creature token with reach and 'This
///        token's power and toughness are each equal to the number of lands
///        you control.'
///    −8: Return all permanent cards from your graveyard to your hand. You
///        get an emblem with 'You have no maximum hand size.'"
///
/// ## Implemented (v1)
/// - Legendary Planeswalker, loyalty 5, Wrenn subtype, mana cost {4}{G}.
/// - <b>+1</b>: reveals (partitions) the top four cards of the controller's
///   library — every land card goes to hand, the rest to the graveyard
///   (CR 701.15 reveal; the partition is mandatory). Reveals as many cards
///   as exist when the library has fewer than four.
/// - <b>0</b>: puts every land card from the controller's hand onto the
///   battlefield tapped (CR 305.9 — "put onto the battlefield" is not a land
///   drop). "Any number" — v1 auto-picks all land cards in hand, the
///   maximal legal choice (same auto-resolve posture as the other Wrenn
///   planeswalkers — <see cref="WrennAndSixFactory"/> / loyalty abilities
///   don't yet declare interactive choices).
/// - <b>−3</b>: creates a green Treefolk creature token with reach
///   (<see cref="TokenFactory.CreateOnBattlefield"/>) whose power and
///   toughness are each "equal to the number of lands you control" — a
///   characteristic-defining ability (CR 604.3 / 613.2 Layer 7a) wired via
///   <see cref="CdaPowerToughnessEffect"/> when a
///   <see cref="ContinuousEffectsService"/> is supplied (token-with-CDA
///   precedent: <see cref="AdelineResplendentCatharFactory"/>). The
///   evaluator counts <see cref="CardType.Land"/> cards on the controller's
///   battlefield, read fresh on every Compute so it tracks lands entering /
///   leaving live.
/// - <b>−8 ultimate</b>: returns all permanent cards (CR 110.4a — artifact /
///   creature / enchantment / land / planeswalker / battle cards) from the
///   controller's graveyard to hand, then mints an emblem in the
///   controller's command zone (CR 114).
///
/// ## Deferred (v1 gaps)
/// - <b>Interactive choices</b>: the +1 reveal is non-interactive (no agent
///   prompt — the partition is mechanical anyway), and the 0's "any number"
///   auto-picks every land in hand. <see cref="LoyaltyAbility"/> doesn't yet
///   declare interactive choices — same posture as
///   <see cref="WrennAndSixFactory"/> / <see cref="WrennAndRealmbreakerFactory"/>.
/// - <b>−8 emblem "no maximum hand size"</b>: the engine does not enforce a
///   maximum hand size (no cleanup-step discard-to-seven is run — see
///   <see cref="ReliquaryTowerFactory"/> / <see cref="SeaGateRestorationFactory"/>),
///   so the emblem's rider is structural only. The emblem is minted with no
///   live ability so it shows up in <see cref="Player.Emblems"/> for log /
///   UI; once the maximum-hand-size SBA is wired this clause becomes a no-op
///   confirmation rather than new infra.
/// - <b>Shape-path −3 token CDA</b>: the single-argument
///   <see cref="Create(Player)"/> path mints the token but, with no
///   <see cref="ContinuousEffectsService"/>, the CDA P/T is not registered;
///   the token keeps its seeded 0/0 until a real game's effects service is
///   wired. Use <see cref="Create(Player, ContinuousEffectsService, IEventBus?, Func{IEnumerable{ICard}})"/>
///   for the live CDA (same overload posture as
///   <see cref="AdelineResplendentCatharFactory"/>).
/// </summary>
[CardName("Wrenn and Seven")]
public static class WrennAndSevenFactory
{
    public const string CardName = "Wrenn and Seven";
    public const string Cost = "{4}{G}";
    public const int StartingLoyalty = 5;

    /// <summary>Token created by the −3 — a green Treefolk with reach.</summary>
    public const string TokenName = "Treefolk";
    public const string Reach = "Reach";

    /// <summary>
    /// Construct Wrenn and Seven with no live runtime wiring (the
    /// dispatcher / shape path — the overload <see cref="NamedCardFactory"/>
    /// dispatches to). The +1 / 0 / −8 operate on the controller's zones
    /// directly; the −3 mints the token but its CDA P/T is not registered
    /// (no effects service). Use the service overload for the live CDA.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, landsYouControlSource: null);

    /// <summary>
    /// Construct Wrenn and Seven with optional runtime services. When
    /// <paramref name="effects"/> and <paramref name="landsYouControlSource"/>
    /// are supplied, the −3 token's characteristic-defining P/T (CR 604.3 /
    /// 613.2 Layer 7a) registers against the continuous-effects service so it
    /// reads "the number of lands you control" live.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the −3 token's CDA
    /// (<see cref="CdaPowerToughnessEffect"/>) registers against. May be null —
    /// the token then keeps its seeded base P/T.</param>
    /// <param name="eventBus">Event bus for the token CDA's ETB/LTB lifecycle
    /// (<see cref="CardMovedEvent"/>). May be null — the CDA's battlefield gate
    /// still covers correctness, but no explicit unregister fires when the
    /// token leaves.</param>
    /// <param name="landsYouControlSource">Closure returning the cards to count
    /// for "lands you control" — typically
    /// <c>() =&gt; controller.Zones.Battlefield.GetCards()</c>. The CDA filters
    /// to <see cref="CardType.Land"/>. Read fresh on every Compute. May be null
    /// (token CDA not wired).</param>
    public static Planeswalker Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? landsYouControlSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var wrenn = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Wrenn });

        wrenn.SetOwner(owner);
        wrenn.SetController(owner);

        // -----------------------------------------------------------------
        // +1: Reveal the top four cards of your library. Put all land cards
        //     revealed this way into your hand and the rest into your
        //     graveyard.
        // CR 701.15 — reveal. The partition is mandatory (no choice): every
        // revealed land → hand, every non-land → graveyard. Reveals as many
        // as the library holds when fewer than four remain.
        // -----------------------------------------------------------------
        wrenn.AddAbility(new LoyaltyAbility(wrenn, +1, () =>
        {
            var controller = wrenn.Controller ?? owner;
            var top = controller.Zones.Library.GetCards().Take(4).ToList();
            foreach (var card in top)
            {
                controller.Zones.Library.RemoveCard(card);
                if (card.HasType(CardType.Land))
                {
                    controller.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
                else
                {
                    controller.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }
            }
        }));

        // -----------------------------------------------------------------
        // 0: Put any number of land cards from your hand onto the
        //    battlefield tapped.
        // CR 305.9 — putting a land onto the battlefield this way is NOT a
        // land play (doesn't consume the land drop). "Any number" — v1
        // auto-picks every land in hand (the maximal legal choice). The
        // lands enter tapped (CR 110.5 — a permanent enters tapped only if
        // an effect says so).
        // -----------------------------------------------------------------
        wrenn.AddAbility(new LoyaltyAbility(wrenn, 0, () =>
        {
            var controller = wrenn.Controller ?? owner;
            var lands = controller.Zones.Hand.GetCards()
                .Where(c => c.HasType(CardType.Land))
                .ToList();
            foreach (var land in lands)
            {
                controller.Zones.Hand.RemoveCard(land);
                controller.Zones.Battlefield.AddCard(land);
                land.SetZone(ZoneType.Battlefield);
                land.SetController(controller);
                // CR 110.5 — enters tapped. Guard against an already-tapped
                // state (Permanent.Tap throws if re-tapped); a freshly-placed
                // land is untapped, so this taps it exactly once.
                if (land is Permanent perm && !perm.IsTapped)
                {
                    perm.Tap();
                }
            }
        }));

        // -----------------------------------------------------------------
        // −3: Create a green Treefolk creature token with reach and "This
        //     token's power and toughness are each equal to the number of
        //     lands you control."
        // CR 111.4 — token characteristics. CR 604.3 / 613.2 Layer 7a — the
        // CDA P/T is wired via CdaPowerToughnessEffect when an effects
        // service is supplied (token-with-CDA precedent: Adeline).
        // -----------------------------------------------------------------
        wrenn.AddAbility(new LoyaltyAbility(wrenn, -3, () =>
        {
            var controller = wrenn.Controller ?? owner;

            // CR 111.4 — green Treefolk with reach. Seed 0/0; Layer 7a sets
            // the real P/T from "lands you control" on every Compute.
            var spec = new TokenFactory.TokenSpec(
                Name: TokenName,
                Power: 0,
                Toughness: 0,
                Subtypes: new[] { CardSubtype.Treefolk },
                Keywords: new[] { Reach },
                Colors: new[] { ManaColor.Green });

            var token = TokenFactory.CreateOnBattlefield(spec, controller);

            if (effects != null && landsYouControlSource != null)
            {
                var lifecycle = new TreefolkTokenCdaLifecycle(
                    token, effects, eventBus, landsYouControlSource);
                lifecycle.Attach();
            }
        }));

        // -----------------------------------------------------------------
        // −8 ultimate: Return all permanent cards from your graveyard to
        //     your hand. You get an emblem with "You have no maximum hand
        //     size."
        // CR 110.4a — a permanent card is an artifact / battle / creature /
        // enchantment / land / planeswalker card (instants / sorceries are
        // NOT permanent cards). The emblem's "no maximum hand size" rider is
        // structural only — the engine doesn't enforce a maximum hand size
        // (ReliquaryTower / SeaGate Restoration precedent).
        // -----------------------------------------------------------------
        wrenn.AddAbility(new LoyaltyAbility(wrenn, -8, () =>
        {
            var controller = wrenn.Controller ?? owner;

            var permanentCards = controller.Zones.Graveyard.GetCards()
                .Where(IsPermanentCard)
                .ToList();
            foreach (var card in permanentCards)
            {
                controller.Zones.Graveyard.RemoveCard(card);
                controller.Zones.Hand.AddCard(card);
                card.SetZone(ZoneType.Hand);
            }

            // CR 114 — emblem in the controller's command zone. Structural
            // only (the "no maximum hand size" SBA isn't wired — see class
            // xmldoc "Deferred").
            var emblem = new Emblem(
                controller: controller,
                sourceName: "Wrenn and Seven — no-maximum-hand-size emblem",
                abilities: Array.Empty<IAbility>());
            controller.AddEmblem(emblem);
        }));

        return wrenn;
    }

    /// <summary>
    /// CR 110.4a — a permanent card is an artifact, battle, creature,
    /// enchantment, land, or planeswalker card. Instants and sorceries are
    /// not permanent cards. (The engine's <see cref="CardType"/> enum doesn't
    /// model Battle, so the predicate covers the five permanent types it
    /// does.) Pure predicate exposed for tests / the live −8.
    /// </summary>
    public static bool IsPermanentCard(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.HasType(CardType.Artifact)
            || card.HasType(CardType.Creature)
            || card.HasType(CardType.Enchantment)
            || card.HasType(CardType.Land)
            || card.HasType(CardType.Planeswalker);
    }

    /// <summary>
    /// Count "lands you control" among the supplied cards. Pure helper
    /// exposed for tests; mirrors the closure baked into the live CDA.
    /// </summary>
    public static int CountLands(IEnumerable<ICard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.Count(c => c.HasType(CardType.Land));
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for the −3 Treefolk token's CDA P/T.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when the token is on the
    /// battlefield, unregisters when it leaves. Mirrors Adeline's / Tarmogoyf's
    /// lifecycle — only the count closure differs (lands you control).
    /// </summary>
    private sealed class TreefolkTokenCdaLifecycle
    {
        private readonly Creature _token;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _landsSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public TreefolkTokenCdaLifecycle(
            Creature token,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> landsSource)
        {
            _token = token;
            _effects = effects;
            _eventBus = eventBus;
            _landsSource = landsSource;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            if (!ReferenceEquals(e.Card, _token)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _token.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                // CR 604.3 — "power and toughness are each equal to the number
                // of lands you control." Both evaluators count lands.
                _registered = new CdaPowerToughnessEffect(
                    _token,
                    powerOf: _ => CountLands(_landsSource()),
                    toughnessOf: _ => CountLands(_landsSource()));
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
