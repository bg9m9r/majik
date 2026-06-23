using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Khalni Gem (Rise of the Eldrazi, {4}).
///
/// Colourless artifact. Oracle text (verified against Scryfall):
///   "When this artifact enters, return two lands you control to their
///    owner's hand.
///    {T}: Add two mana of any one color."
///
/// ## Implemented (v1)
/// - <b>Artifact identity</b> — colourless {4} artifact, owner / controller
///   wiring.
/// - <b>ETB return trigger (CR 603.6a)</b> — a <see cref="TriggeredAbility"/>
///   wired off <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution it
///   returns up to TWO lands the controller controls to their owner's hand.
///   The two picks are made one at a time via
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> (deterministic
///   first-fallback when no agent is registered — same posture as
///   <see cref="BounceLandCycleFactory"/>). Khalni Gem is an artifact, not a
///   land, so unlike the Karoo bounce lands it never needs a self-exclusion
///   filter — it is never a candidate. Fewer than two lands present → returns
///   as many as possible (CR 608.2b — "do as much as possible"); zero lands →
///   clean no-op. The bounce routes through <see cref="Fx.BounceToHand"/>,
///   which prefers <see cref="ZoneService.MoveCard"/> when one is resolvable
///   (the ambient <see cref="ZoneServiceRegistry"/> in live play) so LTB
///   triggers / replacements on the returned lands fire.
/// - <b>{T}: Add two mana of any one color</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG), each producing two
///   mana of that colour (CR 605.1a — "any one color" resolves to five distinct
///   single-colour mana abilities; only one mode fires per tap because the
///   shared {T} cost is paid once). Same any-one-colour modelling as Gilded
///   Lotus (<see cref="GildedLotusFactory"/>), scaled from three pips to two.
/// </summary>
[CardName("Khalni Gem")]
public static class KhalniGemFactory
{
    public const string CardName = "Khalni Gem";
    public const string PrintedManaCost = "{4}";

    /// <summary>
    /// Shape-only build (no event bus / trigger manager). The ETB return
    /// trigger is attached structurally but not registered; suitable for
    /// dispatcher / structural tests. Mirrors every other ETB-trigger
    /// factory's single-arg posture.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — see <see cref="FestivalCrasherFactory"/>). The live
    /// <see cref="TriggerManager"/> auto-binds any card carrying an
    /// <see cref="ITriggeredAbility"/> on its first battlefield entry, so no
    /// explicit manager is threaded here; the ETB trigger is simply attached to
    /// the card shape. The bounce resolves the live <see cref="ZoneService"/>
    /// from the ambient <see cref="ZoneServiceRegistry"/> at resolution time.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = effects; // reserved — trigger auto-binds via TriggerManager on entry.

        var gem = new Artifact(CardName, PrintedManaCost);
        gem.SetOwner(owner);
        gem.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this artifact enters, return two lands you control to
        //    their owner's hand."
        // Returns up to two of the controller's lands. Khalni Gem is an
        // artifact, not a land, so it is never itself a candidate (no
        // self-exclusion filter needed, unlike the Karoo bounce lands).
        // Fewer than two lands → return as many as possible (CR 608.2b);
        // zero lands → clean no-op.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: return two lands you control to their owner's hand",
            async ctx =>
            {
                var controller = gem.Controller ?? owner;
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                var zones = ZoneServiceRegistry.Get(controller);

                // Return two lands, choosing one at a time so the second pick
                // sees the post-first-bounce battlefield (CR 608.2b ordering).
                for (var i = 0; i < 2; i++)
                {
                    var candidates = controller.Zones.Battlefield.GetCards()
                        .Where(c => c.HasType(CardType.Land))
                        .ToList();
                    if (candidates.Count == 0) break; // CR 608.2b — nothing left.

                    ICard pick = candidates[0];
                    if (agent != null)
                    {
                        var chosen = await agent.ChooseFromBattlefieldAsync(
                            controller, candidates, BotIntent.Bounce)
                            .ConfigureAwait(false);
                        // Re-validate the agent's pick at resolution (CR 608.2b).
                        if (chosen != null && candidates.Contains(chosen))
                        {
                            pick = chosen;
                        }
                    }

                    Fx.BounceToHand(pick, zones);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: gem,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(gem),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        gem.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}: Add two mana of any one color.
        // CR 605.1a — "any one color" resolves to five distinct single-colour
        // mana abilities (one per WUBRG); only one fires per tap (shared {T}).
        // ManaCost.Parse("WW") → two White, etc. Same shape as Gilded Lotus
        // scaled from three pips to two.
        // ----------------------------------------------------------------
        foreach (var colour in new[] { "WW", "UU", "BB", "RR", "GG" })
        {
            gem.AddAbility(new ManaAbility(gem, owner, ManaCost.Parse(colour)));
        }

        return gem;
    }
}
