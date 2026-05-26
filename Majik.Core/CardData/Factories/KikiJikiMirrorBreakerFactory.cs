using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Named-card factory for Kiki-Jiki, Mirror Breaker (Champions of
/// Kamigawa, {2}{R}{R}{R}).
///
/// Legendary Creature — Goblin Shaman 2/2. Oracle text:
///   "Haste
///    {T}: Create a token that's a copy of another target nonlegendary
///    creature you control, except it has haste. Exile it at the
///    beginning of the next end step."
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Goblin Shaman at printed cost
///   {2}{R}{R}{R}; <see cref="CardSupertype.Legendary"/> +
///   Goblin / Shaman subtypes so the Legend Rule (CR 704.5j) and
///   tribal lord scopes (Goblin Chieftain / Goblin Warchief) see
///   Kiki-Jiki correctly.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker so
///   combat helpers + summoning-sickness checks read it.
/// - <b>Activated ability (CR 602)</b>: <c>{T}: create a token copy of
///   another target nonlegendary creature you control with haste.</c>
///   Cost = <see cref="AdditionalCost.Tap"/> only (no mana).
///   <see cref="TargetRequest"/> declares a 1..1 "another target
///   nonlegendary creature you control"; the printed restrictions
///   (another / nonlegendary / you-control / creature) are enforced
///   at resolve via the standard CR 608.2b posture (same as
///   Heliod's "another target creature" — choose-time
///   <see cref="TargetRequest.LegalCandidates"/> remains empty pending
///   the wider live-battlefield gather plumbing). On resolution:
///   <ol>
///     <li>Snapshot the chosen target's copiable values per CR 706.2:
///         name, P/T, subtypes, keyword names, colour identity.</li>
///     <li>Add "Haste" to the keyword set (CR 702.10 / "except it has
///         haste" — added even when absent on the original).</li>
///     <li>Mint the token via <see cref="TokenFactory.CreateOnBattlefield"/>
///         under Kiki-Jiki's controller, routed through the supplied
///         <see cref="ZoneService"/> when one is wired so token-ETB
///         triggers (Impact Tremors / Soul Warden / Purphoros) fire.</li>
///     <li>Clear summoning sickness on the token (Haste — CR 702.10b).</li>
///     <li>Register a one-shot end-step exile (CR 603.7) on the
///         supplied <see cref="TriggerManager"/>, closed over the
///         specific token spawned by this activation.</li>
///   </ol>
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. The
///   activated ability is attached structurally; token creation falls
///   back to raw zone moves and the delayed exile is NOT registered.
///   Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> —
///   fully-wired overload. Tokens publish <see cref="CardMovedEvent"/>
///   on ETB and the delayed end-step exile is bus-driven via
///   <see cref="DelayedTriggeredAbility"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time legality filter</b>: "another target nonlegendary
///   creature you control" is checked at resolve, not at choose-time.
///   <see cref="TargetRequest.LegalCandidates"/> is left empty (same
///   posture as Heliod / Solitude / Snapcaster Mage — production
///   agent enumerates the live battlefield itself).
/// - <b>Layer 1 copy effect</b>: the token's P/T + keywords are
///   snapshotted at the moment the ability resolves; if the original's
///   characteristics change later (counters, +1/+1 boost, lord
///   anthems), the token does NOT track them. Aligns with the v1
///   <see cref="CopyEffect"/> lossiness documented on
///   <see cref="SplinterTwinFactory"/>.
/// - <b>Layer 6 ability copy fidelity</b>: the spawned token does
///   inherit any abilities that were granted to the original at the
///   moment of copy via <see cref="Card.Abilities"/> snapshot, but
///   doesn't yet replay live ability-grant lifecycles bound to the
///   original (an aura that grants {T}: do-thing to the original is
///   not re-wired to the token). Same v1 posture as Splinter Twin.
/// </summary>
[CardName("Kiki-Jiki, Mirror Breaker")]
public static class KikiJikiMirrorBreakerFactory
{
    public const string CardName = "Kiki-Jiki, Mirror Breaker";
    public const string PrintedManaCost = "{2}{R}{R}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Construct Kiki-Jiki, Mirror Breaker with no live
    /// runtime wiring. The activated ability is attached structurally
    /// for shape tests; tokens land on the battlefield via raw zone
    /// manipulation (no <see cref="CardMovedEvent"/>) and the delayed
    /// end-step exile is NOT registered.</summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>Construct Kiki-Jiki, Mirror Breaker with optional
    /// runtime services. When <paramref name="zoneService"/> is
    /// supplied, the spawned token enters the battlefield via
    /// <see cref="ZoneService"/> so <see cref="CardMovedEvent"/>
    /// publishes (ETB triggers from other permanents — Impact Tremors,
    /// Soul Warden — fire). When <paramref name="triggers"/> is
    /// supplied, the delayed end-step exile is registered as a
    /// <see cref="DelayedTriggeredAbility"/>.</summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste keyword marker. Combat + summoning-sickness
        // helpers read this via KeywordAbility lookups.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // {T}: Create a token that's a copy of another target
        // nonlegendary creature you control, except it has haste.
        // Exile it at the beginning of the next end step.
        //
        // Cost: {T} (AdditionalCost.Tap). No mana cost on the printed
        // activation. CR 602.1b — source is Kiki-Jiki himself.
        // ----------------------------------------------------------------
        ActivatedAbility? tapAbility = null;
        var tapEffect = new Effect(
            $"{CardName}: create a haste token copy of another target nonlegendary creature you control, exile EOT",
            () =>
            {
                if (tapAbility == null) return;
                var chosen = tapAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature original) return;

                // CR 608.2b — resolve-time legality recheck. The printed
                // restrictions (another / nonlegendary / you-control /
                // still on the battlefield) gate at resolve.
                if (original.Zone != ZoneType.Battlefield) return;
                if (ReferenceEquals(original, card)) return;                  // "another"
                if (original.HasSupertype(CardSupertype.Legendary)) return;   // "nonlegendary"
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(original.Controller, controller)) return; // "you control"

                // CR 706.2 — snapshot copiable values: name, P/T,
                // subtypes, keyword names, colour identity. v1 lossy:
                // doesn't track later changes to the original's
                // characteristics (see factory xmldoc).
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

                // CR 603.7 — delayed end-step trigger to exile the
                // spawned token. Closure captures the specific token
                // minted by this activation; bound at resolve time so
                // multiple activations register independent triggers.
                if (triggers != null)
                {
                    var resolvedAt = DateTime.UtcNow;
                    var exileEffect = new Effect(
                        $"{CardName}: exile token at next end step",
                        () =>
                        {
                            if (token.Zone != ZoneType.Battlefield) return;
                            if (!controller.Zones.Battlefield.GetCards().Contains(token)) return;

                            if (zoneService != null)
                            {
                                zoneService.MoveCard(token, ZoneType.Battlefield, ZoneType.Exile, controller);
                            }
                            else
                            {
                                controller.Zones.Battlefield.RemoveCard(token);
                                controller.Zones.Exile.AddCard(token);
                                token.SetZone(ZoneType.Exile);
                            }
                        });

                    var delayed = new DelayedTriggeredAbility(
                        source: card,
                        controller: controller,
                        condition: new EventTriggerCondition<StepStartedEvent>(
                            (e, _) => e.StepType == PhaseStateType.End
                                      && e.Timestamp > resolvedAt),
                        effects: new IEffect[] { exileEffect });

                    triggers.RegisterDelayed(delayed);
                }
            });

        tapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
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

        return card;
    }
}
