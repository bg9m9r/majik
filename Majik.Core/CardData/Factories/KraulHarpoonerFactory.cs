using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kraul Harpooner (Guilds of Ravnica).
///
/// Creature — Insect Warrior {1}{G} 3/2. Oracle text:
///   "Reach
///    Undergrowth — When this creature enters, choose up to one target creature
///    you don't control with flying. This creature gets +X/+0 until end of turn,
///    where X is the number of creature cards in your graveyard, then you may
///    have this creature fight that creature."
///
/// ## Implemented (v1)
/// - Reach keyword (via <see cref="KeywordAbility"/>).
/// - ETB triggered ability: counts creature cards in the controller's graveyard
///   (Undergrowth X) and registers a <see cref="PumpUntilEndOfTurnEffect"/> for
///   +X/+0 on Kraul Harpooner itself. Effect is a no-op when the graveyard
///   contains no creature cards.
///
/// ## Deferred (v1 gaps)
/// - <b>Target selection</b>: "choose up to one target creature you don't
///   control with flying" requires targeting infrastructure with controller-
///   filter + flying-filter. Deferred until agent prompt system supports it.
/// - <b>Fight step</b>: "you may have this creature fight that creature" (CR
///   701.12 — each deals damage equal to its power to the other). Deferred;
///   no fight action wired in v1.
/// - <b>"You may" prompt</b>: the fight is optional; deferred alongside targeting.
/// </summary>
[CardName("Kraul Harpooner")]
public static class KraulHarpoonerFactory
{
    /// <summary>
    /// Construct Kraul Harpooner owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var k = new Creature(
            "Kraul Harpooner",
            manaCost: "{1}{G}",
            power: 3, toughness: 2,
            subtypes: new[] { CardSubtype.Insect, CardSubtype.Warrior });
        k.SetOwner(owner);
        k.SetController(owner);

        // --------------------------------------------------------------------
        // Reach — CR 702.17.
        // KeywordAbility is the marker; CombatAbilities.HasReach reads it.
        // --------------------------------------------------------------------
        k.AddAbility(new KeywordAbility("Reach", k, owner));

        // --------------------------------------------------------------------
        // Undergrowth ETB trigger: +X/+0 until end of turn, X = creature cards
        // in controller's graveyard (CR 702.86 — undergrowth).
        // Targeting + fight step deferred (see xmldoc above).
        // --------------------------------------------------------------------
        var etbEffect = new Effect(
            "Kraul Harpooner: Undergrowth +X/+0 EOT",
            () =>
            {
                var x = owner.Zones.Graveyard.GetCards()
                    .Count(c => c.HasType(CardType.Creature));
                if (x > 0 && k.ActiveEffects != null)
                {
                    k.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(k, x, 0));
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: k,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(k),
            effects: new IEffect[] { etbEffect });

        k.AddAbility(etbTrigger);

        return k;
    }

    /// <summary>
    /// Layer 7c +P/+T effect with end-of-turn expiry.
    /// Mirrors the private <c>PumpUntilEndOfTurnEffect</c> in
    /// <c>OracleSpellBinder</c> but lives here so the factory can register it
    /// without coupling to the binder's private nested type.
    /// </summary>
    private sealed class PumpUntilEndOfTurnEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;

        public PumpUntilEndOfTurnEffect(Creature target, int p, int t)
        {
            _target = target;
            _p = p;
            _t = t;
        }

        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += _p;
            chars.Toughness += _t;
        }
    }
}
