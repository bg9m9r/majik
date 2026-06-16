using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for War Screecher (Foundations Jumpstart). Creature — Bird
/// 1/3. Oracle text (verified against Scryfall):
///   "Flying
///    {5}{W}, {T}: Other creatures you control get +1/+1 until end of turn."
///
/// The base shape (name, Creature, Bird subtype, {1}{W}, 1/3, Flying) is
/// materialised from the embedded JSON definition (<c>war-screecher.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="AvatarOfWoeFactory"/>); the {5}{W}, {T} mass-pump activated
/// ability is layered on here because the JSON <c>AbilityDefinition</c> schema
/// has no controller-scoped mass-pump verb.
///
/// ## The activated ability — controller-scoped mass pump (anthem-grant)
/// "{5}{W}, {T}: Other creatures you control get +1/+1 until end of turn."
/// CR 602 — an activated ability whose cost is {5}{W} mana
/// (<see cref="ManaCostCost"/>) + {T} (<see cref="AdditionalCost.Tap"/> on War
/// Screecher). NON-TARGETED (CR 611 — a one-shot anthem, not a continuous static
/// and not a targeted ability), so it carries no <see cref="TargetRequest"/>.
///
/// On resolution it reads the ABILITY's controller off
/// <see cref="ResolutionContext.Source"/> (CR 109.5 / 400.7 — "you" = the
/// ability's controller), snapshots every OTHER creature that controller
/// controls at that moment (CR 608.2 — effects resolve against current game
/// state), excludes the source itself ("OTHER", CR 601.2c), and registers a
/// +1/+1 <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c, CR 613.1f, with
/// CR 514.2 end-of-turn expiry) on each affected creature's own
/// <see cref="Creature.ActiveEffects"/>. Creatures that enter after resolution
/// do NOT get the buff (one-shot-snapshot posture, same as
/// <see cref="RestlessPrairieFactory"/>'s anthem trigger).
///
/// ## RE-SOURCE-SAFE (rebindSafe: true) + Agatha's Soul Cauldron
/// The effect reads its scope off <c>(ctx.Source as Permanent)?.Controller</c>
/// and the live board off <see cref="ResolutionContext.Game"/>, with NO captured
/// "this creature" reference, so the ability rebinds cleanly onto any new source
/// (CR 707.2 / 613.1f). It is flagged <see cref="ActivatedAbility.RebindSafe"/>
/// so Agatha's Soul Cauldron re-homes the REAL ability via RebindTo: the {5}{W},
/// {T} cost taps the bearer and the pump scopes to the BEARER's controller's
/// board, never the exiled War Screecher. The same shape is independently
/// reconstructable by <see cref="OracleActivatedAbilityBinder"/> (the
/// controller-scoped mass-pump-other shape), so the binder fallback covers it
/// too.
///
/// ## v1 posture
/// - <b>Pump targets per-creature <see cref="Creature.ActiveEffects"/></b> — each
///   pump registers against the affected creature's OWN effects service (the
///   service it was wired with), matching the Layer-7c posture used across the
///   engine. A creature with no effects service (shape-only path) silently
///   no-ops.
/// </summary>
[CardName("War Screecher")]
public static class WarScreecherFactory
{
    public const string CardName = "War Screecher";
    public const string Slug = "war-screecher";
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Bird
        // subtype, {1}{W}, 1/3, Flying). The JSON carries no abilities — the
        // mass-pump ability is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {5}{W}, {T}: Other creatures you control get +1/+1 until end of turn.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: other creatures you control get +{PumpPower}/+{PumpToughness} until end of turn",
            ctx =>
            {
                // CR 109.5 / 400.7 — "you" = the ability's controller. Read it off
                // the (possibly rebound) source so a re-homed copy scopes to the
                // NEW controller's board, never the printed War Screecher's.
                var you = (ctx.Source as Permanent)?.Controller
                    ?? ctx.Controller
                    ?? owner;
                var source = ctx.Source ?? card;

                // Snapshot the affected creatures (CR 608.2) so same-step zone
                // moves don't disturb the enumeration. With a live game wired,
                // sweep every player's battlefield and scope by CONTROL (CR 110.2
                // — a creature you control can sit in an opponent's zone); else
                // fall back to the controller's own battlefield zone.
                IEnumerable<Creature> battlefield = ctx.Game is { } game
                    ? game.AllPlayers.SelectMany(pl => pl.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                    : you.Zones.Battlefield.GetCards().OfType<Creature>();

                var affected = battlefield
                    .Where(c => ReferenceEquals(c.Controller, you))   // "you control"
                    .Where(c => !ReferenceEquals(c, source))          // "OTHER" (CR 601.2c)
                    .ToList();

                foreach (var creature in affected)
                {
                    // CR 613.1f Layer 7c — +1/+1 with CR 514.2 end-of-turn expiry.
                    creature.ActiveEffects?.Register(
                        new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
                }

                return ValueTask.CompletedTask;
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{5}{W}"),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { pumpEffect },
            rebindSafe: true);

        card.AddAbility(ability);

        return card;
    }
}
