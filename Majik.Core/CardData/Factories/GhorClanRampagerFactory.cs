using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghor-Clan Rampager (Gatecrash / Modern Horizons,
/// {2}{R}{G}). Creature — Beast 4/4. Oracle text (verified against
/// Scryfall / the embedded seed):
///   "Trample
///    Bloodrush — {R}{G}, Discard Ghor-Clan Rampager: Target attacking
///    creature gets +4/+4 and gains trample until end of turn."
///
/// The card's base shape (name, Creature, Beast subtype, {2}{R}{G}, 4/4) is
/// materialised from the embedded JSON definition
/// (<c>ghor-clan-rampager.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (Trample keyword on the body, Bloodrush activated ability) are layered on
/// top here — the JSON <c>AbilityDefinition</c> schema doesn't yet express
/// keyword markers or hand-activated discard abilities, so they live in the
/// factory (same posture as the other JSON-backed cards, e.g.
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Trample (CR 702.19)</b>: <see cref="KeywordAbility"/> marker on the
///   creature body. <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>
///   reads the marker (or the layer-system keyword set when an
///   <see cref="ContinuousEffectsService"/> is wired) — same posture as
///   Colossal Skyturtle's Flying marker.
///
/// - <b>Bloodrush — {R}{G}, Discard this card (CR 702.74-style hand
///   activation)</b>: an <see cref="ActivatedAbility"/> gated to the hand by
///   <see cref="DiscardSelfCost"/> (a card not in the activating player's
///   hand cannot pay it) plus a {R}{G} <see cref="ManaCostCost"/>. This is
///   the same cost/activation-zone shape Channel uses (Colossal Skyturtle),
///   and matches Bloodrush's rule (CR 702.x — Bloodrush is an activated
///   ability you may activate only while the card is in your hand). On
///   resolve: the targeted creature gets +4/+4 and gains Trample until end of
///   turn, registered as a <see cref="PumpUntilEndOfTurnEffect"/> +
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> pair on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — both expire at the
///   cleanup step). Identical effect shape to Reckless Charge's pump+grant.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Attacking" target restriction</b>: Bloodrush requires "target
///   attacking creature", but <see cref="GameContext"/> exposes no live
///   combat / attackers view yet (the same <c>ICurrentCombatProvider</c> gap
///   noted on <see cref="AtarkaWorldRenderFactory"/>). The candidate gatherer
///   therefore offers every creature on every battlefield (the production
///   agent prompt narrows to legal attackers; the CR 608.2b resolve guard
///   re-checks the target is still a battlefield creature). Same pragmatic
///   posture as Colossal Skyturtle's Channel 2 "target creature" gather. When
///   the combat-provider primitive lands, the gatherer + a resolve-time
///   "is attacking" guard can be tightened.
/// - <b>Enchantment / non-pump fizzle</b>: if the chosen target is no longer a
///   battlefield <see cref="Creature"/> at resolution (zone change, type
///   loss) the effect is a clean no-op (CR 608.2b) rather than throwing.
/// </summary>
[CardName("Ghor-Clan Rampager")]
public static class GhorClanRampagerFactory
{
    public const string CardName = "Ghor-Clan Rampager";
    public const string Slug = "ghor-clan-rampager";
    public const string PrintedManaCost = "{2}{R}{G}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>CR 702.x — Bloodrush activation mana cost: {R}{G}.</summary>
    public const string BloodrushManaCost = "{R}{G}";

    /// <summary>+P pump magnitude. Ghor-Clan Rampager prints +4/+4.</summary>
    public const int PumpPower = 4;

    /// <summary>+T pump magnitude. Ghor-Clan Rampager prints +4/+4.</summary>
    public const int PumpToughness = 4;

    /// <summary>Granted keyword. CR 702.19 — Trample.</summary>
    public const string GrantedKeyword = "Trample";

    /// <summary>
    /// Construct Ghor-Clan Rampager owned and controlled by
    /// <paramref name="owner"/>. The Trample body marker and the Bloodrush
    /// activated ability are both wired. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Beast
        // subtype, {2}{R}{G}, 4/4). The JSON carries no abilities — Trample +
        // Bloodrush are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.19 — Trample. KeywordAbility marker so CombatAbilities
        // surfaces the trample combat-damage assignment rule. Same shape as
        // Colossal Skyturtle's Flying marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // CR 702.x — Bloodrush: {R}{G}, Discard this card: Target attacking
        // creature gets +4/+4 and gains trample until end of turn.
        AttachBloodrush(card, owner);

        return card;
    }

    // -----------------------------------------------------------------------
    // Bloodrush — {R}{G}, Discard this card: +4/+4 and gains trample EOT
    // -----------------------------------------------------------------------

    private static void AttachBloodrush(Creature card, Player owner)
    {
        ActivatedAbility? bloodrush = null;

        var targetRequests = new[]
        {
            new TargetRequest(
                Description: "target attacking creature",
                MinTargets: 1,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.CombatTrick | BotIntent.Buff,
                // "Target attacking creature." GameContext exposes no live
                // combat view yet (ICurrentCombatProvider gap — see
                // AtarkaWorldRender), so gather every battlefield creature;
                // the production agent prompt narrows to legal attackers and
                // the CR 608.2b resolve guard re-checks battlefield presence.
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .OfType<Creature>()
                    .Cast<object>()
                    .ToList()),
        };

        var effect = new Effect(
            $"{CardName} (Bloodrush): target attacking creature gets +4/+4 and gains trample until end of turn",
            () =>
            {
                if (bloodrush!.ChosenTargets.Count == 0
                    || bloodrush.ChosenTargets[0].Count == 0) return;

                var raw = bloodrush.ChosenTargets[0][0];

                // CR 608.2b — target must still be a creature on the
                // battlefield at resolution; otherwise the ability does
                // nothing (clean no-op rather than an NRE).
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (target.ActiveEffects == null) return;

                // CR 613.1c — Layer 7c +P/+T modification (+4/+4 until EOT).
                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

                // CR 613.1c (Layer 6) — keyword grant. Trample lifts excess
                // combat damage onto the defending player (CR 702.19).
                target.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
            });

        bloodrush = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(BloodrushManaCost),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[] { effect },
            targetRequests: targetRequests);

        card.AddAbility(bloodrush);
    }
}
