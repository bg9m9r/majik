using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wicked Wolf (Throne of Eldraine, {2}{G}{G}).
///
/// Creature — Wolf 3/3. Oracle text (Scryfall, verified 2026-06-14):
///   "When this creature enters, it fights up to one target creature you
///    don't control.
///    Sacrifice a Food: Put a +1/+1 counter on this creature. It gains
///    indestructible until end of turn. Tap it."
///
/// Wicked Wolf is the green removal/payoff body of the Throne-of-Eldraine
/// Food shell: it eats a creature on entry (a fight, not targeted removal),
/// then sacrifices Foods to grow, protect itself, and tap (the printed
/// "Tap it." is a drawback — the wolf taps even though no combat is
/// involved). It pairs with Witch's Oven / Cauldron Familiar / Gilded Goose
/// Food production.
///
/// The base shape (name, Creature, Wolf subtype, {2}{G}{G}, 3/3) is
/// materialised from the embedded JSON definition (<c>wicked-wolf.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the two abilities are layered
/// on here (same posture as <see cref="CauldronFamiliarFactory"/>, whose
/// JSON is shape-only).
///
/// ## Implemented (v1)
/// - 3/3 <see cref="Creature"/> — Wolf at {2}{G}{G}, owner / controller
///   stamped.
/// - <b>ETB fight trigger (CR 603.6a / CR 701.12)</b>: a single
///   <see cref="TriggeredAbility"/> firing on this card's own ETB
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>). On resolution it
///   fights "up to one target creature you don't control" — the fighters
///   each deal damage equal to their power to the other simultaneously via
///   <see cref="Fx.Fight"/> (CR 701.12a; deathtouch / lifelink honoured,
///   combat-only riders not). The opposing creature is read from the LIVE
///   resolution context (<see cref="ContextOpponents"/>) at resolution
///   time — a deterministic v1 picker selects the first creature an
///   opponent controls (same resolver-null-safe posture as Cauldron
///   Familiar's drain). The fight fizzles cleanly (no-op) when there is no
///   opposing creature ("up to one" — CR 701.12c needs both fighters).
/// - <b>"Sacrifice a Food: Put a +1/+1 counter on this creature. It gains
///   indestructible until end of turn. Tap it." (CR 602 / CR 122)</b>: an
///   <see cref="ActivatedAbility"/> whose only cost is
///   <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/> (no mana —
///   the printed cost is the Food sacrifice alone). On resolution, in
///   printed order:
///   <list type="number">
///     <item>Adds one <see cref="CounterType.PlusOnePlusOne"/> counter to
///       Wicked Wolf (CR 122.1).</item>
///     <item>Grants indestructible until end of turn via
///       <see cref="GrantKeywordUntilEndOfTurnEffect"/> (CR 702.12 /
///       CR 613.1f Layer 6, cleanup expiry CR 514.2) — so it survives the
///       lethal it took fighting a bigger creature.</item>
///     <item>Taps it (CR 701.21a — the printed "Tap it." drawback; the
///       wolf taps regardless of combat).</item>
///   </list>
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + abilities. The ETB trigger is
///   attached for shape observability but not registered with any
///   <see cref="TriggerManager"/>; this is the overload
///   <see cref="NamedCardFactory"/> dispatches to. The trigger body reads
///   the live game off the threaded <see cref="ResolutionContext"/>, so it
///   is correct on the production routed build (the engine auto-registers
///   the ETB trigger by zone via <c>TriggerManager.BindCard</c>).
///
/// ## Deferred (v1 gaps)
/// - <b>Fight target prompt</b>: "up to one target creature you don't
///   control" is auto-targeted to the first eligible opposing creature
///   (deterministic v1 picker). Agent-driven target selection waits on the
///   shared targeting-prompt surface (same gap as Kraul Harpooner / Prey
///   Upon). "Up to one" is honoured — zero opposing creatures = clean
///   no-op.
/// - <b>Sacrifice-a-Food target prompt</b>: the embedded
///   <see cref="UnderworldCookbookFactory.SacrificeAFoodCost"/> picks the
///   first Food the controller controls deterministically (shared v1
///   sacrifice-picker posture, not specific to this card).
/// </summary>
[CardName("Wicked Wolf")]
public static class WickedWolfFactory
{
    public const string CardName = "Wicked Wolf";
    public const string Slug = "wicked-wolf";
    public const string PrintedManaCost = "{2}{G}{G}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>The keyword granted by the Food-sacrifice ability.</summary>
    public const string GrantedKeyword = "Indestructible";

    /// <summary>
    /// Construct Wicked Wolf owned and controlled by <paramref name="owner"/>.
    /// The base shape comes from the embedded JSON; the ETB fight trigger and
    /// the Food-sacrifice activated ability are layered on. Self-contained —
    /// no service wiring required (the ETB body reads the live game off the
    /// threaded <see cref="ResolutionContext"/>; the activated ability mutates
    /// counters / effects / tap state directly on the card).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Wolf
        // subtype, {2}{G}{G}, 3/3). The JSON carries no abilities — both are
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB fight trigger — CR 603.6a + CR 701.12.
        //   "When this creature enters, it fights up to one target creature
        //    you don't control."
        // The fighters each deal damage equal to their power to the other
        // simultaneously (CR 701.12a). "Up to one" → fizzles cleanly when
        // there is no opposing creature (CR 701.12c — a fight needs both
        // fighters). The opponent's creature is read from the LIVE
        // resolution context so it is correct on the routed prod build
        // (resolver-null-safe posture; mirrors Cauldron Familiar #2549).
        // ----------------------------------------------------------------
        var fightEffect = new Effect(
            $"{CardName}: fight up to one target creature you don't control",
            ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 701.12c — both fighters must be on the battlefield;
                // if Wicked Wolf already left, the fight does nothing.
                if (card.Zone != ZoneType.Battlefield)
                    return ValueTask.CompletedTask;

                // Deterministic v1 picker — first creature any opponent
                // controls on the battlefield. Agent-driven targeting is the
                // shared deferred gap (see class xmldoc).
                var foe = ContextOpponents.Of(ctx, controller)
                    .SelectMany(opp => opp.Zones.Battlefield.GetCards())
                    .OfType<Creature>()
                    .FirstOrDefault(c => c.Zone == ZoneType.Battlefield);

                // "up to one" — no opposing creature means a clean no-op.
                if (foe != null)
                {
                    Fx.Fight(card, foe);
                }

                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { fightEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // "Sacrifice a Food: Put a +1/+1 counter on this creature. It gains
        // indestructible until end of turn. Tap it." — CR 602 activated
        // ability with NO mana cost; the printed cost is the Food sacrifice
        // alone. Resolution applies the three clauses in printed order.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +1/+1 counter, indestructible until EOT, tap it",
            () =>
            {
                // CR 122.1 — put a +1/+1 counter on Wicked Wolf.
                card.Counters.Add(CounterType.PlusOnePlusOne);

                // CR 702.12 / CR 613.1f Layer 6 — gains indestructible until
                // end of turn (cleanup expiry, CR 514.2). Registered on the
                // card's own layer stack so it survives lethal fight damage.
                card.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, GrantedKeyword));

                // CR 701.21a — "Tap it." The printed drawback taps Wicked
                // Wolf regardless of combat. Idempotent if already tapped.
                if (!card.IsTapped) card.Tap();
            });

        var pumpAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new UnderworldCookbookFactory.SacrificeAFoodCost(),
            },
            effects: new IEffect[] { pumpEffect });

        card.AddAbility(pumpAbility);

        return card;
    }
}
