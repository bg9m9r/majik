using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grim Lavamancer (Torment / many reprints, {R}).
///
/// Creature — Human Wizard 1/1. Oracle text (Scryfall, verified):
///   "{R}, {T}, Exile two cards from your graveyard: This creature deals
///    2 damage to any target."
///
/// The base shape (name, Creature, Human/Wizard subtypes, {R}, 1/1) is
/// materialised from the embedded JSON definition (<c>grim-lavamancer.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="StormscaleScionFactory"/>). The single activated ability is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express a multi-cost ({mana} + {T} + exile-from-graveyard) any-target
/// ping.
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Human Wizard at printed cost {R}; owner / controller
///   wired. Both <see cref="CardSubtype.Human"/> and
///   <see cref="CardSubtype.Wizard"/> are stamped so Human-tribal and
///   Wizard-tribal anchors (e.g. Adeliz, Naban) see it correctly.
///
/// - <b>{R}, {T}, Exile two cards from your graveyard: This creature deals
///   2 damage to any target (CR 602)</b>: <see cref="ActivatedAbility"/>
///   with:
///   <list type="number">
///     <item>a <see cref="ManaCostCost"/> for the printed {R} (CR 602.1b —
///       the mana symbol in the activation cost).</item>
///     <item><see cref="AdditionalCost.Tap"/> on the Lavamancer (CR 602.1b
///       — the {T} symbol; summoning-sickness / tapped-state legality is
///       handled by the cost layer, same as Fanatical Firebrand).</item>
///   </list>
///   The "Exile two cards from your graveyard" cost (CR 118 / 601.2g — an
///   additional cost) is performed inside the resolution closure, mirroring
///   the graveyard-exile-as-cost handling in
///   <see cref="SeasonedPyromancerFactory"/> / <see cref="ClingToDustFactory"/>
///   (the generic <see cref="AdditionalCost"/> surface has no
///   exile-from-graveyard payment type — the enum is Tap / Sacrifice /
///   Discard / PayLife only). A single any-target request is declared so the
///   activating player's agent picks a damage-receiving target at activation
///   (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes the 2 damage
///   through <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert
///   to loyalty removal (CR 306.7) — same shape as Mogg Fanatic / Fanatical
///   Firebrand / Lightning Bolt.
///
/// ## Order of operations
///
/// CR 117.1c — all costs for an activated ability are paid simultaneously
/// from the player's perspective. The mana + tap costs are taken by the
/// cost layer at activation; the graveyard exile + the damage are performed
/// inside the resolution closure. The closure first checks that two cards
/// are available in the owner's graveyard — if fewer than two are present
/// the exile cost cannot be paid, so the whole body no-ops (no exile, no
/// damage), matching the real-card legality (CR 601.2g — you can't activate
/// the ability without paying the full cost).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Graveyard-exile cost as a first-class cost surface</b>: there is no
///   <see cref="AdditionalCostType"/> for "exile N from your graveyard", so
///   the cost is paid inside the resolve closure (with an up-front
///   two-cards-available guard) rather than gated by the cost layer at
///   activation. Same posture as Seasoned Pyromancer's "exile this card
///   from your graveyard" payment. The observable contract (exactly two
///   graveyard cards leave for exile and 2 damage is dealt, or nothing
///   happens) is preserved.
/// - <b>Which two cards are exiled</b>: the closure exiles the two
///   front-most graveyard cards (insertion order). The choice of which two
///   to exile has no gameplay-relevant downstream consequence for the
///   Lavamancer itself; an agent-driven pick is a future refinement,
///   matching the heuristic-pick posture elsewhere.
/// </summary>
[CardName("Grim Lavamancer")]
public static class GrimLavamancerFactory
{
    public const string CardName = "Grim Lavamancer";
    public const string Slug = "grim-lavamancer";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int PingDamage = 2;

    /// <summary>CR 602 — printed activation mana cost: {R}.</summary>
    public const string ActivationManaCost = "{R}";

    /// <summary>Number of graveyard cards the activation cost exiles.</summary>
    public const int GraveyardExileCount = 2;

    /// <summary>
    /// Construct Grim Lavamancer owned and controlled by
    /// <paramref name="owner"/>. The {R}, {T}, Exile-two-ping activated
    /// ability is attached to the card. The ability is fully self-contained —
    /// no service wiring required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Wizard subtypes, {R}, 1/1). The JSON carries no abilities —
        // the ping ability is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {R}, {T}, Exile two cards from your graveyard:
        //   This creature deals 2 damage to any target.
        // CR 602 — activated ability with a single any-target request.
        // The mana ({R}) + tap ({T}) costs are taken by the cost layer at
        // activation; the "exile two from your graveyard" cost (no enum
        // member exists for it) + the 2 damage are performed in the resolve
        // closure. The closure short-circuits if fewer than two graveyard
        // cards are available (the cost can't be paid — CR 601.2g).
        // ----------------------------------------------------------------
        ActivatedAbility? pingAbility = null;
        var pingEffect = new Effect(
            $"{CardName}: exile two from graveyard, then 2 damage to any target",
            () =>
            {
                var graveyard = owner.Zones.Graveyard.GetCards().ToList();

                // CR 601.2g — the exile-two cost can only be paid if at
                // least two cards are present. If not, the whole activation
                // is illegal: no exile, no damage.
                if (graveyard.Count < GraveyardExileCount)
                {
                    return;
                }

                // Pay the cost — exile the two front-most graveyard cards
                // (CR 118 / 701.10 — exile is owner's exile zone).
                for (var i = 0; i < GraveyardExileCount; i++)
                {
                    var toExile = graveyard[i];
                    owner.Zones.Graveyard.RemoveCard(toExile);
                    owner.Zones.Exile.AddCard(toExile);
                    toExile.SetZone(ZoneType.Exile);
                }

                if (pingAbility != null
                    && pingAbility.ChosenTargets.Count > 0
                    && pingAbility.ChosenTargets[0].Count > 0)
                {
                    var target = pingAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, PingDamage);
                }
            });

        pingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { pingEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(pingAbility);

        return card;
    }
}
