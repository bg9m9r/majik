using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sorin, Imperious Bloodlord (M19/M20 reprint shape,
/// {2}{B}).
///
/// Legendary Planeswalker — Sorin. Starting loyalty 4.
/// Oracle text (Scryfall, verified 2026-06):
///   "+1: Target creature you control gains deathtouch and lifelink until end
///        of turn. If it's a Vampire, put a +1/+1 counter on it.
///    +1: You may sacrifice a Vampire. When you do, Sorin deals 3 damage to any
///        target and you gain 3 life.
///    −3: You may put a Vampire creature card from your hand onto the
///        battlefield."
///
/// The base shape (name, Legendary Planeswalker — Sorin, {2}{B}, loyalty 4) is
/// materialised from the embedded JSON definition
/// (<c>sorin-imperious-bloodlord.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three loyalty abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// loyalty abilities, until-end-of-turn keyword grants, conditional counters,
/// sacrifice riders, damage / life gain, or hand→battlefield moves, so they
/// live in the factory (same posture as
/// <see cref="ChandraTorchOfDefianceFactory"/> /
/// <see cref="LilianaOfTheVeilFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+1: Target creature you control gains deathtouch and lifelink until end
///   of turn. If it's a Vampire, put a +1/+1 counter on it
///   (CR 606 + CR 702.2 + CR 702.15 + CR 121.1)</b>: picks the first creature
///   from <paramref name="ownCreatureResolver"/> and registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> for "Deathtouch" and one for
///   "Lifelink" (both Layer 6, expiring at cleanup — CR 514.2) against the
///   creature's <see cref="Permanent.ActiveEffects"/>. If that creature is a
///   Vampire (CR 205.3m), a +1/+1 counter is placed on it via
///   <see cref="Fx.PlaceCounter"/>. With no resolver wired (or no creature) the
///   clause no-ops; the loyalty change still applies (CR 606.3). A creature
///   with no <see cref="Permanent.ActiveEffects"/> service attached still gets
///   the Vampire counter — only the keyword grants need the layer system.
/// - <b>+1: You may sacrifice a Vampire. When you do, Sorin deals 3 damage to
///   any target and you gain 3 life (CR 606 + CR 701.16 + CR 603.10 + CR 119 +
///   CR 119.3)</b>: when <paramref name="sacrificeVampireResolver"/> yields a
///   Vampire to sacrifice, it is sacrificed via <see cref="Fx.Sacrifice"/>
///   (CR 701.16 — not a destroy, so indestructible / regen don't gate). The
///   "when you do" reflexive trigger (CR 603.10) then resolves: Sorin deals 3
///   to the "any target" from <paramref name="anyTargetResolver"/>
///   (<see cref="Fx.DealDamageAny"/> routes Player / Creature / Planeswalker)
///   and the controller gains 3 life. The whole clause is optional ("you may")
///   — a null resolver / no Vampire declines the sacrifice, so the reflexive
///   trigger never fires (no damage, no life gain).
/// - <b>−3: You may put a Vampire creature card from your hand onto the
///   battlefield (CR 606 + CR 701.20)</b>: when
///   <paramref name="handVampireResolver"/> yields a Vampire creature card from
///   the controller's hand, it is moved hand→battlefield under the controller's
///   control (CR 614-free put — not a cast). Null resolver / no Vampire
///   declines; loyalty change still applies.
///
/// ## Deferred (v1 gaps)
/// - <b>Target / choice prompts</b>: <see cref="LoyaltyAbility"/> does not yet
///   declare <see cref="Majik.Core.Targeting.TargetRequest"/>s; the +1 target
///   creature, the Vampire to sacrifice, the "any target", and the hand Vampire
///   are supplied by the resolvers rather than chosen via the agent. Same gap
///   Chandra / Liliana / Teferi share.
/// </summary>
[CardName("Sorin, Imperious Bloodlord")]
public static class SorinImperiousBloodlordFactory
{
    public const string CardName = "Sorin, Imperious Bloodlord";
    public const string Slug = "sorin-imperious-bloodlord";
    public const int StartingLoyalty = 4;
    public const int VampireCounterAmount = 1;
    public const int SacrificeDamage = 3;
    public const int SacrificeLifeGain = 3;
    private const string Deathtouch = "Deathtouch";
    private const string Lifelink = "Lifelink";

    /// <summary>
    /// Construct Sorin with no resolvers wired — every loyalty ability no-ops
    /// (loyalty changes still apply). Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, ownCreatureResolver: null, sacrificeVampireResolver: null,
            anyTargetResolver: null, handVampireResolver: null);

    /// <summary>
    /// Construct Sorin, Imperious Bloodlord.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="ownCreatureResolver">Returns candidate "creatures you
    /// control" for the +1 deathtouch/lifelink grant. v1 picks the first. May
    /// be null — the clause no-ops.</param>
    /// <param name="sacrificeVampireResolver">Returns the Vampire to sacrifice
    /// for the second +1 (CR 701.16). Returning null declines the optional
    /// sacrifice, so the reflexive trigger (damage + life gain) never
    /// fires.</param>
    /// <param name="anyTargetResolver">Returns the "any target" (Player /
    /// Creature / Planeswalker) the second +1's reflexive trigger deals 3 to.
    /// May be null — the damage no-ops; life gain still applies once the
    /// Vampire is sacrificed.</param>
    /// <param name="handVampireResolver">Returns a Vampire creature card from
    /// the controller's hand for the −3. Returning null declines.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Creature>>? ownCreatureResolver,
        Func<Creature?>? sacrificeVampireResolver,
        Func<object?>? anyTargetResolver,
        Func<Creature?>? handVampireResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Sorin, {2}{B}, loyalty 4). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var sorin = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +1: Target creature you control gains deathtouch and lifelink
        //    until end of turn. If it's a Vampire, put a +1/+1 counter on it.
        // CR 606 (loyalty) + CR 702.2 (deathtouch) + CR 702.15 (lifelink) +
        // CR 514.2 (EOT expiry) + CR 121.1 (+1/+1 counter). v1 picks the first
        // creature from the resolver (no agent target prompt yet).
        sorin.AddAbility(new LoyaltyAbility(sorin, +1, () =>
        {
            var candidates = ownCreatureResolver?.Invoke();
            if (candidates == null) return;
            foreach (var creature in candidates)
            {
                if (creature == null) continue;
                if (creature.Zone != ZoneType.Battlefield) continue;

                // Grant deathtouch + lifelink until end of turn (Layer 6).
                // Needs the continuous-effects layer system; null ActiveEffects
                // (shape-only path) silently skips the keyword grants.
                creature.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, Deathtouch));
                creature.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, Lifelink));

                // "If it's a Vampire, put a +1/+1 counter on it." (CR 205.3m).
                if (creature.HasSubtype(CardSubtype.Vampire))
                    Fx.PlaceCounter(creature, CounterType.PlusOnePlusOne, VampireCounterAmount);

                return; // "target creature" — a single creature.
            }
        }));

        // -- +1: You may sacrifice a Vampire. When you do, Sorin deals 3 damage
        //    to any target and you gain 3 life.
        // CR 606 (loyalty) + CR 701.16 (sacrifice) + CR 603.10 (reflexive
        // "when you do" trigger) + CR 119 (damage) + CR 119.3 (life gain). The
        // sacrifice is optional ("you may"); a null resolver / no Vampire
        // declines it and the reflexive rider never resolves.
        sorin.AddAbility(new LoyaltyAbility(sorin, +1, () =>
        {
            var controller = sorin.Controller ?? owner;

            var vampire = sacrificeVampireResolver?.Invoke();
            if (vampire == null) return;                 // declined the sacrifice
            if (vampire.Zone != ZoneType.Battlefield) return;
            if (!vampire.HasSubtype(CardSubtype.Vampire)) return; // must be a Vampire

            Fx.Sacrifice(vampire); // CR 701.16 — not a destroy effect.

            // "When you do" reflexive trigger (CR 603.10): 3 damage to any
            // target + you gain 3 life.
            var target = anyTargetResolver?.Invoke();
            if (target != null) Fx.DealDamageAny(target, SacrificeDamage);
            Fx.GainLife(controller, SacrificeLifeGain);
        }));

        // -- −3: You may put a Vampire creature card from your hand onto the
        //    battlefield.
        // CR 606 (loyalty) + CR 701.20-free put (not a cast). v1 takes the
        // Vampire creature card supplied by the resolver and moves it
        // hand→battlefield under the controller's control. Optional ("you may")
        // — a null resolver / no Vampire declines; loyalty change still applies.
        sorin.AddAbility(new LoyaltyAbility(sorin, -3, () =>
        {
            var controller = sorin.Controller ?? owner;

            var card = handVampireResolver?.Invoke();
            if (card == null) return;                    // declined
            if (card.Zone != ZoneType.Hand) return;
            if (!card.HasSubtype(CardSubtype.Vampire)) return; // must be a Vampire creature

            controller.Zones.Hand.RemoveCard(card);
            controller.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);
            card.SetController(controller);
        }));

        return sorin;
    }
}
