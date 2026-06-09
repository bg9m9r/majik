using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Scans oracle text for common triggered-ability phrasings and synthesizes
/// <see cref="TriggeredAbility"/> instances attached to a permanent.
///
/// Templates handled (first match per line wins):
///   "When [ ~ | this creature ] enters the battlefield, ..."  → ETB
///   "When ~ dies, ..."                                       → death trigger
///   "Whenever ~ deals combat damage to a player, ..."        → combat
///
/// Effect tail is fed back through <see cref="OracleSpellBinder"/>-style
/// simple templates: "you gain N life", "draw N cards", "deals N damage
/// to any target" (caster-side; targets deferred — phase 21.5 will route
/// through agent prompts).
/// </summary>
public static class OracleTriggeredAbilityBinder
{
    private static readonly Regex EtbLine = new(
        @"When(ever)?\s+(?<ref>~|this creature|this artifact|this enchantment|this permanent|this land)\s+enters(\s+the\s+battlefield)?\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex DiesLine = new(
        @"When\s+(?<ref>~|this creature)\s+dies\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex CombatDamagePlayer = new(
        @"Whenever\s+(?<ref>~|this creature)\s+deals\s+combat\s+damage\s+to\s+a\s+player\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex AttackLine = new(
        @"Whenever\s+(?<ref>~|this creature)\s+attacks\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);

    private static readonly Regex YouGainLife = new(
        @"you\s+gain\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+li(?:fe|ves)",
        RegexOptions.IgnoreCase);
    private static readonly Regex DrawCards = new(
        @"draw\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);
    // "surveil N" — CR 701.42. Common ETB rider on DSK surveil lands
    // (Underground Mortuary, Lush Portico, Meticulous Archive, Shadowy
    // Backstreet, Thundering Falls) and various spells. We bind the inner
    // surveil effect; the binder above wires the ETB trigger when paired
    // with "When this land enters, surveil N".
    private static readonly Regex SurveilN = new(
        @"surveil\s+(?<n>\d+|a|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase);
    // "scry N" — CR 701.20. ETB rider on the Theros-block "Temple" scry-land
    // cycle ("When this land enters, scry 1.") and various spells. We bind the
    // inner scry effect; the EtbLine binder above wires the ETB trigger. The
    // `\b` after the number keeps it from greedily swallowing the reminder
    // text that follows in parentheses (a separate sentence after the period).
    private static readonly Regex ScryN = new(
        @"scry\s+(?<n>\d+|a|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase);
    // "return a land you control to its owner's hand" — CR 701.20. ETB rider on
    // the Ravnica Karoo / "bounce land" cycle ("When this land enters, return a
    // land you control to its owner's hand."). The controller chooses which
    // land they control to bounce. Lands aren't routed through named factories,
    // so this is the only prod binding path.
    private static readonly Regex ReturnALandYouControlToHand = new(
        @"return\s+a\s+land\s+you\s+control\s+to\s+its\s+owner'?s\s+hand",
        RegexOptions.IgnoreCase);
    private static readonly Regex DealDamageOpponent = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+damage\s+to\s+(that\s+player|any\s+opponent)",
        RegexOptions.IgnoreCase);
    // ------------------------------------------------------------------
    // Utility-land ETB effect riders (CR 603.6e). These are ETB triggers on
    // LANDS, which production NEVER routes through their [CardName] factory
    // (FactoryRouting excludes lands) — the binder chain is the only prod
    // binding path, so the effects MUST be synthesized here or the lands ETB
    // with no trigger at all. Each oracle was verified against
    // EmbeddedCardRepository.GetByName(...).OracleText. Targeting follows the
    // established binder posture: "target creature" effects consult the agent
    // via ChooseFromBattlefieldAsync with a deterministic first-eligible
    // fallback (mirrors the Karoo bounce-land path); "target player" effects
    // hit the first opponent with a controller fallback (mirrors Endurance /
    // Bojuka Bog). Real TargetRequest-driven targeting for binder-bound
    // triggers is deferred engine-wide (the named factories carry the rich
    // TargetRequest shape for the test path).
    // ------------------------------------------------------------------

    // Khalni Garden (Worldwake): "create a 0/1 green Plant creature token."
    private static readonly Regex CreatePlantToken = new(
        @"create\s+a\s+0/1\s+green\s+plant\s+creature\s+token",
        RegexOptions.IgnoreCase);
    // Piranha Marsh (Conflux): "target player loses 1 life." Life loss
    // (CR 119.3), NOT damage — no damage event / replacement applies.
    private static readonly Regex TargetPlayerLosesLife = new(
        @"target\s+player\s+loses\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+life",
        RegexOptions.IgnoreCase);
    // Teetering Peaks (Zendikar): "target creature gets +N/+0 until end of
    // turn." Pump (CR 613.1g layer 7c, CR 514.2 EOT expiry).
    private static readonly Regex TargetCreatureGetsPump = new(
        @"target\s+creature\s+gets\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // Sejiri Steppe (Zendikar): "target creature you control gains protection
    // from the color of your choice until end of turn." (CR 702.16.)
    private static readonly Regex TargetCreatureYouControlGainsProtection = new(
        @"target\s+creature\s+you\s+control\s+gains\s+protection\s+from\s+the\s+color\s+of\s+your\s+choice\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // Soaring Seacliff (Zendikar): "target creature gains flying until end of
    // turn." (CR 702.9.)
    private static readonly Regex TargetCreatureGainsFlying = new(
        @"target\s+creature\s+gains\s+flying\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // Halimar Depths (Worldwake): "look at the top three cards of your
    // library, then put them back in any order." A reorder-only look — never
    // bottoms a card, so this is NOT scry. Routed through ScryAction.Peek /
    // Apply with ToBottom = [] (same primitive the scry path uses).
    private static readonly Regex LookAtTopThreeReorder = new(
        @"look\s+at\s+the\s+top\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+cards\s+of\s+your\s+library\s*,\s*then\s+put\s+them\s+back\s+in\s+any\s+order",
        RegexOptions.IgnoreCase);
    // Mortuary Mire (Battle for Zendikar): "you may put target creature card
    // from your graveyard on top of your library." (CR 701.20.) Graveyard
    // recursion to top of library.
    private static readonly Regex PutCreatureFromGraveyardOnTop = new(
        @"(?:you\s+may\s+)?put\s+target\s+creature\s+card\s+from\s+your\s+graveyard\s+on\s+top\s+of\s+your\s+library",
        RegexOptions.IgnoreCase);
    // Sunscorched Desert (Hour of Devastation): "it deals 1 damage to target
    // player or planeswalker." (Does NOT enter tapped.) Damage (CR 119.1d).
    private static readonly Regex DealsDamageToTargetPlayerOrPw = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+damage\s+to\s+target\s+player\s+or\s+planeswalker",
        RegexOptions.IgnoreCase);
    // Rupture Spire / Transguild Promenade: "sacrifice it unless you pay {1}."
    // (CR 603.1 pay-or-sacrifice.) The cost pip is parameterized.
    private static readonly Regex SacrificeItUnlessYouPay = new(
        @"sacrifice\s+it\s+unless\s+you\s+pay\s+\{(?<cost>[^}]+)\}",
        RegexOptions.IgnoreCase);
    // Crumbling Vestige (Oath of the Gatewatch): "add one mana of any color."
    // One-shot ETB mana (CR 106) — NOT a mana ability (it's a triggered
    // ability that uses the stack).
    private static readonly Regex AddOneManaOfAnyColor = new(
        @"add\s+one\s+mana\s+of\s+any\s+color",
        RegexOptions.IgnoreCase);
    private static readonly Regex CreateTreasure = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+treasure\s+tokens?",
        RegexOptions.IgnoreCase);
    private static readonly Regex CreateClue = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+clue\s+tokens?",
        RegexOptions.IgnoreCase);
    private static readonly Regex PutPlusCounterOnSelf = new(
        @"put\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+\+1/\+1\s+counters?\s+on\s+~",
        RegexOptions.IgnoreCase);
    private static readonly Regex GetEnergy = new(
        @"you get\s+((?:\{E\}\s*)+)",
        RegexOptions.IgnoreCase);
    private static readonly Regex DestroyTargetLand = new(
        @"(?:you\s+may\s+)?destroy\s+target\s+land",
        RegexOptions.IgnoreCase);
    // "Target player puts all the cards from their graveyard on the bottom
    // of their library in a random order." — Endurance (MH2).
    private static readonly Regex EtbGraveyardToLibraryBottom = new(
        @"target\s+player\s+puts\s+all\s+the\s+cards\s+from\s+their\s+graveyard\s+on\s+the\s+bottom\s+of\s+their\s+library\s+in\s+a\s+random\s+order",
        RegexOptions.IgnoreCase);
    // "exile target player's graveyard" — Bojuka Bog (Worldwake / MH2 reprint),
    // a LAND whose only prod binding path is this binder (lands aren't routed
    // through named factories). CR 406.6 — exiling a graveyard moves every card
    // in it to the exile zone. CR 608.2b — empty graveyard is a clean no-op.
    private static readonly Regex ExileTargetPlayersGraveyard = new(
        @"exile\s+target\s+player'?s\s+graveyard",
        RegexOptions.IgnoreCase);
    private static readonly Regex AnotherCreatureEnters = new(
        @"whenever another creature you control enters\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex LandfallLine = new(
        @"(?:landfall\s*[—-]\s*)?whenever a land (?:you control\s+)?enters(?:\s+the battlefield)?(?:\s+under your control)?\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex UpkeepLine = new(
        @"at the beginning of (?:your |each player's )?upkeep\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex EndStepLine = new(
        @"at the beginning of (?:your |each player's )?end step\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    // "Whenever a player casts a spell with mana value N or less, ~ deals X
    // damage to that player." — Eidolon of the Great Revel pattern. The
    // "to that player" inside the effect refers to the spell caster, NOT the
    // ability's controller; BuildEffects handles "that player" via the
    // shared resolver and SpellCastEvent.Spell.Controller.
    private static readonly Regex PlayerCastsCheapSpellLine = new(
        @"whenever\s+(?:a|each)\s+player\s+casts\s+a\s+spell\s+with\s+mana\s+value\s+(?<cmc>\d+|one|two|three|four|five|six|seven)\s+or\s+less\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);

    // Sanctum of Ugin (BFZ): "Whenever you cast a colorless spell with mana
    // value N or greater, you may sacrifice this land. If you do, search your
    // library for a colorless creature card, reveal it, put it into your hand,
    // then shuffle." (CR 603.1.) Sanctum is a LAND — production builds it
    // through the binder chain (never its named factory), so this controller-
    // scoped SpellCastEvent trigger MUST be synthesized here to work in real
    // games. The whole two-sentence body is matched at once; only the MV
    // threshold is parameterized (`mv`).
    private static readonly Regex YouCastColorlessHighMvSacTutor = new(
        @"whenever\s+you\s+cast\s+a\s+colorless\s+spell\s+with\s+mana\s+value\s+(?<mv>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+or\s+greater\s*,\s*you\s+may\s+sacrifice\s+(?:~|this\s+land|this\s+permanent)\.?\s*if\s+you\s+do\s*,\s*search\s+your\s+library\s+for\s+a\s+colorless\s+creature\s+card\s*,\s*reveal\s+it\s*,\s*put\s+it\s+into\s+your\s+hand\s*,\s*then\s+shuffle",
        RegexOptions.IgnoreCase);

    // Goblin Guide attack pattern: "Whenever ~ attacks, defending player
    // reveals the top card of their library. If it's a land card, that
    // player puts it into their hand." Two sentences — the AttackLine
    // regex only captures up to the first period, so this whole-text
    // pattern is matched separately and emits its own TriggeredAbility.
    // The predicate captures the defending player from
    // CreatureAttacksEvent so the effect resolves against the correct
    // library at resolution time (CR 508.1f, CR 506.2).
    private static readonly Regex GoblinGuideFullPattern = new(
        @"whenever\s+(?:~|this\s+creature)\s+attacks\s*,\s*defending\s+player\s+reveals\s+the\s+top\s+card\s+of\s+(?:their|that\s+player'?s)\s+library\.?\s*if\s+it'?s\s+a\s+land(?:\s+card)?\s*,\s*(?:that\s+player|they)\s+puts?\s+it\s+into\s+(?:their|that\s+player'?s)\s+hand",
        RegexOptions.IgnoreCase);

    // Ragavan-style combat-damage rider: "exile the top card of that player's
    // library" (Scryfall phrasing) or "that player exiles the top card of
    // their library" (per-card-text phrasing). Matched inside the
    // CombatDamagePlayer block; the predicate captures the damaged player
    // from CombatDamageDealtEvent so the effect closure exiles from the
    // correct library.
    private static readonly Regex ExileTopOfThatPlayersLibrary = new(
        @"(?:(?:that\s+player|they)\s+exiles?\s+the\s+top\s+card\s+of\s+(?:their|that\s+player'?s)\s+library|exile\s+the\s+top\s+card\s+of\s+that\s+player'?s\s+library)",
        RegexOptions.IgnoreCase);

    public static IEnumerable<TriggeredAbility> Bind(
        ICard source, CardEntity entity, Player? controller = null,
        IReadOnlyList<Player>? allPlayers = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        var ctrl = controller ?? source.Controller ?? source.Owner;
        if (ctrl == null) yield break;

        // Scryfall's oracle text uses the card's literal name (e.g. "Ragavan
        // deals combat damage…"); our regexes use `~` as the self-reference
        // placeholder. Normalise by replacing every occurrence of the
        // card's full name AND the short-name fragment before the comma
        // (e.g. "Ragavan, Nimble Pilferer" → match both "Ragavan" and the
        // full name) with `~`.
        var text = entity.OracleText ?? string.Empty;
        if (!string.IsNullOrEmpty(entity.Name))
        {
            text = text.Replace(entity.Name, "~");
            var commaIdx = entity.Name.IndexOf(',');
            if (commaIdx > 0)
            {
                var shortName = entity.Name[..commaIdx];
                text = text.Replace(shortName, "~");
            }
        }

        foreach (Match m in EtbLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source, allPlayers).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnEnterBattlefieldSelf(source),
                effects: effects);
        }

        foreach (Match m in DiesLine.Matches(text))
        {
            var effectText = m.Groups["effect"].Value;
            var effects = BuildEffects(effectText, ctrl, source, allPlayers).ToList();
            if (effects.Count == 0) continue;
            // CR 603.6a: dies-trigger sources must be active in Graveyard too,
            // because ZoneService moves the card before publishing CardMovedEvent.
            // Mirror UndyingFactory's activeZones approach.
            var hasDestroyLand = DestroyTargetLand.IsMatch(effectText);
            var activeZones = hasDestroyLand
                ? new[] { ZoneType.Battlefield, ZoneType.Graveyard }
                : (IEnumerable<ZoneType>?)null;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnDies(source),
                effects: effects,
                activeZones: activeZones);
        }

        foreach (Match m in CombatDamagePlayer.Matches(text))
        {
            var effectText = m.Groups["effect"].Value;
            var effects = BuildEffects(effectText, ctrl, source).ToList();

            // Ragavan-style "that player exiles the top card of their library"
            // rider — needs the *damaged* player resolved at fire-time. We
            // capture them via a per-ability shared field set by the
            // predicate (CombatDamageDealtEvent.TargetPlayer) so the effect
            // closure exiles from the correct library at resolution time.
            // (CR 603.3 — trigger condition evaluation runs before stack
            // push, so the capture is fresh when the ability resolves.)
            var wantsExileTop = ExileTopOfThatPlayersLibrary.IsMatch(effectText);
            Player? capturedDamaged = null;

            if (wantsExileTop)
            {
                effects.Add(new Effect("exile top of damaged player's library", () =>
                {
                    var victim = capturedDamaged;
                    if (victim == null) return;
                    var top = victim.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null) return;
                    victim.Zones.Library.RemoveCard(top);
                    victim.Zones.Exile.AddCard(top);
                    top.SetZone(ZoneType.Exile);
                }));
            }

            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                {
                    if (!ReferenceEquals(e.Source, source)) return false;
                    if (e.TargetPlayer == null) return false;
                    capturedDamaged = e.TargetPlayer;
                    return true;
                }),
                effects: effects);
        }

