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
    private static readonly Regex DealDamageOpponent = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+damage\s+to\s+(that\s+player|any\s+opponent)",
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

        // "At the beginning of your upkeep, …"
        foreach (Match m in UpkeepLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnStepBegin(ctrl, Majik.Core.StateMachine.PhaseStateType.Upkeep),
                effects: effects);
        }

        // "At the beginning of your end step, …"
        foreach (Match m in EndStepLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl, source).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnStepBegin(ctrl, Majik.Core.StateMachine.PhaseStateType.End),
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
