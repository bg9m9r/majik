using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormbreath Dragon (Theros, {3}{R}{R}).
///
/// Creature — Dragon 4/4. Oracle text:
///   "Flying, haste, protection from white
///    {5}{R}{R}: Monstrosity 3. (If this creature isn't monstrous, put
///    three +1/+1 counters on it and it becomes monstrous.)
///    When this creature becomes monstrous, it deals damage to each
///    opponent equal to the number of cards in that player's hand."
///
/// ## Implemented (v1)
///
/// - 4/4 Creature — Dragon at {3}{R}{R}, owner/controller wired.
/// - <b>Flying (CR 702.9) + Haste (CR 702.10)</b> — keyword markers via
///   <see cref="KeywordAbility"/>, read by the combat/block subsystem
///   the same way Slickshot Show-Off / Arclight Phoenix wire them.
/// - <b>Protection from white (CR 702.16)</b> — single
///   <see cref="ProtectionAbility"/>("white") instance. The Rules.Protection
///   helpers read the quality string; combat / damage / target / attach
///   gates all consult <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
///   per colour (mirrors <see cref="PhyrexianCrusaderFactory"/>'s "red +
///   white" pair — Stormbreath ships the white half only).
/// - <b>Monstrosity 3 (CR 702.95)</b> — no Monstrosity primitive exists
///   in the engine today, so the keyword is modelled as a bespoke
///   activated ability "{5}{R}{R}: If this creature isn't monstrous,
///   place three +1/+1 counters on Stormbreath Dragon and mark it
///   monstrous." The marker lives on the returned
///   <see cref="StormbreathDragonAbility"/> instance
///   (<see cref="StormbreathDragonAbility.IsMonstrous"/>) — the activated
///   ability self-gates so a second activation no-ops (CR 702.95b — "if
///   this creature isn't monstrous"). A future Monstrosity primitive
///   PR can lift the marker onto <see cref="Creature"/> and reuse the
///   gate.
/// - <b>"Becomes monstrous" trigger (CR 603.1)</b> — a real
///   <see cref="TriggeredAbility"/> (carried in <c>card.Abilities</c> so
///   the pool-wide audit's When/Whenever/At → <see cref="ITriggeredAbility"/>
///   check is satisfied). It deals damage to EACH opponent equal to the
///   number of cards in THAT opponent's own hand (a per-opponent variable
///   — opponents with empty hands take zero; the controller's hand size
///   is irrelevant). There is no hand-size threshold gate (the pre-errata
///   "if you have seven or more cards in hand, 3 damage" wording is no
///   longer printed). The opponents + their hand sizes are read from the
///   LIVE game at RESOLUTION via the effect's
///   <see cref="ResolutionContext.Game"/> (<c>ctx.Game.AllPlayers</c>) —
///   the same context-driven idiom as
///   <see cref="AmaliaBenavidesAguirreFactory"/> — so it works on the
///   single-arg shape build AND the production routed build with no
///   captured opponents resolver. The trigger's condition is
///   <see cref="Triggers.Never()"/> (it is never re-fired off the event
///   bus — no "becomes monstrous" engine event exists); instead the
///   monstrosity activation drives its effect inline when the creature
///   becomes monstrous, threading the live resolution context through
///   (the same "enqueue directly" posture the engine uses for Saga
///   chapter abilities, CR 714.2b).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — canonical build. Wires the full
///   ability set including the real becomes-monstrous
///   <see cref="TriggeredAbility"/>. The trigger reads opponents from the
///   live resolution context, so the damage is correct here too.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — the
///   effects-aware overload the source generator recognises and the
///   production <c>GameFacade</c> routed build dispatches to (via
///   <see cref="NamedCardFactory.Create(string, Player, ContinuousEffectsService?)"/>).
///   Stormbreath has no continuous effect to register, so this forwards
///   straight to the canonical overload — its ONLY purpose is to make the
///   generator emit the effects-aware dispatch arm so the routed prod
///   build wires the trigger (mirrors the
///   <see cref="FestivalCrasherFactory"/> / <see cref="KilnFiendFactory"/>
///   fix).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Monstrosity as a first-class keyword primitive</b>: no shared
///   "becomes monstrous" event / "is monstrous" flag is published or
///   read by anything else in the engine. The keyword + marker live
///   inside <see cref="StormbreathDragonAbility"/>; cards that care
///   ("Hold the Monastery", etc.) would need to subscribe to a future
///   primitive's event. Same posture as the Plot deferral noted on
///   <see cref="SlickshotShowOffFactory"/>.
/// - <b>Becomes-monstrous trigger queued on the stack</b>: today the
///   damage runs inline at the end of the monstrosity activation resolve
///   rather than queuing as a separate object on the stack after the
///   monstrosity activation finishes resolving (CR 603.3 — put on the
///   stack the next time a player would receive priority). The
///   per-opponent hand count is therefore locked in at the moment
///   monstrosity resolves rather than when a split trigger would resolve.
///   The two snapshots are indistinguishable for any card that doesn't
///   change an opponent's hand size in between, which is the typical case.
/// </summary>
[CardName("Stormbreath Dragon")]
public static class StormbreathDragonFactory
{
    public const string CardName = "Stormbreath Dragon";
    public const string PrintedManaCost = "{3}{R}{R}";
    public const int Power = 4;
    public const int Toughness = 4;
    public const string MonstrosityCost = "{5}{R}{R}";
    public const int MonstrosityCounters = 3;

