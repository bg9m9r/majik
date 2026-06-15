using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steel Leaf Champion (Dominaria, {G}{G}{G}).
///
/// Creature — Elf Knight 5/4. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "This creature can't be blocked by creatures with power 2 or less."
///
/// ## Implementation
///
/// - 5/4 Creature — Elf Knight at printed cost {G}{G}{G}; owner / controller
///   wired. <see cref="CardSubtype.Elf"/> + <see cref="CardSubtype.Knight"/>
///   subtypes so Elf / Knight tribal scopes (Elvish Archdruid, etc.) see it.
/// - <b>Conditional block restriction (CR 509.1b)</b>: registered as a
///   <see cref="CantBeBlockedExceptByEffect"/> on the supplied
///   <see cref="ContinuousEffectsService"/>. "Can't be blocked by creatures
///   with power 2 or less" is the complement of "can't be blocked except by
///   creatures with power 3 or more" — the effect's
///   <see cref="CantBeBlockedExceptByEffect.AllowedBlockerPredicate"/> accepts
///   a would-be blocker iff its (layer-computed) power is ≥ 3.
///   <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> walks the
///   attacker's <see cref="Creature.ActiveEffects"/> and rejects any blocker
///   the predicate excludes. The predicate reads <see cref="Creature.Power"/>,
///   so the threshold is evaluated continuously against the blocker's CURRENT
///   power at block-declaration time (CR 509.1b is checked at declaration),
///   picking up any pumps / debuffs already applied through the layer system
///   (CR 613) — e.g. a 2/2 that has been given +1/+1 becomes a legal blocker.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The block restriction is
///   NOT registered (no effects service). Suitable for dispatcher / structural
///   tests; the contract test exercises this single-arg path.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — fully wired
///   block restriction. The effect is registered on construction and the
///   service is bound onto <see cref="Creature.ActiveEffects"/> so the combat
///   validator picks the restriction up.
/// </summary>
[CardName("Steel Leaf Champion")]
public static class SteelLeafChampionFactory
{
    public const string CardName = "Steel Leaf Champion";
    public const string PrintedManaCost = "{G}{G}{G}";
    public const int Power = 5;
    public const int Toughness = 4;

    /// <summary>
    /// Minimum power a creature must have to be a legal blocker. "Can't be
    /// blocked by creatures with power 2 or less" ⇒ a legal blocker needs
    /// power ≥ 3.
    /// </summary>
    public const int MinBlockerPower = 3;

    /// <summary>
    /// Construct Steel Leaf Champion with no live wiring. Suitable for
    /// dispatcher / shape tests. The block restriction is NOT registered —
    /// use the effects-aware overload to wire it.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Steel Leaf Champion with optional runtime services. Registers
    /// the "can't be blocked by creatures with power 2 or less" restriction on
    /// <paramref name="effects"/> when supplied (also binding it onto
    /// <see cref="Creature.ActiveEffects"/> so the combat validator picks the
    /// restriction up).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            // Bind the service so BlockLegality / CombatAbilities reads
            // (including the can't-be-blocked-except-by walk) flow through the
            // same layer pipeline.
            card.ActiveEffects = effects;

            // CR 509.1b — "This creature can't be blocked by creatures with
            // power 2 or less." Modelled as the complementary "can't be blocked
            // except by creatures with power ≥ 3": the predicate accepts a
            // blocker iff it is a creature whose CURRENT (layer-computed) power
            // is at least MinBlockerPower. Non-creature blockers are already
            // disallowed by CR 509.1a; we narrow to Creature for the power read.
            effects.Register(new CantBeBlockedExceptByEffect(
                source: card,
                predicate: blocker => blocker is Creature c
                    && c.Power >= MinBlockerPower));
        }

        return card;
    }
}
