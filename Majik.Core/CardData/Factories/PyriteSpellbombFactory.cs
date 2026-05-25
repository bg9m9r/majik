using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pyrite Spellbomb (Mirrodin / reprints, {1}).
///
/// Artifact. Oracle text:
///   "{T}, Sacrifice Pyrite Spellbomb: Pyrite Spellbomb deals 2 damage to
///    any target.
///    {R}, Sacrifice Pyrite Spellbomb: Draw a card."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{T}, Sacrifice: 2 damage to any target</b> — wired as an
///   <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Tap"/>
///   plus <see cref="AdditionalCost.Sacrifice"/> on the spellbomb itself.
///   A single <see cref="TargetRequest"/> is declared so the activating
///   player's agent picks an any-target (player / creature / planeswalker)
///   at activation (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert to
///   loyalty removal (CR 306.7) — same shape as Lightning Bolt / Helix.
///   Sacrifice is performed by the effect closure (mirrors Aether /
///   Nihil Spellbomb — the generic <see cref="AdditionalCost.Pay"/>
///   sacrifice path is a stub).
/// - <b>{R}, Sacrifice: Draw a card</b> — second
///   <see cref="ActivatedAbility"/> on the same card. <see cref="ManaCostCost"/>("{R}")
///   plus self-sacrifice; resolution moves the spellbomb to its owner's
///   graveyard and draws one card for the controller via
///   <see cref="Fx.DrawCards"/>. Empty library is a silent no-op (SBAs
///   handle the loss condition).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move so behaviour is
///   observable. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
///   Mirrors Aether / Nihil Spellbomb.
/// </summary>
[CardName("Pyrite Spellbomb")]
public static class PyriteSpellbombFactory
{
    public const string CardName = "Pyrite Spellbomb";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Pyrite Spellbomb owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = new Artifact(CardName, PrintedManaCost);
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Sacrifice this artifact: ~ deals 2 damage to any target.
        // CR 602 — activated ability with a single any-target request.
        // Resolution reads ChosenTargets and gates on a damage-receiving
        // shape (Player / Creature / Planeswalker) via Fx.DealDamageAny.
        // Illegal-on-resolution targets fail silently (CR 608.2b) — the
        // sacrifice still resolves because the cost was paid.
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            "Pyrite Spellbomb: 2 damage to any target + sac self",
            () =>
            {
                if (damageAbility != null
                    && damageAbility.ChosenTargets.Count > 0
                    && damageAbility.ChosenTargets[0].Count > 0)
                {
                    var target = damageAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, 2);
                }

                SacrificeSelf(spellbomb, owner);
            });

        damageAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(spellbomb),
                AdditionalCost.Sacrifice(spellbomb),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        spellbomb.AddAbility(damageAbility);

        // ----------------------------------------------------------------
        // {R}, Sacrifice this artifact: Draw a card.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Pyrite Spellbomb: draw a card + sac self",
            () =>
            {
                SacrificeSelf(spellbomb, owner);
                Fx.DrawCards(owner, 1);
            });

        var drawAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{R}"),
                AdditionalCost.Sacrifice(spellbomb),
            },
            effects: new IEffect[] { drawEffect });

        spellbomb.AddAbility(drawAbility);

        return spellbomb;
    }

    /// <summary>
    /// Move <paramref name="spellbomb"/> from the battlefield to its
    /// owner's graveyard. Idempotent — no-op if already off the
    /// battlefield. Mirrors the closure used by Aether / Nihil Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
