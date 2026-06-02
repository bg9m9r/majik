using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gray Merchant of Asphodel (Theros, {3}{B}{B}).
///
/// Creature — Zombie 2/4. Oracle text (verified against Scryfall 2026-06-02):
///   "When this creature enters, each opponent loses X life, where X is your
///    devotion to black. You gain life equal to the life lost this way.
///    (Each {B} in the mana costs of permanents you control counts toward
///    your devotion to black.)"
///
/// The card's base shape (name, Creature, Zombie subtype, {3}{B}{B}, 2/4) is
/// materialised from the embedded JSON definition
/// (<c>gray-merchant-of-asphodel.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB devotion-drain trigger is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express a
/// devotion-scaled "each opponent loses X / you gain that much" ETB trigger
/// (same posture as <see cref="DominatorDroneFactory"/>'s ETB drain, of which
/// this is the devotion-scaled, lifegain-paired sibling).
///
/// ## Implemented (v1)
/// - 2/4 <see cref="CardSubtype.Zombie"/> at printed cost {3}{B}{B}, owner /
///   controller wired.
/// - <b>ETB devotion drain (CR 603.1 / CR 700.5 / CR 119.3)</b> — "When this
///   creature enters, each opponent loses X life, where X is your devotion to
///   black. You gain life equal to the life lost this way." A
///   <see cref="TriggeredAbility"/> keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> (no targets — "each
///   opponent" is global, CR 109.5). On resolution:
///   <list type="number">
///     <item>X = the controller's devotion to black (CR 700.5 — the number of
///       {B} mana symbols among the mana costs of permanents they control).
///       Computed via <see cref="NykthosShrineToNyxFactory.ComputeDevotionToColor"/>
///       (the shared devotion helper). Gray Merchant is on the battlefield when
///       its own trigger resolves, so its own {B}{B} counts toward X
///       (CR 603.3 / 700.5).</item>
///     <item>Each opponent (supplied by <c>opponentResolver</c>) loses X life
///       via <see cref="Player.LoseLife"/> (CR 119.3). Same resolver-injection
///       pattern as <see cref="DominatorDroneFactory"/> — the <c>Player</c>
///       aggregate exposes no opponents list at v1.</item>
///     <item>The controller gains life equal to the total life lost this way
///       (CR 119.3 — a separate life-change event from the loss). v1 totals
///       <c>X × opponentCount</c>; see the deferral note re: actual-loss
///       accounting.</item>
///   </list>
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches the ETB trigger
/// structurally (correct card shape for factory-shape / dispatch tests). The
/// trigger is NOT registered with a <see cref="TriggerManager"/> and has no
/// opponent resolver, so the drain no-ops AND the lifegain is 0 (no life lost
/// this way). Production callers use the full overload.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration without a resolver</b> — same gap as
///   <see cref="DominatorDroneFactory"/>; <c>Player</c> doesn't expose an
///   opponent list, so the factory leans on a caller-supplied resolver.
/// - <b>Actual-life-lost accounting</b>: CR 119.3 / 118.9 — "life lost this
///   way" is the life ACTUALLY lost. v1 computes the lifegain as
///   <c>X × opponentCount</c>, which is exact when every opponent simply loses
///   X. It does not yet subtract life a "can't lose life" / loss-replacement
///   effect prevented for a given opponent, because <see cref="Player.LoseLife"/>
///   returns no committed-loss amount. Same primitive-level posture as the rest
///   of the lose-life/gain-life drain family (Blood Artist / Dominator Drone).
/// - <b>Hybrid / Phyrexian black pips</b>: devotion reads the pure-{B} pip
///   field only (no hybrid / Phyrexian buckets yet) — the shared devotion gap
///   documented on <see cref="NykthosShrineToNyxFactory.ComputeDevotionToColor"/>.
/// </summary>
[CardName("Gray Merchant of Asphodel")]
public static class GrayMerchantOfAsphodelFactory
{
    public const string CardName = "Gray Merchant of Asphodel";
    public const string Slug = "gray-merchant-of-asphodel";
    public const int Power = 2;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Gray Merchant of Asphodel with no live wiring. The ETB
    /// devotion-drain trigger attaches structurally; it is NOT registered with
    /// a <see cref="TriggerManager"/> and has no opponent resolver, so the
    /// drain / lifegain no-op. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, opponentResolver: null);

    /// <summary>
    /// Construct a fully-wired Gray Merchant of Asphodel.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for registration. May be null —
    /// the trigger attaches structurally but isn't enrolled.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent" for the
    /// ETB drain. Without a resolver the drain no-ops and no life is gained.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Zombie
        // subtype, {3}{B}{B}, 2/4). The JSON carries no abilities — the ETB
        // devotion drain is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB devotion drain — CR 603.1 (ETB trigger) / CR 700.5 (devotion) /
        // CR 109.5 ("each opponent" is global, no targets) / CR 119.3 (life
        // loss + the separate lifegain).
        //   "When this creature enters, each opponent loses X life, where X is
        //    your devotion to black. You gain life equal to the life lost this
        //    way."
        // X is read on resolution off the controller's live devotion to black.
        // Gray Merchant itself is on the battlefield by then, so its own {B}{B}
        // counts (CR 700.5). Each opponent loses X; the controller gains the
        // total life lost this way (X × opponentCount in v1 — see class xmldoc
        // re: actual-loss accounting).
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses X (devotion to black); you gain that much life",
            () =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;

                var controller = card.Controller ?? owner;
                var x = NykthosShrineToNyxFactory.ComputeDevotionToColor(
                    controller, ManaColor.Black);
                if (x <= 0) return; // CR 119.4 — losing 0 life is not losing life.

                var lifeLost = 0;
                foreach (var opp in opponents)
                {
                    if (opp == null) continue;
                    if (ReferenceEquals(opp, controller)) continue; // "each opponent"
                    opp.LoseLife(x);
                    lifeLost += x;
                }

                // CR 119.3 — "you gain life equal to the life lost this way" is
                // a separate life-change event from the losses.
                if (lifeLost > 0) controller.GainLife(lifeLost);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
