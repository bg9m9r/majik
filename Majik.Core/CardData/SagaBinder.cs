using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.Sagas;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// CR 714 — Saga binder. Detects Saga-subtype permanents, parses the
/// chapter list from their oracle text ("I —", "II —", "III, IV —",
/// etc.) to determine the final chapter number, and attaches a
/// <see cref="SagaState"/> with a generic per-chapter callback.
///
/// Per-card chapter effects (hardcoded by card.Name):
///   - Urza's Saga: I+II → spawn a 0/0 colourless Construct artifact
///     creature token with "This creature gets +1/+1 for each artifact
///     you control" (CDA-style P/T effect registered on the supplied
///     <see cref="ContinuousEffectsService"/> — token is a 0/0 SBA
///     victim without it). III → search controller's library for an
///     artifact card with mv ≤ 2, put it onto the battlefield, shuffle
///     (CR 701.19a / 701.20a). After III resolves, the Saga
///     self-sacrifices via the generic <see cref="SagaState"/> sacrifice
///     SBA (CR 714.5 / 704.5r). The Saga is BOTH a Land and an
///     Enchantment Saga — the primary runtime type is
///     <see cref="Land"/> (preferred by <c>PickPrimaryType</c>) with
///     <see cref="CardType.Enchantment"/> added via <c>AddCardType</c>;
///     the implicit "{T}: Add {C}" mana ability lives on the printed
///     oracle, so <see cref="OracleManaBinder"/> wires it on the
///     production load path; the named-card factory wires it inline.
///   - Fable of the Mirror-Breaker (// Reflection of Kiki-Jiki): I →
///     spawn a 2/2 red Goblin token whose attack trigger creates a
///     Treasure (CR 508.1f / 111.10), wired live when a TriggerManager
///     is supplied; II → "you may discard up to two, then draw that many"
///     (count chosen via the supplied chooser; default rummages maximally);
///     III → exile this Saga and return it transformed into Reflection of
///     Kiki-Jiki (CR 714.4 / 712.4) via ReflectionOfKikiJikiFactory, then
///     clear SagaState so the sacrifice SBA does not fire.
///   - The Legend of Roku (// Avatar Roku): I → exile top 3 of library and
///     grant a runtime exile-cast on each (Card.GrantRuntimeExileCast) so the
///     controller may play them; the grant clears at the end of the
///     controller's NEXT turn (CR 514.2) when an event bus is supplied (same
///     Cleanup-counting shape as Light Up the Stage). II → add one mana of any
///     color (chosen via the supplied color chooser; default {R}). III →
///     exile this Saga and return it transformed into Avatar Roku
///     (CR 714.4 / 712.4) via AvatarRokuFactory, then clear SagaState so the
///     sacrifice SBA does not fire.
///   - All other Sagas: chapter callback is a no-op (per-card effect
///     parsing is a future cut). The state still ticks so SBA
///     sacrifices the Saga after the final chapter.
/// </summary>
public static class SagaBinder
{
    private static readonly Regex ChapterMarker = new(
        @"\b(?<r>I{1,3}V?|IV|V{1,3}I?|IX|X)\s*[—,–]",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Bind a Saga's chapter handler. <paramref name="effects"/> is
    /// required for Urza's Saga's Construct token P/T rider (CDA-style
    /// "+1/+1 per artifact you control"); without it the token still
    /// spawns but enters as a 0/0 (SBA 704.5f sweep). <paramref name="zones"/>
    /// routes Urza's III tutor through <see cref="ZoneService"/> so ETB
    /// triggers on the tutored artifact fire.
    /// </summary>
    public static bool Bind(
        ICard card,
        CardEntity entity,
        ContinuousEffectsService? effects = null,
        ZoneService? zones = null,
        Majik.Core.Abilities.TriggerManager? triggers = null,
        IEventBus? eventBus = null,
        Func<int>? fableRummageChoice = null,
        Func<ManaColor>? rokuColorChoice = null)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (card is not Permanent perm) return false;
        if (!card.HasSubtype(CardSubtype.Saga)) return false;

        var text = entity.OracleText ?? string.Empty;
        var finalChapter = ParseFinalChapter(text);
        if (finalChapter < 1) finalChapter = 3; // safe default

        Action<int> onChapter = card.Name switch
        {
            "Urza's Saga" => MakeUrzasSagaChapterHandler(perm, effects, zones),
            "Fable of the Mirror-Breaker"
                or "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki"
                => MakeFableChapterHandler(perm, zones, triggers, eventBus, fableRummageChoice),
            "The Legend of Roku"
                or "The Legend of Roku // Avatar Roku"
                => MakeRokuChapterHandler(perm, zones, triggers, eventBus, rokuColorChoice),
            _ => _ => { /* generic saga — no-op effect, state still ticks */ },
        };

        perm.SagaState = new SagaState(perm, finalChapter, onChapter);
        return true;
    }

