using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Krenko, Mob Boss (Magic 2013 / many reprints).
///
/// Legendary Creature — Goblin Warrior {2}{R}{R} 3/3. Oracle text:
///   "{T}: Create X 1/1 red Goblin creature tokens, where X is the number
///    of Goblins you control."
///
/// ## Implemented (v1)
/// - 3/3 Legendary Creature — Goblin Warrior at printed cost {2}{R}{R};
///   <see cref="CardSupertype.Legendary"/> supertype + Goblin / Warrior
///   subtypes wired so the Legend Rule (CR 704.5j) and tribal lord scopes
///   (Goblin Chieftain / Goblin Warchief / Goblin Rabblemaster) see Krenko
///   correctly.
/// - <b>Activated ability (CR 602)</b>: <c>{T}: create X 1/1 red Goblin
///   creature tokens, where X is the number of Goblins you control.</c>
///   Cost = <see cref="AdditionalCost.Tap"/> only (no mana). At resolution
///   the effect:
///   <ol>
///     <li>Counts the Goblins on the controller's battlefield
///         <em>including Krenko itself</em> — the oracle text reads
///         "Goblins you control" with no "other" qualifier, so Krenko is
///         counted (contrast Goblin Piledriver's "other attacking Goblins"
///         rider). With no other Goblins out, Krenko produces one token;
///         with three Goblins out (Krenko + two friends), three tokens.
///         This matches the canonical "Krenko goes exponential" curve
///         that defines Goblin tribal in Modern / Legacy / Commander.</li>
///     <li>Spawns the counted number of 1/1 red Goblin tokens via
///         <see cref="TokenFactory.CreateOnBattlefield"/>, routing through
///         the supplied <see cref="ZoneService"/> when one is wired so
///         token-ETB triggers (Impact Tremors, Goblin Bushwhacker's
///         haste-grant on the same turn, Purphoros) fire. CR 111 / 111.4 —
///         "1/1 red Goblin creature token" with explicit red color and
///         Goblin subtype.</li>
///   </ol>
///   The Goblin count is taken at resolution (CR 608.2), so tokens minted
///   by an in-resolution side effect don't retroactively bump X — the
///   snapshot is read once before token creation begins.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. The
///   activated ability is attached for shape observability; token creation
///   falls back to raw zone moves (no <see cref="ZoneService"/>) — token
///   ETB triggers won't auto-fire from the bus. Suitable for shape /
///   <see cref="NamedCardFactory"/> dispatch tests.
/// - <see cref="Create(Player, ZoneService?)"/> — fully-wired overload.
///   Token creation funnels through <see cref="ZoneService.MoveCard"/>
///   so <see cref="Events.CardMovedEvent"/> fires for each token (Soul
///   Warden / Impact Tremors / Goblin Bushwhacker pickup correctly).
///
/// ## Deferred (v1 gaps)
/// - <b>Activation-rate gate</b>: Krenko's {T} ability can only be
///   activated once per turn under normal circumstances (the tap cost
///   gates it — you can't tap an already-tapped permanent per CR 602.5a).
///   The engine's <see cref="ActivatedAbility"/> + tap-cost check already
///   handles this; no extra plumbing needed.
/// - <b>Summoning sickness</b>: Krenko's {T} is gated by
///   <see cref="ActionValidator"/>'s tap-cost check against creatures
///   with summoning sickness (CR 302.1 — must have been controlled since
///   the most recent turn began). The factory itself doesn't bypass this;
///   that's enforced upstream at activation validation time.
/// </summary>
[CardName("Krenko, Mob Boss")]
public static class KrenkoMobBossFactory
{
    public const string CardName = "Krenko, Mob Boss";
    public const string PrintedManaCost = "{2}{R}{R}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Krenko, Mob Boss with no live <see cref="ZoneService"/>
    /// wiring. The activated ability is attached for shape tests; tokens
    /// land on the battlefield via raw zone manipulation so token-ETB
    /// triggers (Impact Tremors / Soul Warden) won't auto-fire from the
    /// bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null);

    /// <summary>
    /// Construct Krenko, Mob Boss with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service so each spawned
    /// Goblin token publishes <see cref="Events.CardMovedEvent"/> on ETB
    /// (Soul Warden / Impact Tremors / Goblin Bushwhacker chain
    /// correctly). When null, tokens are placed on the battlefield via
    /// raw zone moves — fine for unit tests that don't exercise token-ETB
    /// triggers.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Create X 1/1 red Goblin creature tokens, where X is the
        // number of Goblins you control (CR 602 — activated ability;
        // CR 107.1b — variable X resolves at the moment the effect
        // determines it).
        //
        // X-count semantics:
        //   - Counted at resolution (CR 608.2 — effects resolve against
        //     current game state).
        //   - INCLUDES Krenko itself — oracle reads "Goblins you control"
        //     with no "other" qualifier; Krenko is a Goblin he controls.
        //   - Counts Goblin permanents on controller's battlefield only
        //     (CR 109.5 — "you control" = controller, not opponents).
        // ----------------------------------------------------------------
        var tapEffect = new Effect(
            $"{CardName}: create X 1/1 red Goblin tokens (X = Goblins you control)",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 608.2 — snapshot the count at resolution. Includes
                // Krenko (no "other" qualifier on the oracle text).
                int gobCount = controller.Zones.Battlefield.GetCards()
                    .Count(c => c.HasSubtype(CardSubtype.Goblin));

                if (gobCount <= 0) return;

                for (int i = 0; i < gobCount; i++)
                {
                    CreateGoblinToken(controller, zoneService);
                }
            });

        var tapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { tapEffect });

        card.AddAbility(tapAbility);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 red Goblin creature token under
    /// <paramref name="controller"/>'s control. Mirrors
    /// <see cref="GoblinRabblemasterFactory.CreateGoblinToken"/>'s shape so
    /// "1/1 red Goblin token" minting stays uniform across Goblin sources.
    /// </summary>
    public static Creature CreateGoblinToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Goblin creature token".
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