    /// <summary>
    /// Canonical build. Wires Flying / Haste / protection-from-white, the
    /// Monstrosity 3 activated ability, AND the real becomes-monstrous
    /// <see cref="TriggeredAbility"/> whose damage reads opponents from the
    /// live resolution context.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dragon });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 / 702.10 — Flying + Haste keyword markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.16 — Protection from white. Quality string "white" is
        // read by Rules.Protection.HasProtectionFromColor (same wiring
        // shape as Phyrexian Crusader's red/white pair).
        card.AddAbility(new ProtectionAbility("white"));

        // ----------------------------------------------------------------
        // CR 603.1 — becomes-monstrous trigger.
        //   "When this creature becomes monstrous, it deals damage to each
        //    opponent equal to the number of cards in that player's hand."
        // A real TriggeredAbility (so the pool-wide audit sees it in
        // card.Abilities). Its damage effect reads opponents + hand sizes
        // from the LIVE game at RESOLUTION via ctx.Game.AllPlayers — no
        // captured opponents resolver — so it is correct on BOTH the
        // single-arg shape build and the production routed build (same
        // context-driven idiom as Amalia Benavides Aguirre). Condition is
        // Triggers.Never(): no "becomes monstrous" engine event exists, so
        // the trigger is never re-fired off the event bus; the monstrosity
        // activation drives its effect inline (the "enqueue directly"
        // posture the engine uses for Saga chapter abilities, CR 714.2b).
        // ----------------------------------------------------------------
        var becomesMonstrousEffect = new Effect(
            $"{CardName}: deal each opponent damage equal to that opponent's hand size (becomes monstrous)",
            ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 603.1 — read opponents from the live game at resolution.
                // No game context (shape-only Resolve/Execute) ⇒ no opponents
                // to hit, so the effect is a safe no-op.
                var players = ctx.Game?.AllPlayers;
                if (players == null) return ValueTask.CompletedTask;

                foreach (var opp in players)
                {
                    if (ReferenceEquals(opp, controller)) continue; // CR 102.1 — opponents only
                    if (opp.HasLost) continue;
                    // CR 701.31 — damage = the number of cards in THAT
                    // player's hand (per-opponent variable). Empty hand ⇒ 0.
                    var damage = opp.Zones.Hand.GetCards().Count();
                    if (damage <= 0) continue;
                    opp.RecordDamageDealt(damage); // CR 120.3
                    opp.LoseLife(damage);
                }

                return ValueTask.CompletedTask;
            });

        var becomesMonstrousTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.Never(),
            effects: new IEffect[] { becomesMonstrousEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(becomesMonstrousTrigger);

        // CR 702.95 — Monstrosity 3. Modelled as a bespoke activated
        // ability since no Monstrosity primitive exists yet. The
        // ability owns its own "monstrous" flag (see
        // StormbreathDragonAbility.IsMonstrous) and self-gates: a second
        // activation no-ops (CR 702.95b — "If this creature isn't
        // monstrous, …").
        StormbreathDragonAbility? monstrosity = null;
        var monstrosityEffect = new Effect(
            $"{CardName}: become monstrous — +3/+3 counters, then deal each opponent damage equal to that opponent's hand size",
            async ctx =>
            {
                if (monstrosity == null) return;
                if (monstrosity.IsMonstrous) return; // CR 702.95b — self-gate

                // CR 702.95a — place +1/+1 counters and mark monstrous.
                card.Counters.Add(CounterType.PlusOnePlusOne, MonstrosityCounters);
                monstrosity.IsMonstrous = true;

                // CR 603.1 — the becomes-monstrous trigger fires. There is no
                // engine "becomes monstrous" event, so drive the real
                // TriggeredAbility's effect inline, threading THIS activation's
                // live resolution context (ctx.Game / agent) so the damage
                // reads opponents from the live game. (See class xmldoc
                // "Deferred" for the split-on-stack gap.)
                foreach (var effect in becomesMonstrousTrigger.Effects)
                {
                    await effect.ExecuteAsync(ctx).ConfigureAwait(false);
                }
            });

        monstrosity = new StormbreathDragonAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(MonstrosityCost) },
            effects: new IEffect[] { monstrosityEffect });

        card.AddAbility(monstrosity);

        return card;
    }

    /// <summary>
    /// Effects-aware overload the source generator recognises and the
    /// <b>production</b> <c>GameFacade</c> routed build dispatches to (via
    /// <see cref="NamedCardFactory.Create(string, Player, ContinuousEffectsService?)"/>).
    /// Stormbreath registers no continuous effect, so this forwards straight
    /// to the canonical <see cref="Create(Player)"/> — its sole purpose is to
    /// make the generator emit the effects-aware dispatch arm so the routed
    /// prod build wires the becomes-monstrous trigger (without it the routed
    /// build fell through to single-arg dispatch — same fix as Festival
    /// Crasher / Kiln Fiend). The <paramref name="effects"/> service is
    /// intentionally unused; the becomes-monstrous damage reads opponents
    /// from the live resolution context at resolution time, not from a
    /// registered continuous effect.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner);
}

/// <summary>
/// Bespoke <see cref="ActivatedAbility"/> for Stormbreath Dragon's
/// Monstrosity 3. Owns the "monstrous" marker (<see cref="IsMonstrous"/>)
/// since no Monstrosity primitive yet exists in the engine. The
/// activated effect self-gates on <see cref="IsMonstrous"/> so a second
/// activation no-ops (CR 702.95b — "If this creature isn't monstrous,
/// …"). Exposed publicly so tests / bots can inspect the live
/// monstrous state.
/// </summary>
public sealed class StormbreathDragonAbility : ActivatedAbility
{
    /// <summary>
    /// CR 702.95b — the monstrous marker. Flipped to <c>true</c> by the
    /// Monstrosity 3 activation resolving; never flipped back (CR
    /// 702.95c — once monstrous, a creature stays monstrous as long
    /// as it remains on the battlefield with that identity).
    /// </summary>
    public bool IsMonstrous { get; set; }

    public StormbreathDragonAbility(
        ICard source,
        Player controller,
        ICost[] costs,
        IEffect[] effects)
        : base(source, controller, costs: costs, effects: effects)
    {
    }
}
