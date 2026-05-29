using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Agadeem's Awakening // Agadeem, the Undercrypt
/// (Zendikar Rising, {X}{B}{B}{B}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Return from your graveyard to the battlefield any number of target
///    creature cards that each have a different mana value X or less."
///
/// Back face — <see cref="AgadeemTheUndercryptFactory"/> (Land —
/// "As this land enters, you may pay 3 life. If you don't, it enters
/// tapped." / "{T}: Add {B}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="ShatterskullSmashingFactory"/> /
/// <see cref="ShatterskullTheHammerPassFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>agadeems-awakening.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time spell behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor X-spell graveyard returns).
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{X}{B}{B}{B}</c>, mono-black (three {B} pips),
///   owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Agadeem's Awakening",
///   back = "Agadeem, the Undercrypt"); starts on the front face.
/// - <see cref="SpellDefinition.HasVariableX"/> = true — the cast flow
///   prompts for X and stores it in <see cref="ChosenSpellParams.X"/>.
/// - One 0..N "any number of target creature cards" request
///   (MinTargets = 0; MaxTargets = <see cref="int.MaxValue"/>).
/// - Resolution (CR 608.2 / 701.20):
///     <list type="bullet">
///       <item>For each chosen target, resolve it to a live card and keep
///         only creature cards currently in the caster's graveyard
///         (CR 608.2b — illegal-at-resolution targets are dropped).</item>
///       <item>"different mana value X or less" (CR 601.2c) — keep only
///         cards whose mana value is ≤ X, and at most one card per distinct
///         mana value. The first legal target offered for a given mana
///         value wins; later duplicates of that mana value are dropped.</item>
///       <item>Each surviving card is returned from the graveyard to the
///         caster's battlefield via
///         <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (ZoneService-
///         routed when supplied so ETB triggers fire — CR 603.6a).</item>
///       <item>If no targets survive, the spell does nothing.</item>
///     </list>
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real targeting prompt</b>: the live cast flow supplies the chosen
///   targets through <see cref="ChosenSpellParams.Targets"/>; the resolver
///   maps tokens to live cards. The "each a different mana value" legality
///   is also a cast-time targeting restriction (CR 601.2c) — here it is
///   enforced defensively at resolution so an over-broad target set still
///   resolves to a legal subset. Same posture as
///   <see cref="ReanimateFactory"/> / <see cref="PriestOfFellRitesFactory"/>.
///
/// ## References
///
/// - <see cref="ShatterskullSmashingFactory"/> — companion ZNR MDFC X-spell
///   front face with the same HasVariableX + MdfcState shape.
/// - <see cref="ReanimateFactory"/> — graveyard → battlefield return body.
/// </summary>
[CardName("Agadeem's Awakening")]
public static class AgadeemsAwakeningFactory
{
    public const string CardName = "Agadeem's Awakening";
    public const string BackName = "Agadeem, the Undercrypt";

    /// <summary>
    /// Construct Agadeem's Awakening as a Sorcery (identity from JSON) with
    /// the <see cref="MdfcState"/> face tracker attached. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("agadeems-awakening");
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name is observable from the front-face card object.
        card.MdfcState = new MdfcState(CardName, BackName);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "return any number of target creature cards
    /// that each have a different mana value X or less"
    /// <see cref="SpellDefinition"/>.
    ///
    /// <see cref="SpellDefinition.HasVariableX"/> is true; the cast flow
    /// prompts for X and stores it in <see cref="ChosenSpellParams.X"/>.
    /// </summary>
    /// <param name="caster">Spell controller — destination battlefield and
    /// the graveyard whose creature cards are returned (CR 701.20 — "your
    /// graveyard").</param>
    /// <param name="resolver">Target resolver — maps the chosen target token
    /// to the live game object (expected to be a creature
    /// <see cref="Card"/> in the caster's graveyard).</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers on the returned creatures fire (CR 603.6a).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target creature cards in your graveyard with different mana value X or less",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate),
            },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                var rawTargets = chosen.Targets.Count > 0 ? chosen.Targets[0] : Array.Empty<object>();

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: return target creature cards (different mana value ≤ {x}) from graveyard to battlefield",
                        () => Resolve(caster, resolver, rawTargets, x, zoneService)),
                };
            });
    }

    /// <summary>
    /// Resolve the return. Honours CR 608.2b (drop illegal targets) and
    /// CR 601.2c ("different mana value X or less" — at most one card per
    /// distinct mana value, none above X).
    /// </summary>
    private static void Resolve(
        Player caster,
        Func<object, object> resolver,
        IReadOnlyList<object> rawTargets,
        int x,
        ZoneService? zoneService)
    {
        var usedManaValues = new HashSet<int>();
        // Snapshot the legal returns first so the zone moves below don't
        // perturb the in-flight selection (CR 608.2 — all of one spell's
        // instructions use the game state at the time it began resolving).
        var toReturn = new List<Creature>();

        foreach (var token in rawTargets)
        {
            var live = resolver(token);

            // CR 608.2b — target must still be a creature card in the
            // caster's graveyard at resolution.
            if (live is not Creature creature) continue;
            if (creature.Zone != ZoneType.Graveyard) continue;
            if (!ReferenceEquals(creature.Owner, caster)) continue;

            // CR 202.3b — mana value (the X spell's pip is the front-face
            // card's own; the mana value of a card on a non-stack zone uses
            // X = 0, so this is the printed total).
            var mv = creature.ManaCostValue.TotalValue;

            // "mana value X or less" (CR 601.2c).
            if (mv > x) continue;

            // "each have a different mana value" — at most one card per
            // distinct mana value (CR 601.2c). First legal target for a
            // given value wins.
            if (!usedManaValues.Add(mv)) continue;

            toReturn.Add(creature);
        }

        foreach (var creature in toReturn)
        {
            // CR 701.20 — graveyard → battlefield. ZoneService-routed when
            // supplied so ETB triggers fire (CR 603.6a).
            Fx.ReturnFromGraveyardToBattlefield(creature, caster, zoneService);
        }
    }
}