        // "Whenever ~ attacks, ..." per-attacker trigger (CR 508.1f).
        foreach (Match m in AttackLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnAttackSelf(source),
                effects: effects);
        }

        // Goblin Guide attack pattern (CR 508.1f). Whole-text match because
        // the effect spans two sentences ("reveals … library. If it's a
        // land …"), which the single-sentence AttackLine regex truncates.
        // Predicate captures the defending player from CreatureAttacksEvent
        // so the effect closure resolves against the correct library.
        foreach (Match _gg in GoblinGuideFullPattern.Matches(text))
        {
            Player? capturedDefender = null;
            var effect = new Effect("Goblin Guide: defender reveals top; land → hand", () =>
            {
                var def = capturedDefender;
                if (def == null) return;
                var top = def.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                // v1: reveal is event-only (no explicit CardRevealedEvent;
                // a future iteration can emit one for log subscribers). The
                // observable side-effect is the land move.
                if (!top.HasType(CardType.Land)) return;
                def.Zones.Library.RemoveCard(top);
                def.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

            yield return new TriggeredAbility(
                source, ctrl,
                new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
                {
                    if (!ReferenceEquals(e.Attacker, source)) return false;
                    capturedDefender = e.DefendingPlayerOrPlaneswalker as Player;
                    return true;
                }),
                effects: new IEffect[] { effect });
            // Only one Goblin Guide trigger per card; break after the first
            // match to avoid double-binding if the regex matches twice.
            break;
        }

        // "Whenever a player casts a spell with mana value N or less, ~ deals
        // X damage to that player." (Eidolon of the Great Revel, CR 603.1.)
        // The trigger fires globally on SpellCastEvent filtered by CMC and
        // resolves with the spell's caster as "that player". BuildEffects
        // doesn't know how to resolve "that player" symbolically — we
        // build the damage effect inline so the closure captures the caster
        // from the event.
        foreach (Match m in PlayerCastsCheapSpellLine.Matches(text))
        {
            var cmcCeiling = WordToInt(m.Groups["cmc"].Value);
            var effectText = m.Groups["effect"].Value;
            var damageMatch = DealDamageOpponent.Match(effectText);
            if (!damageMatch.Success) continue;
            var dmg = WordToInt(damageMatch.Groups["n"].Value);
            yield return new TriggeredAbility(
                source, ctrl,
                new EventTriggerCondition<Majik.Core.Domain.DomainEvents.SpellCastEvent>(
                    (e, _) =>
                    {
                        var costString = e.Spell.Card.ManaCost ?? "";
                        var cost = Majik.Core.ValueObjects.ManaCost.Parse(costString);
                        return cost.TotalValue <= cmcCeiling;
                    }),
                effects: new[]
                {
                    new Effect($"~ deals {dmg} to spell caster", () =>
                    {
                        // Effect closures don't get the event; the trigger
                        // captures the relevant caster via TriggeredAbility's
                        // event-context plumbing. v1 simplification: each
                        // creature controller targets each opponent equally —
                        // pulled via allPlayers from the binder. Damage goes
                        // to ALL non-controller players, which matches the
                        // common 2-player case (Eidolon hits the opponent who
                        // cast); multiplayer correctness deferred.
                        if (allPlayers == null) return;
                        foreach (var pl in allPlayers)
                        {
                            if (ReferenceEquals(pl, ctrl)) continue;
                            pl.LoseLife(dmg);
                        }
                    }),
                });
        }

        // Sanctum of Ugin (BFZ): "Whenever you cast a colorless spell with mana
        // value N or greater, you may sacrifice this land. If you do, search
        // your library for a colorless creature card, reveal it, put it into
        // your hand, then shuffle." (CR 603.1.)
        //
        // This is a LAND — production builds it through the binder chain (never
        // the named factory), so the trigger is synthesized here. Built inline
        // (not via BuildEffects) because the sac-then-tutor body is bespoke:
        //   - Trigger condition: SpellCastEvent whose Spell.Controller is this
        //     card's controller ("you cast"), the spell is colorless
        //     (CardColors.GetColors == empty, CR 105), and its mana value ≥ N
        //     (printed cost TotalValue; X = 0 per CR 202.3e).
        //   - Effect: "you may sacrifice" → consult the agent (default YES, the
        //     branch is strictly card-advantageous). On YES sacrifice this card
        //     to its owner's graveyard (CR 701.16), search the controller's
        //     library for a colorless creature (agent picks; else first), move
        //     it to hand, and shuffle (CR 701.20a). On NO, nothing happens.
        // activeZones defaults to Battlefield (CR 113.6) — matches the named
        // factory's SanctumOfUginFactory shape.
        foreach (Match m in YouCastColorlessHighMvSacTutor.Matches(text))
        {
            var mvThreshold = WordToInt(m.Groups["mv"].Value);
            if (mvThreshold <= 0) continue;
            if (source is not Permanent self) continue;

            var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            {
                var liveController = self.Controller ?? ctrl;
                if (!ReferenceEquals(e.Spell.Controller, liveController))
                    return false;
                var spellCard = e.Spell.Card;
                if (CardColors.GetColors(spellCard).Count != 0)
                    return false;
                var costStr = spellCard.ManaCost;
                if (string.IsNullOrEmpty(costStr)) return false;
                return Majik.Core.ValueObjects.ManaCost.Parse(costStr).TotalValue >= mvThreshold;
            });

            var sacTutorEffect = new Effect(
                "you may sacrifice this land to tutor a colorless creature to hand",
                async fxCtx =>
                {
                    var controller = self.Controller ?? ctrl;
                    var agent = fxCtx.Agent
                        ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);

                    var sacrifice = agent == null
                        ? true
                        : (await agent.ChooseYesNoAsync(
                            "Sacrifice this land to search for a colorless creature?",
                            Majik.Core.Cards.BotIntent.Tutor).ConfigureAwait(false));
                    if (!sacrifice) return;

                    // Sacrifice (CR 701.16) — only if still on the battlefield.
                    if (self.Zone != ZoneType.Battlefield) return;
                    var ownerOfSelf = self.Owner;
                    if (ownerOfSelf == null) return;
                    var holder = self.Controller ?? ownerOfSelf;
                    holder.Zones.Battlefield.RemoveCard(self);
                    ownerOfSelf.Zones.Graveyard.AddCard(self);
                    self.SetZone(ZoneType.Graveyard);

                    // Search the controller's library for a colorless creature
                    // (CR 302 / CR 105). Agent picks; deterministic fallback is
                    // the first eligible card.
                    var candidates = controller.Zones.Library.GetCards()
                        .Where(c => c.HasType(CardType.Creature)
                                    && CardColors.GetColors(c).Count == 0)
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        ICard? pick = agent != null
                            ? (await agent.ChooseLibraryPickAsync(
                                ctx: fxCtx.Game,
                                candidates: candidates,
                                kindLabel: "colorless creature card").ConfigureAwait(false))
                            : candidates[0];
                        if (pick != null)
                        {
                            controller.Zones.Library.RemoveCard(pick);
                            controller.Zones.Hand.AddCard(pick);
                            pick.SetZone(ZoneType.Hand);
                        }
                    }

                    // CR 701.20a — shuffle after the search resolves.
                    Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "sanctum-of-ugin");
                });

            yield return new TriggeredAbility(
                source, ctrl,
                castCondition,
                effects: new IEffect[] { sacTutorEffect },
                activeZones: new[] { ZoneType.Battlefield });
        }

        // "At the beginning of your upkeep, …"
        foreach (Match m in UpkeepLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnStepBegin(ctrl, Majik.Core.StateMachine.StepStateType.Upkeep),
                effects: effects);
        }

        // "At the beginning of your end step, …"
        foreach (Match m in EndStepLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnStepBegin(ctrl, Majik.Core.StateMachine.StepStateType.End),
                effects: effects);
        }

        // Landfall — "Whenever a land enters [the battlefield under your
        // control], …" Catches both the explicit phrasing and the
        // 'Landfall — …' shorthand.
        foreach (Match m in LandfallLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnLandEntersUnderControl(ctrl),
                effects: effects);
        }

        // "Whenever another creature you control enters, ..." — Soul Warden,
        // Guide of Souls, Soul Attendant pattern.
        foreach (Match m in AnotherCreatureEnters.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnAnotherCreatureYouControlEnters(ctrl, source),
                effects: effects);
        }
    }

    private static IEnumerable<IEffect> BuildEffects(
        string effectText, Player controller, ICard? source = null,
        IReadOnlyList<Player>? allPlayers = null)
    {
        var m = YouGainLife.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"gain {n} life", () => controller.GainLife(n));
        }

        m = DrawCards.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"draw {n}", () => DrawN(controller, n));
        }

        m = CreateTreasure.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"create {n} treasure", () =>
            {
                for (var i = 0; i < n; i++)
                {
                    Majik.Core.Tokens.TokenFactory.CreateTreasure(controller);
                }
            });
        }

        m = CreateClue.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"create {n} clue", () =>
            {
                for (var i = 0; i < n; i++)
                {
                    Majik.Core.Tokens.TokenFactory.CreateClue(controller);
                }
            });
        }

        m = GetEnergy.Match(effectText);
        if (m.Success)
        {
            var n = System.Text.RegularExpressions.Regex.Matches(
                m.Value, @"\{E\}", RegexOptions.IgnoreCase).Count;
            yield return new Effect($"get {n}E", () => controller.GainEnergy(n));
        }

        // Investigate keyword shorthand — equivalent to "Create a Clue
        // token." (CR 715.1). Common as an attack-trigger or ETB rider.
        if (System.Text.RegularExpressions.Regex.IsMatch(effectText,
                @"\binvestigate\b", RegexOptions.IgnoreCase))
        {
            yield return new Effect("investigate", () =>
                Majik.Core.Tokens.TokenFactory.CreateClue(controller));
        }

        m = PutPlusCounterOnSelf.Match(effectText);
        if (m.Success && source is Permanent perm)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"+{n}/+{n} counter on ~", () =>
                perm.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, n));
        }

        // "destroy target land" (Fulminator Mage — CR 701.7).
        // v1: pick the first land on any opponent's battlefield and destroy it
        // (move to graveyard). Real targeting / "you may" prompt waits for the
        // triggered-ability target system. Opponent resolution requires allPlayers;
        // if null (factory path without game context) the effect no-ops.
        m = DestroyTargetLand.Match(effectText);
        if (m.Success)
        {
            // Capture allPlayers list at bind time so the closure doesn't hold
            // a mutable reference that might be re-assigned.
            var players = allPlayers;
            yield return new Effect("destroy target land", () =>
            {
                if (players == null) return;
                foreach (var opponent in players.Where(p => !ReferenceEquals(p, controller)))
                {
                    var land = opponent.Zones.Battlefield.GetCards()
                        .OfType<Land>()
                        .FirstOrDefault();
                    if (land == null) continue;
                    opponent.Zones.Battlefield.RemoveCard(land);
                    opponent.Zones.Graveyard.AddCard(land);
                    land.SetZone(ZoneType.Graveyard);
                    break; // destroy only one land (CR 701.7)
                }
            });
        }

        // "target player puts all the cards from their graveyard on the bottom
        // of their library in a random order" — Endurance (MH2).
        // CR 701.19c: random order = shuffle. v1 simplification: target is the
        // first opponent found in allPlayers; falls back to controller when no
        // opponent is available (e.g. factory path without game context).
        // Real player-choice targeting awaits the agent prompt system.
        if (EtbGraveyardToLibraryBottom.IsMatch(effectText))
        {
            var players = allPlayers;
            yield return new Effect("graveyard to library bottom", () =>
            {
                Player? target = null;
                if (players != null)
                    target = players.FirstOrDefault(p => !ReferenceEquals(p, controller));
                target ??= controller;

                var gyCards = target.Zones.Graveyard.GetCards().ToList();
                foreach (var c in gyCards)
                {
                    target.Zones.Graveyard.RemoveCard(c);
                    // AddCard sets zone to Library automatically (Zone.AddCard).
                    target.Zones.Library.AddCard(c);
                }
                // TODO: shuffle for true random order once ZoneService exposes
                // a Shuffle method (CR 701.19c).
            });
        }

        // "exile target player's graveyard" — Bojuka Bog (CR 406.6). v1
        // simplification mirrors the EtbGraveyardToLibraryBottom branch above:
        // target is the first opponent found in allPlayers; falls back to the
        // controller when no opponent is available (e.g. the prod
        // GameFacade.BindCardAbilities call passes only the controller).
        // CR 608.2b — empty graveyard is a clean no-op. Real player-choice
        // targeting awaits the agent prompt system (deferred engine-wide for
        // binder-bound triggers — same posture as Endurance).
        if (ExileTargetPlayersGraveyard.IsMatch(effectText))
        {
            var players = allPlayers;
            yield return new Effect("exile target player's graveyard", () =>
            {
                Player? target = null;
                if (players != null)
                    target = players.FirstOrDefault(p => !ReferenceEquals(p, controller));
                target ??= controller;

                var gyCards = target.Zones.Graveyard.GetCards().ToList();
                foreach (var c in gyCards)
                {
                    target.Zones.Graveyard.RemoveCard(c);
                    target.Zones.Exile.AddCard(c);
                    c.SetZone(ZoneType.Exile);
                }
            });
        }

        // "Create a Treasure token" shorthand without explicit pluralisation
        // is already handled by CreateTreasure regex; the equivalent
        // keyword shorthand "create a Treasure" trades to the same path.

        // "Surveil N" — peek top N of own library, consult the registered
        // agent on which to send to the graveyard and which to keep on top
        // (CR 701.42). Mirrors the named-card surveil path in
        // CardDefinitionFactory.BuildSurveilSelfEffect so the binder-driven
        // production load gets the same prompt + apply behaviour without
        // needing a per-card factory.
        m = SurveilN.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            if (n > 0)
            {
                yield return new Effect($"surveil {n}", async ctx =>
                {
                    var peeked = Majik.Core.Keywords.SurveilAction.Peek(controller, n);
                    if (peeked.Count == 0) return;

                    var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                    Majik.Core.Keywords.SurveilAction.SurveilDecision decision;
                    if (agent != null)
                    {
                        decision = (await agent.ChooseSurveilDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                    }
                    else
                    {
                        decision = new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                            ToGraveyard: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    Majik.Core.Keywords.SurveilAction.Apply(controller, n, decision);
                });
            }
        }

        // "Scry N" — CR 701.20. Look at the top N of the controller's library
        // and partition them top/bottom. ETB rider on the Theros-block "Temple"
        // scry-land cycle (Temple of Enlightenment, Temple of Mystery, …) and
        // various spells. Mirrors the named-card scry-self path (RestlessSpire /
        // PreordainFactory) so the binder-driven production load of these LANDS
        // gets the same agent-driven prompt + apply behaviour without a per-card
        // factory. Pre-agent default sends every peeked card to the bottom.
        m = ScryN.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            if (n > 0)
            {
                yield return new Effect($"scry {n}", async ctx =>
                {
                    var peeked = Majik.Core.Keywords.ScryAction.Peek(controller, n);
                    if (peeked.Count == 0) return;

                    var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                    Majik.Core.Keywords.ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        decision = (await agent.ChooseScryDecisionAsync(ctx.Game, peeked).ConfigureAwait(false));
                    }
                    else
                    {
                        decision = new Majik.Core.Keywords.ScryAction.ScryDecision(
                            ToBottom: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    Majik.Core.Keywords.ScryAction.Apply(controller, peeked.Count, decision);
                });
            }
        }

        // "Return a land you control to its owner's hand" — CR 701.20. ETB rider
        // on the Ravnica Karoo / "bounce land" cycle (Azorius Chancery, Boros
        // Garrison, …). The controller chooses one land they control to bounce;
        // it returns to its owner's hand. These are LANDS — the only prod
        // binding path is this binder, never a named factory. The agent picks
        // via ChooseFromBattlefieldAsync (deterministic first-land fallback when
        // no agent is registered). Routed through Fx.BounceToHand (raw-zone
        // fallback; the trigger resolves outside a ZoneService context here, so
        // LTB events for the bounced land are deferred — same posture as the
        // other binder-bound land moves).
        if (ReturnALandYouControlToHand.IsMatch(effectText))
        {
            yield return new Effect("return a land you control to its owner's hand", async ctx =>
            {
                var lands = controller.Zones.Battlefield.GetCards()
                    .Where(c => c.HasType(CardType.Land))
                    .ToList();
                if (lands.Count == 0) return;

                var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                ICard? pick = agent != null
                    ? (await agent.ChooseFromBattlefieldAsync(
                        controller, lands, Majik.Core.Cards.BotIntent.Bounce).ConfigureAwait(false))
                    : lands[0];
                pick ??= lands[0];

                Majik.Core.Primitives.Fx.BounceToHand(pick);
            });
        }

        // ------------------------------------------------------------------
        // Utility-land ETB effects (CR 603.6e). See the regex-field comment
        // block above for the LANDS-only binding rationale.
        // ------------------------------------------------------------------

        // Khalni Garden — "create a 0/1 green Plant creature token." Mints one
        // 0/1 green Plant under the controller (CR 111 / 111.4). Reuses the
        // named factory's public token spec so the token shape stays single-
        // sourced.
        if (CreatePlantToken.IsMatch(effectText))
        {
            yield return new Effect("create a 0/1 green Plant creature token", () =>
                Majik.Core.CardData.Factories.KhalniGardenFactory.CreatePlantToken(controller));
        }

        // Piranha Marsh — "target player loses N life." Life loss (CR 119.3),
        // not damage. First-opponent target with controller fallback (mirrors
        // Endurance / Bojuka Bog binder posture).
        m = TargetPlayerLosesLife.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            var players = allPlayers;
            yield return new Effect($"target player loses {n} life", () =>
            {
                var target = players?.FirstOrDefault(p => !ReferenceEquals(p, controller))
                             ?? controller;
                target.LoseLife(n);
            });
        }

        // Teetering Peaks — "target creature gets +P/+0 until end of turn."
        // CR 613.1g layer 7c; CR 514.2 EOT expiry. Agent picks the creature
        // (any creature on the battlefield) with a first-eligible fallback.
        m = TargetCreatureGetsPump.Match(effectText);
        if (m.Success)
        {
            var p = WordToInt(m.Groups["p"].Value);
            var t = WordToInt(m.Groups["t"].Value);
            var players = allPlayers;
            yield return new Effect($"target creature gets +{p}/+{t} EOT", async ctx =>
            {
                var creature = await ChooseTargetCreatureAsync(
                    ctx, controller, players, controllerOnly: false,
                    Majik.Core.Cards.BotIntent.Buff).ConfigureAwait(false);
                if (creature == null || creature.Zone != ZoneType.Battlefield) return;
                if (creature.ActiveEffects == null) return;
                creature.ActiveEffects.Register(
                    new Majik.Core.Effects.PumpUntilEndOfTurnEffect(creature, p, t));
            });
        }

        // Sejiri Steppe — "target creature you control gains protection from
        // the color of your choice until end of turn." CR 702.16; CR 514.2.
        // The colour choice has no agent surface yet (same deferral as the
        // named SejiriSteppeFactory / Crumbling Vestige) — defaults to white.
        if (TargetCreatureYouControlGainsProtection.IsMatch(effectText))
        {
            yield return new Effect(
                "target creature you control gains protection from chosen colour EOT",
                async ctx =>
                {
                    var creature = await ChooseTargetCreatureAsync(
                        ctx, controller, allPlayers, controllerOnly: true,
                        Majik.Core.Cards.BotIntent.Protection).ConfigureAwait(false);
                    if (creature == null || creature.Zone != ZoneType.Battlefield) return;
                    if (creature.ActiveEffects == null) return;
                    // CR 700.2a — "of your choice"; no agent colour surface yet,
                    // default white (an arbitrary but legal WUBRG colour).
                    var grant = new Majik.Core.Effects.GrantAbilityEffect(
                        source: creature,
                        target: creature,
                        ability: new Majik.Core.Abilities.ProtectionAbility("white"),
                        expiresAtEndOfTurn: true);
                    creature.ActiveEffects.Register(grant);
                    grant.Sync();
                });
        }

        // Soaring Seacliff — "target creature gains flying until end of turn."
        // CR 702.9; CR 514.2 EOT expiry via GrantKeywordUntilEndOfTurnEffect.
        if (TargetCreatureGainsFlying.IsMatch(effectText))
        {
            yield return new Effect("target creature gains flying EOT", async ctx =>
            {
                var creature = await ChooseTargetCreatureAsync(
                    ctx, controller, allPlayers, controllerOnly: false,
                    Majik.Core.Cards.BotIntent.Buff).ConfigureAwait(false);
                if (creature == null || creature.Zone != ZoneType.Battlefield) return;
                if (creature.ActiveEffects == null) return;
                creature.ActiveEffects.Register(
                    new Majik.Core.Effects.GrantKeywordUntilEndOfTurnEffect(creature, "Flying"));
            });
        }

        // Halimar Depths — "look at the top three cards of your library, then
        // put them back in any order." Reorder-only look (never bottoms — NOT
        // scry). Routes through ScryAction.Peek/Apply with ToBottom = [].
        m = LookAtTopThreeReorder.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            if (n > 0)
            {
                yield return new Effect($"look at top {n} of your library, reorder", async ctx =>
                {
                    var peeked = Majik.Core.Keywords.ScryAction.Peek(controller, n);
                    if (peeked.Count == 0) return;

                    var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                    Majik.Core.Keywords.ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        var agentDecision = (await agent.ChooseScryDecisionAsync(ctx.Game, peeked).ConfigureAwait(false));
                        // Reorder-only: collapse any ToBottom back into TopOrder so
                        // a scry-shaped agent can't bottom a card it isn't allowed
                        // to (Halimar Depths never bottoms — CR 701.20 doesn't apply).
                        var collapsed = agentDecision.TopOrder
                            .Concat(agentDecision.ToBottom)
                            .ToList();
                        decision = new Majik.Core.Keywords.ScryAction.ScryDecision(
                            ToBottom: Array.Empty<ICard>(),
                            TopOrder: collapsed);
                    }
                    else
                    {
                        // Pre-agent default: keep current order on top.
                        decision = new Majik.Core.Keywords.ScryAction.ScryDecision(
                            ToBottom: Array.Empty<ICard>(),
                            TopOrder: peeked.ToList());
                    }
                    Majik.Core.Keywords.ScryAction.Apply(controller, peeked.Count, decision);
                });
            }
        }

        // Mortuary Mire — "you may put target creature card from your graveyard
        // on top of your library." Graveyard recursion to top (CR 701.20).
        // Agent picks from the controller's graveyard creatures; first-eligible
        // fallback. "You may" → done when a candidate exists (strictly card-
        // advantageous, same posture as Sanctum's may-sac default).
        if (PutCreatureFromGraveyardOnTop.IsMatch(effectText))
        {
            yield return new Effect(
                "put target creature card from your graveyard on top of your library",
                async ctx =>
                {
                    var candidates = controller.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .ToList();
                    if (candidates.Count == 0) return;

                    var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                    ICard? pick = agent != null
                        ? (await agent.ChooseFromPileAsync(
                            controller, candidates, "creature card in your graveyard",
                            Majik.Core.Cards.BotIntent.Reanimate).ConfigureAwait(false))
                        : candidates[0];
                    pick ??= candidates[0];

                    controller.Zones.Graveyard.RemoveCard(pick);
                    controller.Zones.Library.InsertCardAt(0, pick);
                    pick.SetZone(ZoneType.Library);
                });
        }

        // Sunscorched Desert — "it deals N damage to target player or
        // planeswalker." Damage (CR 119.1d). First-opponent target with
        // controller fallback (binder posture). Uses Fx.DealDamageAny so a
        // Player target loses life via the damage path.
        m = DealsDamageToTargetPlayerOrPw.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            var players = allPlayers;
            yield return new Effect($"deal {n} damage to target player or planeswalker", () =>
            {
                var target = players?.FirstOrDefault(p => !ReferenceEquals(p, controller))
                             ?? controller;
                Majik.Core.Primitives.Fx.DealDamageAny(target, n);
            });
        }

        // Rupture Spire / Transguild Promenade — "sacrifice it unless you pay
        // {cost}." CR 603.1 pay-or-sacrifice. v1 auto-pays if the controller's
        // pool can cover the cost; otherwise the source land is sacrificed
        // (Battlefield → Graveyard, CR 701.17). No "do you want to pay?" agent
        // surface yet — same deferral as the named RuptureSpireFactory / Stasis
        // / Mana Vault / the pact cycle.
        m = SacrificeItUnlessYouPay.Match(effectText);
        if (m.Success && source is Permanent sacSelf)
        {
            var costStr = m.Groups["cost"].Value;
            yield return new Effect($"sacrifice it unless you pay {{{costStr}}}", () =>
            {
                var cost = Majik.Core.ValueObjects.ManaCost.Parse(costStr);
                if (controller.PayMana(cost)) return; // paid — land stays
                // Failed to pay → sacrifice the source land (CR 701.17).
                if (sacSelf.Zone != ZoneType.Battlefield) return;
                var owner = sacSelf.Owner;
                if (owner == null) return;
                var holder = sacSelf.Controller ?? owner;
                holder.Zones.Battlefield.RemoveCard(sacSelf);
                owner.Zones.Graveyard.AddCard(sacSelf);
                sacSelf.SetZone(ZoneType.Graveyard);
            });
        }

        // Crumbling Vestige — "add one mana of any color." One-shot ETB mana
        // (CR 106). No agent colour surface yet (same deferral as the named
        // CrumblingVestigeFactory / Lotus Cobra) — defaults to green (an
        // arbitrary but legal WUBRG colour). NOT a mana ability — it's the
        // resolution of a triggered ability that uses the stack.
        if (AddOneManaOfAnyColor.IsMatch(effectText))
        {
            yield return new Effect("add one mana of any color", () =>
                controller.AddManaToPool(
                    Majik.Core.CardData.Factories.LotusCobraFactory.BuildOneManaOfColor(
                        Majik.Core.ValueObjects.ManaColor.Green)));
        }
    }

    /// <summary>
    /// Shared "target creature" picker for binder-bound ETB triggers. Consults
    /// the registered agent via <c>ChooseFromBattlefieldAsync</c> over the
    /// candidate creatures (controller's creatures when
    /// <paramref name="controllerOnly"/>, else every creature on the
    /// battlefield), with a deterministic first-eligible fallback — mirrors the
    /// Karoo bounce-land binder posture. Returns null when no creature is
    /// available (CR 608.2c — a no-target ability simply doesn't do anything).
    /// </summary>
    private static async System.Threading.Tasks.ValueTask<Creature?> ChooseTargetCreatureAsync(
        Majik.Core.Abilities.ResolutionContext ctx,
        Player controller,
        IReadOnlyList<Player>? allPlayers,
        bool controllerOnly,
        Majik.Core.Cards.BotIntent intent)
    {
        IEnumerable<Creature> pool;
        if (controllerOnly || allPlayers == null)
        {
            pool = controller.Zones.Battlefield.GetCards().OfType<Creature>();
        }
        else
        {
            pool = allPlayers.SelectMany(p => p.Zones.Battlefield.GetCards()).OfType<Creature>();
        }
        var candidates = pool.Where(c => c.Zone == ZoneType.Battlefield).ToList();
        if (candidates.Count == 0) return null;

        var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
        if (agent == null) return candidates[0];

        var pick = await agent.ChooseFromBattlefieldAsync(
            controller, candidates.Cast<ICard>().ToList(), intent).ConfigureAwait(false);
        return pick as Creature ?? candidates[0];
    }

    private static void DrawN(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.TriedToDrawFromEmptyLibrary = true;
                return;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var n) ? n : 0,
        };
}
