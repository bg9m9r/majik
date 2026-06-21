using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BECK half of the split/fuse card Beck // Call
/// (Dragon's Maze, {G}{U} // {4}{W}{U}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-03):
///   "Whenever a creature enters this turn, you may draw a card.
///    Fuse (You may cast one or both halves of this card from your hand.)"
///
/// Sister half — <see cref="CallFactory"/> ({4}{W}{U}; "Create four 1/1 white
/// Bird creature tokens with flying. Fuse ...").
///
/// ## The "this turn" repeating delayed trigger (CR 603.7e)
///
/// Beck creates a TURN-SCOPED REPEATING delayed triggered ability when it
/// resolves: "Whenever a creature enters this turn, you may draw a card." This
/// is NOT a one-shot delayed trigger (which fires once and unregisters,
/// CR 603.7) — it fires EVERY time a creature enters the battlefield for the
/// rest of the turn, then is torn down at end-of-turn cleanup
/// (CR 514.2 / CR 603.7e). The engine models this with
/// <see cref="RepeatingDelayedTriggeredAbility"/>, registered through the
/// existing <see cref="TriggerManager"/> via
/// <see cref="TriggerManager.RegisterDelayed"/> and expired in
/// <see cref="Majik.Core.Game.TurnDriver"/>'s cleanup step via
/// <see cref="TriggerManager.ExpireTurnScopedDelayedTriggers"/>. The trigger
/// is active in every zone (CR 603.7d) — Beck itself heads to the graveyard
/// after it resolves, but the delayed trigger persists.
///
/// Pairing this half with <see cref="CallFactory"/> via Fuse (CR 702.102) is
/// the classic combo: Call's four Birds each enter AFTER Beck has registered
/// the repeating trigger, so each Bird ETB draws a card. The engine has no
/// fuse-cast surface yet (shared v1 gap, see below), but the repeating delayed
/// trigger itself — the deferral this factory pays down — is fully modelled
/// and observable through <see cref="BuildResolveEffect"/>.
///
/// ## Implemented (v1)
/// - Sorcery identity at {G}{U} (green/blue, mana value 2), built from the
///   embedded JSON def (<c>beck.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="MdfcState"/> attached (front half — Beck; sister = Call).
/// - <b>Repeating delayed "whenever a creature enters this turn, you may draw
///   a card" trigger</b> (CR 603.7e) registered on resolve via
///   <see cref="BuildResolveEffect"/> — see the section above. The optional
///   draw ("you may", CR 603.5) is modelled by the <paramref name="mayDraw"/>
///   closure, defaulting to drawing (degrade-to-yes), mirroring
///   <see cref="CuriosityFactory"/>.
///
/// ## Fuse (CR 702.102) — IMPLEMENTED
/// - <b>Fuse</b> — casting BOTH halves from hand as one split spell is now
///   wired via <see cref="BeckCallFactory.BuildFusedDefinition"/> +
///   <see cref="Majik.Core.Game.SplitCardCast"/> /
///   <see cref="Majik.Core.Costs.FuseAlternativeCost"/>. This single-half
///   factory is unchanged: each half is still independently castable via its
///   own <c>[CardName]</c> factory.
/// </summary>
[CardName("Beck")]
public static class BeckFactory
{
    public const string CardName = "Beck";
    public const string SisterName = "Call";
    public const string Slug = "beck";
    public const string PrintedManaCost = "{G}{U}";

    /// <summary>
    /// Build the Beck half as a Sorcery from the embedded JSON def, with the
    /// <see cref="MdfcState"/> face tracker attached (front half — Beck).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(def, owner);

        // CR 712 — attach the split-card face tracker so the sister half's
        // printed name (Call) is observable from the Beck object. Informational
        // only, matching the Wear // Tear posture.
        card.MdfcState = new MdfcState(CardName, SisterName);
        return card;
    }

    /// <summary>
    /// Build Beck's resolve effect — register the turn-scoped REPEATING delayed
    /// triggered ability "Whenever a creature enters this turn, you may draw a
    /// card" (CR 603.7e) through <paramref name="triggers"/>.
    /// </summary>
    /// <param name="caster">The resolving caster — the trigger's controller and
    /// the player who may draw.</param>
    /// <param name="triggers">
    /// The trigger manager the repeating delayed trigger is registered on. When
    /// null the resolve is a no-op (suitable for shape tests); the repeating
    /// trigger is the whole point, so production wiring always supplies it.
    /// </param>
    /// <param name="mayDraw">
    /// Models the controller's "you may draw a card" yes/no choice each time
    /// the trigger fires (CR 603.5). When null it defaults to drawing
    /// (degrade-to-yes), matching <see cref="CuriosityFactory"/>. Returning
    /// false skips that fire's draw.
    /// </param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        TriggerManager? triggers = null,
        Func<bool>? mayDraw = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: register 'whenever a creature enters this turn, you may draw a card' (CR 603.7e)",
                () =>
                {
                    if (triggers == null)
                    {
                        return;
                    }

                    // CR 603.5 — optional draw. Default to drawing when no
                    // choice closure is supplied (degrade-to-yes).
                    var drawEffect = new Effect(
                        $"{CardName}: a creature entered — you may draw a card",
                        () =>
                        {
                            if (mayDraw != null && !mayDraw()) return;
                            DrawOne(caster);
                        });

                    // CR 603.7e — TURN-SCOPED REPEATING delayed trigger.
                    // "Whenever a creature enters" = ANY creature, any
                    // controller, entering the battlefield (CR 603.6e). Stays
                    // registered and fires on every such ETB until end-of-turn
                    // cleanup tears it down (TriggerManager
                    // .ExpireTurnScopedDelayedTriggers, invoked from
                    // TurnDriver.Cleanup). The source is the caster (Beck is in
                    // the graveyard by now — CR 603.7d, all zones active).
                    var delayed = new RepeatingDelayedTriggeredAbility(
                        source: caster,
                        controller: caster,
                        condition: Triggers.OnAnyCreatureEntersBattlefield(),
                        effects: new IEffect[] { drawEffect });

                    triggers.RegisterDelayed(delayed);
                }),
        };
    }

    /// <summary>
    /// Draw a single card for <paramref name="player"/> via raw library → hand
    /// zone moves. Empty-library halts the draw and stamps
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> so the SBA loop
    /// (CR 704.5b) ends the game on the next pass. Mirrors
    /// <see cref="CuriosityFactory"/>'s simple-draw shape.
    /// </summary>
    private static void DrawOne(Player player)
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
