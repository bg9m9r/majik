using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.46 — Splice onto Arcane. Optional <em>additional</em> cost
/// paid when casting an Arcane spell. The caster reveals a card with
/// "Splice onto Arcane" from their hand and pays its splice cost; the
/// spliced card's effects are added to the resolving Arcane spell.
///
/// <para>
/// The spliced card stays in the caster's hand — it is NOT cast (CR
/// 702.46a). Splice is announced as part of the Arcane spell's cast
/// (CR 601.2b — additional costs / optional costs locked in at
/// announcement) and modelled here as an <see cref="IAdditionalCost"/>
/// the caller layers onto a cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// <c>additionalCosts</c> list (same Buyback / Kicker / sacrifice-rider
/// shape).
/// </para>
///
/// <para>
/// <see cref="Pay"/> checks (a) the target Arcane card carries the
/// <see cref="CardSubtype.Arcane"/> subtype (CR 702.46 — splice only
/// onto Arcane spells), (b) the spliced card is in the caster's hand
/// (CR 702.46a — revealed from hand), and (c) the caster can pay the
/// splice mana cost. <see cref="SpellCastFlow"/> reads
/// <see cref="BuildSplicedEffects"/> after the target spell's printed
/// effect factory runs to fold the spliced card's effects into the
/// resolving spell.
/// </para>
///
/// <para>
/// Splice is <em>not</em> an alternative cost (CR 118.9): the Arcane
/// spell still pays its printed mana cost in full, and the splice cost
/// is paid on top. Each splice rider is one instance of this cost; the
/// caller may layer multiple <see cref="SpliceOntoArcaneCost"/> entries
/// onto a single cast for multi-card splice chains (CR 702.46b —
/// multiple splice riders stack in announcement order).
/// </para>
/// </summary>
public sealed class SpliceOntoArcaneCost : IAdditionalCost
{
    private readonly ICard _arcaneTarget;
    private readonly ICard _splicedCard;
    private readonly ManaCost _spliceCost;
    private readonly Func<Player, IReadOnlyList<IEffect>> _effectBuilder;

    /// <summary>
    /// Build a splice rider attached to an Arcane spell being cast.
    /// </summary>
    /// <param name="arcaneTarget">The Arcane spell card being cast.
    /// Must carry <see cref="CardSubtype.Arcane"/> (CR 702.46).</param>
    /// <param name="splicedCard">The card with "Splice onto Arcane"
    /// being revealed from <paramref name="splicedCard"/>'s owner's
    /// hand. Must currently reside in <see cref="ZoneType.Hand"/>.</param>
    /// <param name="spliceCost">The printed splice mana cost
    /// (e.g. <c>{1}{R}</c> for Desperate Ritual, <c>{2}{B}</c> for
    /// Goryo's Vengeance).</param>
    /// <param name="effectBuilder">Closure returning the spliced
    /// card's resolve-time effects. Invoked by
    /// <see cref="BuildSplicedEffects"/> at cast-flow injection time.
    /// Receives the spell's controller (caster) so factory bodies
    /// can route their mana / damage / search effects through the
    /// right player.</param>
    public SpliceOntoArcaneCost(
        ICard arcaneTarget,
        ICard splicedCard,
        ManaCost spliceCost,
        Func<Player, IReadOnlyList<IEffect>> effectBuilder)
    {
        _arcaneTarget = arcaneTarget ?? throw new ArgumentNullException(nameof(arcaneTarget));
        _splicedCard = splicedCard ?? throw new ArgumentNullException(nameof(splicedCard));
        _spliceCost = spliceCost ?? throw new ArgumentNullException(nameof(spliceCost));
        _effectBuilder = effectBuilder ?? throw new ArgumentNullException(nameof(effectBuilder));
    }

    /// <summary>The Arcane spell card carrying the splice rider attaches to.</summary>
    public ICard ArcaneTarget => _arcaneTarget;

    /// <summary>The card with "Splice onto Arcane" being revealed.
    /// Stays in hand (CR 702.46a) — not cast.</summary>
    public ICard SplicedCard => _splicedCard;

    /// <summary>Printed splice mana cost.</summary>
    public ManaCost SpliceCost => _spliceCost;

    public string Description => $"Splice onto Arcane {_spliceCost}";

    /// <summary>
    /// CR 702.46 — Splice is legal when (a) the target spell is Arcane,
    /// (b) the spliced card is in the caster's hand to reveal, and
    /// (c) the caster can produce the splice mana. Pure check — does
    /// not mutate the pool.
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (!_arcaneTarget.HasSubtype(CardSubtype.Arcane)) return false;
        if (_splicedCard.Zone != ZoneType.Hand) return false;
        return caster.ManaPool.Pay(_spliceCost).Success;
    }

    /// <summary>
    /// CR 702.46 / CR 601.2f — pay the splice mana. Returns true on
    /// success. Re-checks the Arcane / hand-residence gate; a splice
    /// can't legally pay if the rider lost legality between announce
    /// and pay (mirrors Escape's "exile N other graveyard cards" gate
    /// short-circuit). The spliced card is NOT moved out of hand
    /// (CR 702.46a — "the card stays in your hand").
    /// </summary>
    public bool Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (!_arcaneTarget.HasSubtype(CardSubtype.Arcane)) return false;
        if (_splicedCard.Zone != ZoneType.Hand) return false;
        return caster.PayMana(_spliceCost);
    }

    /// <summary>
    /// CR 702.46 — produce the spliced card's effect list. Consumed by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> after the Arcane
    /// spell's <c>EffectFactory</c> runs; the returned effects are
    /// appended to the resolving spell's effect chain in splice-
    /// announcement order so multiple spliced riders concatenate
    /// deterministically (CR 702.46b).
    /// </summary>
    public IReadOnlyList<IEffect> BuildSplicedEffects(Player controller)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        return _effectBuilder(controller);
    }
}
