using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sungold Sentinel (Innistrad: Midnight Hunt, {1}{W}).
///
/// Creature — Human Soldier 3/2. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Whenever this creature enters or attacks, exile up to one target card
///    from a graveyard.
///    Coven — {1}{W}: Choose a color. This creature gains hexproof from that
///    color until end of turn and can't be blocked by creatures of that color
///    this turn. Activate only if you control three or more creatures with
///    different powers."
///
/// ## Implemented (v1)
/// - <b>Card shape</b> — {1}{W} Creature — Human Soldier 3/2 built directly
///   (no embedded JSON resource; same posture as
///   <see cref="VeilOfSummerFactory"/> / <see cref="MotherOfRunesFactory"/>
///   for cards without a seed def).
/// - <b>Enter / attack exile trigger (CR 603.6c)</b> — two
///   <see cref="TriggeredAbility"/> entries over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> and
///   <see cref="Triggers.OnAttackSelf"/>, each with a 0..1 ("up to one")
///   "target card in a graveyard" <see cref="TargetRequest"/>. Resolution
///   rechecks legality (CR 608.2b — still in a graveyard) and moves the card
///   to its owner's exile (mirrors <see cref="SoulGuideLanternFactory"/>'s
///   ETB branch). "Up to one" means a clean no-op when no target is chosen.
/// - <b>Coven-gated activated ability (CR 602.1 / CR 602.5c)</b> —
///   <c>{1}{W}: Choose a color. ...</c> with a
///   <see cref="ManaCostCost"/>{1}{W}. The "Activate only if you control three
///   or more creatures with different powers" rider is the Coven condition
///   (CR 702.150) exposed via <see cref="CanActivateCoven"/> (shared with
///   <see cref="AugurOfAutumnFactory.HasCoven"/>).
/// - <b>Hexproof-from-colour grant (CR 702.11e)</b> — on resolution, registers
///   a <see cref="GrantKeywordUntilEndOfTurnEffect"/> adding
///   "Hexproof from {Colour}" to Sungold's keyword set until end of turn (CR
///   514.2). <see cref="Majik.Core.Targeting.TargetLegality"/> reads the
///   colour-qualified hexproof keyword and denies opponents' matching-colour
///   spells/abilities while letting other colours — and the controller —
///   target.
/// - <b>Can't-be-blocked-by-colour grant (CR 509.1b)</b> — on the same
///   resolution, registers a <see cref="CantBeBlockedExceptByEffect"/>
///   (expiring at end of turn) whose allowed-blocker predicate is "blocker's
///   effective colours do NOT contain the chosen colour".
///   <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> consults it.
///
/// ## v1 gaps (consistent with the rest of the engine)
/// - <b>Agent-side colour prompt</b>: CR 601.2c — "choose a color" is made as
///   the ability is activated. v1 takes the colour as a parameter to
///   <see cref="ResolveColorGrant"/> (tests/bots supply it); the dispatcher
///   activated-ability closure defaults to white. Same posture as
///   <see cref="MotherOfRunesFactory"/>'s colour pick.
/// - <b>Coven enforcement at activation</b>: <see cref="CanActivateCoven"/>
///   exposes the gate; wiring it as the activated ability's
///   live <c>CanActivate</c> predicate follows the same deferred posture as
///   other "activate only if" riders.
/// </summary>
[CardName("Sungold Sentinel")]
public static class SungoldSentinelFactory
{
    public const string CardName = "Sungold Sentinel";
    public const string PrintedManaCost = "{1}{W}";
    public const string CovenActivationCost = "{1}{W}";

    /// <summary>
    /// Construct Sungold Sentinel with no continuous-effects service. The
    /// colour-grant activated ability still attaches structurally but the
    /// hexproof / can't-be-blocked grants need a service to register against
    /// — supply one via <see cref="Create(Player, ContinuousEffectsService)"/>.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, activeEffects: null);

