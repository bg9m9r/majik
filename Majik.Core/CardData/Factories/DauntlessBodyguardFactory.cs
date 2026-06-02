using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dauntless Bodyguard (Dominaria — Creature — Human
/// Knight {W} 2/1).
///
/// Oracle text (verified against Scryfall):
///   "As this creature enters, choose another creature you control.
///    Sacrifice this creature: The chosen creature gains indestructible until
///    end of turn."
///
/// The base shape (name, Creature — Human Knight, {W}, 2/1) is materialised
/// from the embedded JSON definition (<c>dauntless-bodyguard.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (as-enters creature choice, sacrifice→indestructible activated ability) are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express an as-enters object choice nor a sacrifice cost, so they live in the
/// factory.
///
/// ## Implemented
///
/// ### "As this creature enters, choose another creature you control." (CR 614.12)
/// Resolved eagerly via a <c>Func&lt;Creature, Creature&gt;</c> selector on the
/// wired overload — same posture as <see cref="AdaptiveAutomatonFactory"/> /
/// <see cref="MetallicMimicFactory"/> (the engine has no Choose-a-creature agent
/// prompt yet). The selector is handed the Bodyguard itself so callers can
/// honour the "another" restriction (CR 601.2c-style — the chosen creature must
/// be a different object). The choice is stored per-card in a
/// <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
/// keyed by the Bodyguard instance (a flicker is a new object and chooses
/// again) and exposed via <see cref="GetChosenCreature"/>.
///
/// ### "Sacrifice this creature: The chosen creature gains indestructible until
/// end of turn." (CR 602 activated ability / CR 702.12 Indestructible)
/// A single <see cref="ActivatedAbility"/> whose cost is
/// <see cref="AdditionalCost.Sacrifice"/> on the Bodyguard itself (no mana
/// component). Resolution registers a
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Indestructible" to
/// the chosen creature, which expires in the cleanup step (CR 514.2). Same sac
/// + keyword-grant shape as <see cref="SelflessSpiritFactory"/>, but scoped to
/// the single creature chosen as the Bodyguard entered rather than the whole
/// team.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseCreature prompt. The wired overload accepts a
///   <c>Func&lt;Creature, Creature&gt;</c> selector closure — bots and tests
///   supply the chosen creature directly. Same posture as Adaptive Automaton /
///   Metallic Mimic.
/// - <b>Choice timing</b>: CR 614.12 says the choice is part of the as-enters
///   replacement; v1 captures it eagerly at factory-build time. Observationally
///   equivalent in the current ETB pipeline.
/// - <b>Sacrifice payment</b>: the generic <see cref="AdditionalCost.Pay"/>
///   sacrifice path is a no-op stub (same posture as
///   <see cref="SelflessSpiritFactory"/> / Caustic Caterpillar); the activated
///   ability closure performs the zone move directly so the sacrifice is
///   observable.
/// - <b>No live continuous-effects service ≡ shape-only</b>: without a
///   <see cref="ContinuousEffectsService"/> the activated ability still
///   sacrifices the Bodyguard, but the indestructible grant is a no-op (no
///   layers service to register against).
/// </summary>
[CardName("Dauntless Bodyguard")]
public static class DauntlessBodyguardFactory
{
    public const string CardName = "Dauntless Bodyguard";
    public const string Slug = "dauntless-bodyguard";

    // Per-card chosen creature — same ConditionalWeakTable posture as
    // AdaptiveAutomatonFactory. Keyed by the Bodyguard instance so a flicker
    // (a new object) chooses again.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Creature, ChoiceBox>
        _chosen = new();

    private sealed class ChoiceBox { public Creature? Value; }

    /// <summary>
    /// Construct Dauntless Bodyguard with no live wiring and no as-enters choice
    /// resolved. Suitable for card-shape / dispatcher tests — the chosen-creature
    /// slot is unset (<see cref="GetChosenCreature"/> returns null) and the
    /// sacrifice ability is attached for shape observability (activating it
    /// sacrifices the Bodyguard but grants nothing — no layers service). This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, creatureChooser: null);

    /// <summary>
    /// Construct a fully-wired Dauntless Bodyguard. When
    /// <paramref name="creatureChooser"/> is supplied the as-enters "choose
    /// another creature you control" choice is resolved eagerly and stored.
    /// When <paramref name="continuousEffects"/> is also supplied, activating
    /// the sacrifice ability registers a
    /// <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Indestructible"
    /// to the chosen creature. The card shape (including the sacrifice ability)
    /// is always wired regardless of which services are present.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// indestructible grant against. May be null — the sacrifice still happens
    /// but no grant is made.</param>
    /// <param name="creatureChooser">Resolves the chosen creature at as-enters
    /// time. Handed the Bodyguard itself so callers can honour the "another"
    /// restriction (CR 614.12). May be null — no choice is made and the sac
    /// ability grants nothing.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<Creature, Creature>? creatureChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Human Knight,
        // {W}, 2/1). The JSON carries no abilities — the as-enters choice and
        // the sacrifice ability are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (creatureChooser != null)
        {
            // CR 614.12 — "As this creature enters, choose another creature you
            // control." v1 resolves eagerly at factory-build time (mirrors
            // Adaptive Automaton / Metallic Mimic). The chooser is responsible
            // for the "another" restriction (must not pick the Bodyguard
            // itself); the Bodyguard is passed in so the closure can enforce it.
            var chosen = creatureChooser(card);
            _chosen.AddOrUpdate(card, new ChoiceBox { Value = chosen });
        }

        // ----------------------------------------------------------------
        // "Sacrifice this creature: The chosen creature gains indestructible
        //  until end of turn." (CR 602 activated ability / CR 702.12.)
        // Cost = AdditionalCost.Sacrifice on self, no mana. Resolution grants
        // "Indestructible" to the stored chosen creature via a Layer-6
        // GrantKeywordUntilEndOfTurnEffect that expires at cleanup (CR 514.2).
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice self + grant chosen creature indestructible EOT",
            () =>
            {
                SacrificeSelf(card, owner);

                if (continuousEffects == null) return;
                if (!_chosen.TryGetValue(card, out var box) || box.Value == null) return;

                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(box.Value, "Indestructible"));
            });

        var sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { sacEffect });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// Returns the creature chosen as Dauntless Bodyguard entered, if one was
    /// resolved at construction time, else null. Per-card (not per-factory) — a
    /// flickered Bodyguard is a new object and chooses again.
    /// </summary>
    public static Creature? GetChosenCreature(Creature dauntlessBodyguard)
    {
        ArgumentNullException.ThrowIfNull(dauntlessBodyguard);
        return _chosen.TryGetValue(dauntlessBodyguard, out var box) ? box.Value : null;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by
    /// <see cref="SelflessSpiritFactory"/> — the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a no-op stub, so the
    /// effect closure performs the zone move directly.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