    /// <summary>
    /// Urza's Saga (Modern Horizons 2). Legendary Enchantment — Urza's
    /// Saga, also a Land.
    ///   I, II — Create a 0/0 colourless Construct artifact creature
    ///           token with "This creature gets +1/+1 for each artifact
    ///           you control."
    ///   III   — Search your library for an artifact card with mana
    ///           value 2 or less, put it onto the battlefield, then
    ///           shuffle.
    /// After III resolves the Saga sacrifices itself via the generic
    /// SBA path (<see cref="SagaState.ShouldBeSacrificed"/> →
    /// <c>SagaSacrificedCheck</c>; CR 714.5 / 704.5r).
    ///
    /// Construct shape is delegated to
    /// <see cref="KarnScionOfUrzaFactory.CreateConstructToken"/> — same
    /// 0/0 colourless Construct artifact-creature token + CDA "+1/+1
    /// per artifact you control" rider already in use by Karn, Scion of
    /// Urza's -2.
    ///
    /// III tutor (v1): deterministic — pick the first artifact card in
    /// the controller's library with <c>ManaCost.TotalValue ≤ 2</c>.
    /// CR 701.20a shuffle wired via <see cref="LibraryShuffle"/>. Same
    /// posture as <c>ChordOfCallingFactory</c>'s GSZ-style tutor when
    /// no agent is registered.
    /// </summary>
    private static Action<int> MakeUrzasSagaChapterHandler(
        Permanent perm,
        ContinuousEffectsService? effects,
        ZoneService? zones) => chapter =>
    {
        var controller = perm.Controller ?? perm.Owner!;
        switch (chapter)
        {
            case 1:
            case 2:
                KarnScionOfUrzaFactory.CreateConstructToken(controller, zones, effects);
                break;
            case 3:
                UrzasSagaTutorArtifact(controller, zones);
                break;
        }
    };

