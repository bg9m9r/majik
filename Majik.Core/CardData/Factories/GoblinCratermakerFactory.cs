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
/// Named-card factory for Goblin Cratermaker (Guilds of Ravnica, {1}{R}).
///
/// Creature — Goblin Warrior 2/2. Oracle text:
///   "{1}, Sacrifice Goblin Cratermaker: Choose one —
///     • Goblin Cratermaker deals 2 damage to target creature.
///     • Destroy target colorless nonland permanent."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Warrior, mana cost {1}{R}, owner/controller wired.
/// - <b>"Choose one —" activated ability</b> (CR 602, CR 700.2): modelled
///   as TWO separate <see cref="ActivatedAbility"/>s sharing the same cost
///   shape (<c>{1}</c> + sacrifice self). Same v1 pattern as
///   <see cref="UmezawasJitteFactory"/>'s three-mode activated abilities —
///   each "mode" is a standalone activation with its own
///   <see cref="TargetRequest"/>. The activating player picks which
///   activation to use (= which mode); CR 700.2's "each mode at most
///   once per activation" is trivially satisfied because each activation
///   triggers exactly one of the two abilities.
///   - <b>Mode A — 2 damage to target creature</b>: 1..1 "target creature"
///     <see cref="TargetRequest"/>. On resolution, deals 2 damage to the
///     chosen creature via <see cref="Fx.DealDamage"/>; sacrifice runs in
///     the effect closure (mirrors the spellbomb / firebrand sac-self
///     pattern).
///   - <b>Mode B — destroy target colorless nonland permanent</b>: 1..1
///     "target colorless nonland permanent" <see cref="TargetRequest"/>.
///     Resolution-time legality check (CR 608.2b): target must still be
///     a permanent on the battlefield, not a Land, and have no coloured
///     pips (see <see cref="Majik.Core.Cards.CardColors.GetColors"/>).
///     Destroy uses <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///     with <see cref="ZoneMoveReason.Destroy"/> — Indestructible
///     (CR 702.12) cancels the destroy; regeneration shields (CR 701.15)
///     are consumed normally.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: same gap as Pyrite / Aether
///   Spellbomb — the engine's generic <see cref="AdditionalCost"/>
///   sacrifice payment is currently a no-op stub. The effect closure
///   performs the zone move so behaviour is observable. Remove the
///   explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// - <b>Target-legality filter for "colorless nonland permanent"</b>:
///   the agent-side gatherer doesn't yet filter to colorless nonland
///   permanents — resolution-time guard handles illegal targets
///   (CR 608.2b — effect involving an illegal target does nothing,
///   but the cost was already paid so the sacrifice still resolves).
/// </summary>
[CardName("Goblin Cratermaker")]
public static class GoblinCratermakerFactory
{
    public const string CardName = "Goblin Cratermaker";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Mode A damage amount.</summary>
    public const int DamageAmount = 2;

    /// <summary>
    /// Construct Goblin Cratermaker owned and controlled by
    /// <paramref name="owner"/>. Both modal activated abilities are
    /// attached; the activating player chooses which to use at activation
    /// time.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Mode A — {1}, Sac: ~ deals 2 damage to target creature.
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            $"{CardName} — Mode A: 2 damage to target creature + sac self",
            () =>
            {
                if (damageAbility != null
                    && damageAbility.ChosenTargets.Count > 0
                    && damageAbility.ChosenTargets[0].Count > 0
                    && damageAbility.ChosenTargets[0][0] is Creature target
                    && target.Zone == ZoneType.Battlefield)
                {
                    Fx.DealDamage(target, DamageAmount);
                }

                SacrificeSelf(card, owner);
            });

        damageAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(damageAbility);

        // ----------------------------------------------------------------
        // Mode B — {1}, Sac: Destroy target colorless nonland permanent.
        // CR 608.2b — resolution-time legality on "colorless nonland
        // permanent". Indestructible / regeneration handled by the
        // standard Destroy reason.
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            $"{CardName} — Mode B: destroy target colorless nonland permanent + sac self",
            () =>
            {
                if (destroyAbility != null
                    && destroyAbility.ChosenTargets.Count > 0
                    && destroyAbility.ChosenTargets[0].Count > 0
                    && destroyAbility.ChosenTargets[0][0] is Permanent target
                    && IsLegalDestroyTarget(target))
                {
                    Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                }

                SacrificeSelf(card, owner);
            });

        destroyAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target colorless nonland permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(destroyAbility);

        return card;
    }

    /// <summary>
    /// Resolution-time legality for Mode B. Target must still be a
    /// permanent on the battlefield, not a Land, and colourless
    /// (no coloured pips per <see cref="CardColors.GetColors"/>).
    /// </summary>
    private static bool IsLegalDestroyTarget(Permanent perm)
    {
        if (perm.Zone != ZoneType.Battlefield) return false;
        if (perm.HasType(CardType.Land)) return false;
        return CardColors.GetColors(perm).Count == 0;
    }

    /// <summary>
    /// Move <paramref name="cratermaker"/> from the battlefield to its
    /// owner's graveyard. Idempotent — no-op if already off the
    /// battlefield. Mirrors the closure used by Pyrite / Aether / Nihil
    /// Spellbomb's sac-self effects.
    /// </summary>
    private static void SacrificeSelf(Creature cratermaker, Player owner)
    {
        if (cratermaker.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(cratermaker);
        owner.Zones.Graveyard.AddCard(cratermaker);
        cratermaker.SetZone(ZoneType.Graveyard);
    }
}