    /// <summary>
    /// Construct Sungold Sentinel. When <paramref name="activeEffects"/> is
    /// supplied it is wired as the creature's <see cref="Permanent.ActiveEffects"/>
    /// so the Coven grant can register its until-end-of-turn hexproof-from-colour
    /// and can't-be-blocked-by-colour effects. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to when a service is available.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? activeEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            CardName,
            PrintedManaCost,
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });
        card.SetOwner(owner);
        card.SetController(owner);
        if (activeEffects != null) card.ActiveEffects = activeEffects;

        // ----------------------------------------------------------------
        // Whenever this creature enters or attacks, exile up to one target
        // card from a graveyard. CR 603.6c — two trigger conditions, one
        // exile effect. "Up to one" → MinTargets 0.
        // ----------------------------------------------------------------
        AddExileTrigger(card, owner, Triggers.OnEnterBattlefieldSelf(card));
        AddExileTrigger(card, owner, Triggers.OnAttackSelf(card));

        // ----------------------------------------------------------------
        // Coven — {1}{W}: Choose a color. This creature gains hexproof from
        // that color until end of turn and can't be blocked by creatures of
        // that color this turn. CR 602.1.
        // ----------------------------------------------------------------
        var grantEffect = new Effect(
            $"{CardName}: gains hexproof-from-colour + can't-be-blocked-by-colour EOT",
            () => ResolveColorGrant(card, ManaColor.White, card.ActiveEffects));

        var covenAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(CovenActivationCost) },
            effects: new IEffect[] { grantEffect });

        card.AddAbility(covenAbility);

        return card;
    }

    private static void AddExileTrigger(Creature card, Player owner, ITriggerCondition condition)
    {
        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{CardName}: exile up to one target card from a graveyard",
            () =>
            {
                if (trigger == null) return;
                if (trigger.ChosenTargets.Count == 0) return;
                if (trigger.ChosenTargets[0].Count == 0) return; // "up to one" — zero chosen
                ResolveExile(trigger.ChosenTargets[0][0]);
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to one target card in a graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(trigger);
    }

    /// <summary>
    /// Resolve the exile clause against <paramref name="rawTarget"/>. CR
    /// 608.2b — the target card must still be in a graveyard; otherwise a
    /// clean no-op. Null (the "up to zero" case) is also a no-op. Exposed for
    /// tests / bots without driving the full trigger flow.
    /// </summary>
    public static void ResolveExile(object? rawTarget)
    {
        if (rawTarget is not ICard targetCard) return;
        if (targetCard.Zone != ZoneType.Graveyard) return;

        var targetOwner = targetCard.Owner;
        if (targetOwner == null) return;

        targetOwner.Zones.Graveyard.RemoveCard(targetCard);
        targetOwner.Zones.Exile.AddCard(targetCard);
        targetCard.SetZone(ZoneType.Exile);
    }

    /// <summary>
    /// The Coven activation gate (CR 702.150 / the printed "Activate only if
    /// you control three or more creatures with different powers"). Delegates
    /// to <see cref="AugurOfAutumnFactory.HasCoven"/> — the canonical Coven
    /// condition helper.
    /// </summary>
    public static bool CanActivateCoven(Player controller) =>
        AugurOfAutumnFactory.HasCoven(controller);

    /// <summary>
    /// Apply Sungold Sentinel's Coven grant against <paramref name="self"/>:
    /// register an until-end-of-turn "Hexproof from {colour}" keyword grant
    /// (CR 702.11e) and an until-end-of-turn "can't be blocked by creatures of
    /// {colour}" restriction (CR 509.1b). Both need a
    /// <paramref name="activeEffects"/> service to register against; without
    /// one this is a no-op (shape-only path). Exposed for tests / bots.
    /// </summary>
    public static void ResolveColorGrant(
        Creature self,
        ManaColor color,
        ContinuousEffectsService? activeEffects)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (activeEffects == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var colorName = ColorName(color);

        // CR 702.11e — "gains hexproof from {colour} until end of turn".
        activeEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(self, $"Hexproof from {colorName}"));

        // CR 509.1b — "can't be blocked by creatures of {colour} this turn".
        // Allowed-blocker predicate: blocker is legal iff its effective colour
        // set does NOT contain the chosen colour.
        activeEffects.Register(
            new CantBeBlockedExceptByEffect(
                source: self,
                predicate: blocker => !BlockerIsColor(blocker, color),
                expiresAtEndOfTurn: true));
    }

    private static bool BlockerIsColor(ICard blocker, ManaColor color)
    {
        IReadOnlySet<ManaColor> colors = blocker is Permanent perm
            ? perm.GetEffectiveColors()
            : CardColors.GetColors(blocker);
        return colors.Contains(color);
    }

    private static string ColorName(ManaColor color) => color switch
    {
        ManaColor.White => "White",
        ManaColor.Blue => "Blue",
        ManaColor.Black => "Black",
        ManaColor.Red => "Red",
        ManaColor.Green => "Green",
        _ => "White",
    };
}
