using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reflection of Kiki-Jiki — the back face of
/// Fable of the Mirror-Breaker (Kamigawa: Neon Dynasty).
///
/// Enchantment Creature — Goblin Shaman 2/2 (NOT legendary). Oracle text:
///   "{1}{R}, {T}: Create a token that's a copy of another target
///    nonlegendary creature you control. That token has haste. Sacrifice
///    it at the beginning of the next end step."
///
/// This is the Reflection-of-Kiki-Jiki re-skin of Kiki-Jiki, Mirror
/// Breaker's printed ability — the copy mechanism is identical (CR 706.2
/// snapshot + "except it has haste" + delayed end-step removal). The only
/// printed differences are:
///   - cost <c>{1}{R}, {T}</c> instead of bare <c>{T}</c>;
///   - the spawned token is <b>sacrificed</b> (battlefield → graveyard,
///     CR 701.16) at the next end step rather than <b>exiled</b>;
///   - the card is a nonlegendary Enchantment Creature rather than a
///     Legendary Creature.
///
/// The copy-token mechanism is reused verbatim from
/// <see cref="KikiJikiMirrorBreakerFactory"/> (same resolve-time legality
/// recheck, same lossy CR 706.2 snapshot, same Haste grant + summoning-
/// sickness clear), so the v1 gaps documented there (choose-time legality
/// filter; Layer 1 copy lossiness; Layer 6 live ability-grant replay)
/// apply here too.
///
/// ## Faces
/// The card carries an <see cref="MdfcState"/> with
/// front = "Fable of the Mirror-Breaker", back = "Reflection of
/// Kiki-Jiki", pre-flipped to the back face — Reflection of Kiki-Jiki
/// only ever exists as the transformed (back) face on the battlefield
/// (CR 712.4). <see cref="FableOfTheMirrorBreakerFactory"/>'s chapter III
/// builds this permanent when the Saga transforms.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. The
///   activated ability is attached structurally; token creation falls
///   back to raw zone moves and the delayed sacrifice is NOT registered.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. Tokens publish <see cref="CardMovedEvent"/> on ETB and the
///   delayed end-step sacrifice is bus-driven via
///   <see cref="DelayedTriggeredAbility"/>.
/// </summary>
[CardName("Reflection of Kiki-Jiki")]
public static class ReflectionOfKikiJikiFactory
{
    public const string FrontName = "Fable of the Mirror-Breaker";
    public const string CardName = "Reflection of Kiki-Jiki";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Construct Reflection of Kiki-Jiki with no live runtime
    /// wiring. The activated ability is attached structurally for shape
    /// tests; tokens land via raw zone manipulation and the delayed
    /// end-step sacrifice is NOT registered.</summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>Construct Reflection of Kiki-Jiki with optional runtime
    /// services. When <paramref name="zoneService"/> is supplied the
    /// spawned token enters via <see cref="ZoneService"/> so
    /// <see cref="CardMovedEvent"/> publishes (ETB triggers fire). When
    /// <paramref name="triggers"/> is supplied the delayed end-step
    /// sacrifice is registered as a
    /// <see cref="DelayedTriggeredAbility"/>.</summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 712.4 — Reflection of Kiki-Jiki is a nonlegendary Enchantment
        // Creature — Goblin Shaman 2/2. Built as a Creature (carries P/T)
        // with the Enchantment card type added (CR 205.2a).
        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);
        card.AddCardType(CardType.Enchantment);

        // CR 712 — this face only exists as the transformed back face.
        card.MdfcState = new MdfcState(FrontName, CardName);
        if (!card.MdfcState.IsBackFace) card.MdfcState.Transform();

        AddCopyAbility(card, owner, zoneService, triggers);

        return card;
    }

    /// <summary>
    /// Attach the "{1}{R}, {T}: create a haste token copy of another
    /// target nonlegendary creature you control; sacrifice it at the
    /// next end step" activated ability. The copy body mirrors
    /// <see cref="KikiJikiMirrorBreakerFactory"/> verbatim — the only
    /// behavioural differences vs. Kiki-Jiki are the added {1}{R} mana
    /// cost and the end-step removal being a <b>sacrifice</b>
    /// (battlefield → graveyard) rather than an exile.
    /// </summary>
    public static void AddCopyAbility(
        Creature card,
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);

        ActivatedAbility? tapAbility = null;
        var tapEffect = new Effect(
            $"{CardName}: create a haste token copy of another target nonlegendary creature you control, sacrifice EOT",
            () =>
            {
                if (tapAbility == null) return;
                var chosen = tapAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature original) return;

                // CR 608.2b — resolve-time legality recheck (another /
                // nonlegendary / you-control / still on the battlefield).
                if (original.Zone != ZoneType.Battlefield) return;
                if (ReferenceEquals(original, card)) return;                  // "another"
                if (original.HasSupertype(CardSupertype.Legendary)) return;   // "nonlegendary"
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(original.Controller, controller)) return; // "you control"

                // CR 706.2 — snapshot copiable values (name, P/T, subtypes,
                // keyword names, colour). v1 lossy (see Kiki-Jiki factory).
                var keywords = new List<string>(
                    original.Abilities.OfType<KeywordAbility>()
                        .Select(k => k.Keyword));
                if (!keywords.Contains("Haste")) keywords.Add("Haste");

                var colours = CardColors.GetColors(original).ToList();

                var spec = new TokenFactory.TokenSpec(
                    Name: original.Name,
                    Power: original.BasePower,
                    Toughness: original.BaseToughness,
                    Subtypes: original.Subtypes.ToList(),
                    Keywords: keywords,
                    Colors: colours);

                var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

                // CR 702.10b — Haste lets the token attack immediately.
                token.HasSummoningSickness = false;

                // CR 603.7 — delayed end-step trigger to SACRIFICE the
                // spawned token (battlefield → graveyard, CR 701.16).
                if (triggers != null)
                {
                    var resolvedAt = DateTime.UtcNow;
                    var sacEffect = new Effect(
                        $"{CardName}: sacrifice token at next end step",
                        () =>
                        {
                            if (token.Zone != ZoneType.Battlefield) return;
                            if (!controller.Zones.Battlefield.GetCards().Contains(token)) return;

                            if (zoneService != null)
                            {
                                zoneService.MoveCard(token, ZoneType.Battlefield, ZoneType.Graveyard, controller);
                            }
                            else
                            {
                                controller.Zones.Battlefield.RemoveCard(token);
                                controller.Zones.Graveyard.AddCard(token);
                                token.SetZone(ZoneType.Graveyard);
                            }
                        });

                    var delayed = new DelayedTriggeredAbility(
                        source: card,
                        controller: controller,
                        condition: new EventTriggerCondition<StepStartedEvent>(
                            (e, _) => e.StepType == PhaseStateType.End
                                      && e.Timestamp > resolvedAt),
                        effects: new IEffect[] { sacEffect });

                    triggers.RegisterDelayed(delayed);
                }
            });

        tapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse(PrintedManaCost)),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { tapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target nonlegendary creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(tapAbility);
    }
}
