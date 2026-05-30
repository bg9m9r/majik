using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Primitives;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Wandering Emperor (Kamigawa: Neon Dynasty,
/// {2}{W}{W}).
///
/// Legendary Planeswalker. Starting loyalty 3.
/// Oracle text (Scryfall, verified):
///   "Flash
///    As long as The Wandering Emperor entered this turn, you may activate
///    her loyalty abilities any time you could cast an instant.
///    +1: Put a +1/+1 counter on up to one target creature. It gains first
///        strike until end of turn.
///    −1: Create a 2/2 white Samurai creature token with vigilance.
///    −2: Exile target tapped creature. You gain 2 life."
///
/// The card's base shape (name, Legendary Planeswalker, {2}{W}{W}, loyalty 3)
/// is materialised from the embedded JSON definition
/// (<c>the-wandering-emperor.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Flash keyword marker and
/// the three loyalty abilities are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers, loyalty
/// abilities, targeted exile, counters, or token creation, so they live in
/// the factory (same posture as <see cref="NahiriTheHarbingerFactory"/> /
/// <see cref="TeferiTimeRavelerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Flash</b> keyword marker (CR 702.8) — same surface as
///   <see cref="TishanasTidebinderFactory"/>'s Flash grant.
/// - <b>+1: Put a +1/+1 counter on up to one target creature. It gains first
///   strike until end of turn.</b> (CR 606 loyalty + CR 122 counters +
///   CR 702.7 first strike.) Walks <paramref name="targetResolver"/>'s
///   candidates and applies a single <see cref="CounterType.PlusOnePlusOne"/>
///   counter to the first creature, then grants it First Strike via a
///   <see cref="KeywordAbility"/>. "up to one target" — no resolver / no
///   creature candidate is a legal choice of zero targets, so the clause
///   no-ops (loyalty change still applies, CR 606.3).
/// - <b>−1: Create a 2/2 white Samurai creature token with vigilance.</b>
///   (CR 606 + CR 111 token creation + CR 702.20 vigilance.) Mints a 2/2
///   white Samurai with the Vigilance keyword via
///   <see cref="TokenFactory.CreateOnBattlefield"/>; routes through the
///   supplied <see cref="ZoneService"/> when present so ETB triggers fire.
/// - <b>−2: Exile target tapped creature. You gain 2 life.</b> (CR 606 +
///   CR 701.21 exile + CR 119.3 life gain.) Exiles the first tapped creature
///   from <paramref name="targetResolver"/>'s candidates (CR 701.27 "tapped"),
///   then the controller gains 2 life via <see cref="Fx.GainLife"/>. The life
///   gain happens regardless of whether a legal target was exiled only when a
///   target was actually exiled — "Exile target tapped creature. You gain 2
///   life." is a single instruction sequence on one resolution; with no legal
///   target on resolution the whole ability has no legal target and wouldn't
///   resolve, but the v1 auto-pick treats "no candidate" as the ability still
///   resolving with its loyalty paid and grants the 2 life (the life-gain
///   clause is not gated on the exile in the printed text).
///
/// ## Deferred (v1 gaps)
/// - <b>Instant-speed loyalty static</b>: "As long as The Wandering Emperor
///   entered this turn, you may activate her loyalty abilities any time you
///   could cast an instant." The engine has no per-source "loyalty abilities
///   may be activated at instant speed while a condition holds" timing
///   modifier yet (the same family of cast-time speed modifiers Teferi, Time
///   Raveler's +1 is deferred against). Shipped as a no-op: the loyalty
///   abilities are present and activate normally at sorcery speed; the
///   instant-speed grant is not wired. (CR 606.3 loyalty timing baseline.)
/// - <b>First strike "until end of turn"</b>: the +1's First Strike grant is
///   a permanent <see cref="KeywordAbility"/> marker rather than an
///   end-of-turn-expiring continuous effect — same deterministic posture as
///   <see cref="NahiriTheHarbingerFactory"/>'s −8 haste grant. The cleanup
///   removal (CR 514.2) is not wired in this v1.
/// - <b>Target prompts</b>: <see cref="LoyaltyAbility"/> doesn't declare
///   <see cref="Majik.Core.Targeting.TargetRequest"/>s. The +1 and −2 pick
///   the first legal candidate from the supplied resolver deterministically.
///   Same gap as Nahiri / Karn / Liliana / Ugin.
/// </summary>
[CardName("The Wandering Emperor")]
public static class TheWanderingEmperorFactory
{
    public const string CardName = "The Wandering Emperor";
    public const string Slug = "the-wandering-emperor";
    public const int StartingLoyalty = 3;
    public const int Plus1Loyalty = +1;
    public const int Minus1Loyalty = -1;
    public const int Minus2Loyalty = -2;

    /// <summary>
    /// Construct The Wandering Emperor with no resolvers / services wired —
    /// the +1 no-ops (no creature resolver), the −1 still mints the Samurai
    /// token (owner-scoped), and the −2 no-ops the exile but still grants 2
    /// life. Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetResolver: null, zoneService: null);

    /// <summary>
    /// Construct The Wandering Emperor.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetResolver">Returns candidate permanents for the +1
    /// (a creature to receive the +1/+1 counter + first strike) and the −2
    /// (a tapped creature to exile). v1 picks the first legal candidate. May
    /// be null — the +1 chooses zero targets and the −2 exiles nothing.</param>
    /// <param name="zoneService">When supplied, the −1 token creation routes
    /// through <see cref="ZoneService"/> so ETB triggers (Soul Warden etc.)
    /// fire. May be null — token still enters via raw zone mutation.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Permanent>>? targetResolver,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker, {2}{W}{W}, loyalty 3). The JSON carries no abilities —
        // the Flash marker + three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var emperor = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- Flash (CR 702.8) — keyword marker so the engine treats the card
        //    as castable any time its controller could cast an instant.
        emperor.AddAbility(new KeywordAbility("Flash", emperor, owner));

        // -- +1: Put a +1/+1 counter on up to one target creature. It gains
        //    first strike until end of turn. ----------------------------------
        // CR 606 (loyalty) + CR 122 (counters) + CR 702.7 (first strike).
        // "up to one target" — picking zero is legal; with no creature
        // candidate the clause no-ops (loyalty change still applies).
        emperor.AddAbility(new LoyaltyAbility(emperor, Plus1Loyalty, () =>
        {
            var candidates = targetResolver?.Invoke();
            if (candidates == null) return;
            foreach (var p in candidates)
            {
                if (p == null) continue;
                if (p.Zone != ZoneType.Battlefield) continue;
                if (!p.HasType(CardType.Creature)) continue;

                Fx.PlaceCounter(p, CounterType.PlusOnePlusOne, 1);

                // "It gains first strike until end of turn." — CR 702.7.
                // v1 ships a permanent keyword marker (end-of-turn cleanup
                // deferred — same posture as Nahiri's −8 haste grant).
                p.AddAbility(new KeywordAbility("First Strike", p, p.Controller ?? owner));
                return; // "up to one target" — a single creature.
            }
        }));

        // -- −1: Create a 2/2 white Samurai creature token with vigilance. ---
        // CR 606 + CR 111 (token creation) + CR 702.20 (vigilance).
        emperor.AddAbility(new LoyaltyAbility(emperor, Minus1Loyalty, () =>
        {
            var controller = emperor.Controller ?? owner;
            var spec = new TokenFactory.TokenSpec(
                Name: "Samurai",
                Power: 2,
                Toughness: 2,
                Subtypes: new[] { CardSubtype.Samurai },
                Keywords: new[] { "Vigilance" },
                // CR 105.2a / CR 111.4 — "2/2 white Samurai creature token".
                Colors: new[] { ManaColor.White });
            TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
        }));

        // -- −2: Exile target tapped creature. You gain 2 life. --------------
        // CR 606 + CR 701.21 (exile) + CR 701.27 (tapped) + CR 119.3 (life
        // gain). v1 deterministic first-legal pick from the supplied resolver.
        emperor.AddAbility(new LoyaltyAbility(emperor, Minus2Loyalty, () =>
        {
            var controller = emperor.Controller ?? owner;

            var candidates = targetResolver?.Invoke();
            if (candidates != null)
            {
                foreach (var p in candidates)
                {
                    if (p == null) continue;
                    if (p.Zone != ZoneType.Battlefield) continue;
                    if (!p.HasType(CardType.Creature)) continue;
                    if (!p.IsTapped) continue;

                    // Raw-zone, owner-routed exile (same posture as Nahiri).
                    var holder = p.Controller ?? p.Owner;
                    holder?.Zones.Battlefield.RemoveCard(p);
                    var exileOwner = p.Owner ?? owner;
                    exileOwner.Zones.Exile.AddCard(p);
                    p.SetZone(ZoneType.Exile);
                    break; // "target" — a single creature.
                }
            }

            // "You gain 2 life." — CR 119.3.
            Fx.GainLife(controller, 2);
        }));

        return emperor;
    }
}