    /// <summary>
    /// CR 701.19a — search the controller's library for an artifact
    /// card with mana value ≤ 2, put it onto the battlefield, then
    /// shuffle (CR 701.20a). v1 deterministic picker — first matching
    /// card by library order (same posture as
    /// <see cref="StoneforgeMysticFactory"/>'s ETB tutor and
    /// Chord-of-Calling's no-agent fallback). Routes the move through
    /// <see cref="ZoneService.MoveCard"/> when available so ETB
    /// triggers on the tutored artifact fire (CR 603.6a).
    /// </summary>
    private static void UrzasSagaTutorArtifact(Player controller, ZoneService? zones)
    {
        var pick = controller.Zones.Library.GetCards()
            .FirstOrDefault(c =>
                c.HasType(CardType.Artifact) &&
                ManaCost.Parse(c.ManaCost).TotalValue <= 2);

        if (pick != null)
        {
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, controller);
            }
            else
            {
                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(controller);
            }
        }
        // CR 701.20a — shuffle regardless of whether anything was found.
        LibraryShuffle.ShuffleLibrary(controller, "urzas-saga");
    }

    /// <summary>
    /// Fable of the Mirror-Breaker (NEO, {2}{R}) // Reflection of Kiki-Jiki.
    /// I — Create a 2/2 red Goblin creature token with "Whenever this
    ///     creature attacks, create a Treasure token." The token's attack
    ///     trigger is wired live when a <paramref name="triggers"/> manager
    ///     is supplied; the Treasure is minted via
    ///     <see cref="Majik.Core.Tokens.TokenFactory.CreateTreasure"/>.
    /// II — You may discard up to two cards, then draw that many cards. The
    ///     "you may"/"up to two" choice is supplied by
    ///     <paramref name="rummageChoice"/> (clamped to [0, 2] and to the
    ///     hand size); the default deterministic policy discards as many as
    ///     possible (up to two) — i.e. always rummages.
    /// III — Exile this Saga, then return it transformed (Reflection of
    ///     Kiki-Jiki, CR 714.4 / 712.4). Modelled by exiling the Fable
    ///     Enchantment and minting the Reflection-of-Kiki-Jiki Enchantment
    ///     Creature (back face) on the battlefield under the same
    ///     controller via <see cref="ReflectionOfKikiJikiFactory"/>. The
    ///     <see cref="SagaState"/> is cleared so the generic Saga-sacrifice
    ///     SBA (CR 704.5r) does not fire on the transformed permanent.
    /// </summary>
    private static Action<int> MakeFableChapterHandler(
        Permanent perm,
        ZoneService? zones,
        Majik.Core.Abilities.TriggerManager? triggers,
        IEventBus? eventBus,
        Func<int>? rummageChoice) => chapter =>
    {
        var controller = perm.Controller ?? perm.Owner!;
        switch (chapter)
        {
            case 1:
                FableCreateGoblinToken(controller, zones, triggers);
                break;
            case 2:
                FableRummage(controller, rummageChoice);
                break;
            case 3:
                FableTransform(perm, controller, zones, triggers);
                break;
        }
    };

    /// <summary>
    /// Fable chapter I — CR 111 / 111.6. Create a 2/2 red Goblin creature
    /// token with "Whenever this creature attacks, create a Treasure
    /// token." (CR 508.1f). The attack trigger is attached to the token and
    /// registered with the supplied <paramref name="triggers"/> manager so
    /// a <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
    /// for the token queues the Treasure-creation ability (no-op wiring
    /// when no trigger manager is supplied — the token still exists with
    /// the ability attached for shape).
    /// </summary>
    private static void FableCreateGoblinToken(
        Player controller,
        ZoneService? zones,
        Majik.Core.Abilities.TriggerManager? triggers)
    {
        var goblin = Majik.Core.Tokens.TokenFactory.CreateOnBattlefield(
            new Majik.Core.Tokens.TokenFactory.TokenSpec(
                Name: "Goblin",
                Power: 2,
                Toughness: 2,
                Subtypes: new[] { CardSubtype.Goblin },
                Keywords: null,
                Colors: new[] { ManaColor.Red }),
            controller,
            zones);

        // CR 508.1f — "Whenever this creature attacks, create a Treasure
        // token." Attached to the token itself (same shape as Goblin
        // Rabblemaster's attack trigger), so when this specific Goblin
        // attacks the Treasure is minted.
        var treasureEffect = new Effect(
            "Fable Goblin: create a Treasure token on attack",
            () => Majik.Core.Tokens.TokenFactory.CreateTreasure(
                goblin.Controller ?? controller, zones));

        var attackTrigger = new Majik.Core.Abilities.TriggeredAbility(
            source: goblin,
            controller: controller,
            condition: Majik.Core.Abilities.Triggers.OnAttackSelf(goblin),
            effects: new IEffect[] { treasureEffect },
            activeZones: new[] { ZoneType.Battlefield });

        goblin.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    /// <summary>
    /// Fable chapter II — CR 701.7. "You may discard up to two cards, then
    /// draw that many cards." <paramref name="rummageChoice"/> picks how
    /// many cards to discard (the "you may"/"up to two" decision); the
    /// result is clamped to [0, 2] and to the current hand size. The
    /// default policy (null choice) discards as many as possible, up to
    /// two — matching the deterministic looter posture used elsewhere
    /// (Cathartic Reunion / Faithless Looting).
    /// </summary>
    private static void FableRummage(Player player, Func<int>? rummageChoice)
    {
        var handCount = player.Zones.Hand.GetCards().Count();
        var want = rummageChoice?.Invoke() ?? 2;
        var n = Math.Clamp(want, 0, Math.Min(2, handCount));
        if (n <= 0) return; // "you may" opt-out — discard 0, draw 0.

        DiscardUpToAndDraw(player, max: n);
    }

    /// <summary>
    /// Fable chapter III — CR 714.4 / 712.4. "Exile this Saga, then return
    /// it to the battlefield transformed." Modelled as: exile the Fable
    /// Enchantment, then mint the Reflection-of-Kiki-Jiki Enchantment
    /// Creature (back face) on the battlefield under the same controller.
    /// The Fable's <see cref="SagaState"/> is cleared so
    /// <c>SagaSacrificedCheck</c> (CR 704.5r) does not subsequently try to
    /// sacrifice it.
    /// </summary>
    private static void FableTransform(
        Permanent perm,
        Player controller,
        ZoneService? zones,
        Majik.Core.Abilities.TriggerManager? triggers)
    {
        // CR 714.4 — clear the Saga state first so the SBA can't sacrifice
        // the permanent we're about to exile + transform.
        perm.SagaState = null;
        if (perm.MdfcState != null && !perm.MdfcState.IsBackFace)
            perm.MdfcState.Transform();

        // Exile the Fable face.
        if (zones != null)
        {
            zones.MoveCardTo(perm, ZoneType.Exile, controller);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(perm);
            controller.Zones.Exile.AddCard(perm);
            perm.SetZone(ZoneType.Exile);
        }

        // Return it transformed — Reflection of Kiki-Jiki (back face) onto
        // the battlefield under the same controller.
        var reflection = ReflectionOfKikiJikiFactory.Create(
            controller, zones, triggers);

        reflection.SetZone(ZoneType.Library); // sentinel for ZoneService.MoveCardTo's from-check
        controller.Zones.Library.AddCard(reflection);
        if (zones != null)
        {
            zones.MoveCardTo(reflection, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(reflection);
            reflection.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(reflection);
        }
    }

    /// <summary>
    /// The Legend of Roku (TLA, {2}{R}{R}) // Avatar Roku.
    /// I — Exile the top three cards of your library. Until the end of your
    ///     next turn, you may play those cards. Cards move to exile (CR
    ///     701.20) and each gets a runtime exile-cast grant
    ///     (<see cref="Card.GrantRuntimeExileCast"/>) so the controller may
    ///     play them; the grant clears at the end of the controller's NEXT
    ///     turn (CR 514.2) when an <paramref name="eventBus"/> is supplied —
    ///     same Cleanup-counting shape as <see cref="LightUpTheStageFactory"/>.
    /// II — Add one mana of any color (CR 106.1). Color chosen via
    ///     <paramref name="colorChoice"/>; default {R}.
    /// III — Exile this Saga, then return it transformed (Avatar Roku,
    ///     CR 714.4 / 712.4) via <see cref="AvatarRokuFactory"/>; the
    ///     <see cref="SagaState"/> is cleared so the generic Saga-sacrifice SBA
    ///     (CR 704.5r) does not fire on the transformed creature.
    /// </summary>
    private static Action<int> MakeRokuChapterHandler(
        Permanent perm,
        ZoneService? zones,
        Majik.Core.Abilities.TriggerManager? triggers,
        IEventBus? eventBus,
        Func<ManaColor>? colorChoice) => chapter =>
    {
        var controller = perm.Controller ?? perm.Owner!;
        switch (chapter)
        {
            case 1:
                RokuImpulseExile(controller, n: 3, eventBus);
                break;
            case 2:
                var color = colorChoice?.Invoke() ?? ManaColor.Red;
                controller.AddManaToPool(ManaCost.Parse(ManaLetterFor(color)));
                break;
            case 3:
                RokuTransform(perm, controller, zones, eventBus, triggers);
                break;
        }
    };

    /// <summary>Roku chapter I — CR 701.20 / CR 118.9. Exile the top
    /// <paramref name="n"/> cards of <paramref name="controller"/>'s library
    /// and stamp a runtime exile-cast grant on each (cost = printed mana cost)
    /// so the controller may play them. When <paramref name="eventBus"/> is
    /// supplied, schedule the "until end of your next turn" cleanup on the
    /// controller's NEXT turn's Cleanup step (CR 514.2 — second Cleanup
    /// belonging to the controller after the Saga resolved on their turn).
    /// Mirrors <see cref="LightUpTheStageFactory.BuildResolveEffect"/>.</summary>
    private static void RokuImpulseExile(Player controller, int n, IEventBus? eventBus)
    {
        var stamped = new List<Card>(n);
        for (var i = 0; i < n; i++)
        {
            var top = controller.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) break; // library underflow — fewer grants

            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Exile.AddCard(top);
            top.SetZone(ZoneType.Exile);

            if (top is Card concrete)
            {
                concrete.GrantRuntimeExileCast(controller, concrete.ManaCostValue);
                stamped.Add(concrete);
            }
        }

        if (stamped.Count == 0 || eventBus == null) return;

        // CR 514.2 — first Cleanup owned by the controller is THIS turn's
        // (Saga ticks on the controller's main phase), the second is the
        // controller's NEXT turn's cleanup → clear the grant then.
        var cleanupsSeen = 0;
        Action<StepStartedEvent>? handler = null;
        handler = e =>
        {
            if (e.StepType != Majik.Core.StateMachine.PhaseStateType.Cleanup) return;
            if (!ReferenceEquals(e.Player, controller)) return;
            cleanupsSeen++;
            if (cleanupsSeen < 2) return;

            foreach (var s in stamped) s.ClearRuntimeExileCast();
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }

    /// <summary>Roku chapter III — CR 714.4 / 712.4. "Exile this Saga, then
    /// return it transformed." Exile the Roku Saga front face, flip the
    /// <see cref="MdfcState"/>, and mint Avatar Roku (back face) on the
    /// battlefield under the same controller via
    /// <see cref="AvatarRokuFactory"/>. Mirrors
    /// <see cref="FableTransform"/>.</summary>
    private static void RokuTransform(
        Permanent perm,
        Player controller,
        ZoneService? zones,
        IEventBus? eventBus,
        Majik.Core.Abilities.TriggerManager? triggers)
    {
        // CR 714.4 — clear the Saga state first so the SBA can't sacrifice the
        // permanent we're about to exile + transform.
        perm.SagaState = null;
        if (perm.MdfcState != null && !perm.MdfcState.IsBackFace)
            perm.MdfcState.Transform();

        // Exile the Roku front face.
        if (zones != null)
        {
            zones.MoveCardTo(perm, ZoneType.Exile, controller);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(perm);
            controller.Zones.Exile.AddCard(perm);
            perm.SetZone(ZoneType.Exile);
        }

        // Return it transformed — Avatar Roku (back face) onto the battlefield
        // under the same controller.
        var avatar = AvatarRokuFactory.Create(controller, zones, eventBus, triggers);

        avatar.SetZone(ZoneType.Library); // sentinel for ZoneService.MoveCardTo's from-check
        controller.Zones.Library.AddCard(avatar);
        if (zones != null)
        {
            zones.MoveCardTo(avatar, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(avatar);
            avatar.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(avatar);
        }
    }

    /// <summary>Single-pip mana letter for <paramref name="color"/>
    /// (CR 106.1b) — used by Roku chapter II's "add one mana of any
    /// color".</summary>
    private static string ManaLetterFor(ManaColor color) => color switch
    {
        ManaColor.White => "W",
        ManaColor.Blue => "U",
        ManaColor.Black => "B",
        ManaColor.Red => "R",
        ManaColor.Green => "G",
        _ => "R",
    };

    /// <summary>CR 701.7 — discard up to <paramref name="max"/> cards from
    /// the front of <paramref name="player"/>'s hand and draw the same
    /// number. v1: deterministic (no agent prompt). Player-choice opt-out
    /// ("you may") is deferred.</summary>
    private static void DiscardUpToAndDraw(Player player, int max)
    {
        var hand = player.Zones.Hand.GetCards().Take(max).ToList();
        foreach (var card in hand)
        {
            player.Zones.Hand.RemoveCard(card);
            player.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }

        var count = hand.Count;
        for (var i = 0; i < count; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.MarkTriedToDrawFromEmptyLibrary();
                return;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    private static int ParseFinalChapter(string oracleText)
    {
        var max = 0;
        foreach (Match m in ChapterMarker.Matches(oracleText))
        {
            var roman = m.Groups["r"].Value.ToUpperInvariant();
            // Multi-chapter markers like "II, III —" set max via both.
            foreach (var part in roman.Split(','))
            {
                var n = RomanToInt(part.Trim());
                if (n > max) max = n;
            }
        }
        return max;
    }

    private static int RomanToInt(string s) => s switch
    {
        "I" => 1, "II" => 2, "III" => 3, "IV" => 4, "V" => 5,
        "VI" => 6, "VII" => 7, "VIII" => 8, "IX" => 9, "X" => 10,
        _ => 0,
    };
}
