# Contributing to Majik (engine + server)

Thanks for your interest. This repo holds the C# / .NET 10 rules engine, server, bot, and console tooling that powers [majik.tech](https://majik.tech).

## Before you start

- Read [`README.md`](./README.md) for the project layout and common commands.
- Read [`CLAUDE.md`](./CLAUDE.md) for architecture (state machines, event bus, stack/priority/SBAs, abilities/effects, keyword pipeline).
- Game-logic decisions cite [`MagicCompRules 20251114.txt`](./MagicCompRules%2020251114.txt) (the 2025-11-14 Comp Rules) and reference rule numbers (e.g. `Rule 704.5j`). [`RULES_REFERENCE.md`](./RULES_REFERENCE.md) indexes the most-touched rules.

## Development setup

```bash
# 1. Mongo (profiles, decks, matches)
docker compose -f docker-compose.dev.yml up -d

# 2. Build + test
dotnet build Majik.sln
dotnet test  Majik.sln

# 3. Server (REST + SignalR + OpenAPI on :5057)
dotnet run --project Majik.Server
```

The Modern card pool is embedded at `Majik.Core/CardData/Embedded/modern-cards.json.gz`; no seed step required.

## Adding card behaviour

Two paths, in order of preference:

1. **Spell template binder.** Check `Majik.Core/CardData/SpellTemplates/` first — many vanilla spells ("deal N damage to any target", etc.) are covered by oracle-text regex binders. No new code needed.
2. **Named factory.** For unique behaviour, add a class under `Majik.Core/CardData/Factories/` with `[CardName("Card Name")]`. `Majik.Core.SourceGen.NamedCardFactoryGenerator` wires it into the dispatch table at build time.

After adding a factory, regenerate the embedded seed so `IsImplemented` flips on:

```bash
dotnet run --project Majik.Console -- export-modern-cards <path-to-scryfall-all-cards.json>
git add Majik.Core/CardData/Embedded/modern-cards.json.gz
```

## Tests

xUnit + FluentAssertions + Moq. ~5,975 tests across `Majik.Core.Tests`, `Majik.Core.Api.Tests`, `Majik.Server.Tests`, `Majik.Bot.Tests`.

```bash
dotnet test Majik.sln
dotnet test --filter "FullyQualifiedName~StateBasedActionsTests.LegendRule"
```

Rules code without a corresponding test in `Majik.Core.Tests/Rules/` will not be merged. Shared fixtures: `TestDataBuilder.cs` + `TestEventBus.cs` — use these instead of hand-rolling setup.

## PR conventions

- Branch from `main`.
- Conventional Commits style for titles (`feat:`, `fix:`, `chore:`, `feat(card):`, `feat(rules):`, etc.).
- Cite rule numbers in code comments and PR descriptions for rules-engine changes.
- Keep PRs small and focused — one card / one rule / one bug per PR is ideal.
- CI must be green before merge.

## Sign your commits (DCO)

This project uses the [Developer Certificate of Origin](https://developercertificate.org/) (DCO). Every commit must carry a `Signed-off-by` trailer asserting you have the right to submit the change under the project's Apache-2.0 license.

```bash
git commit -s -m "your message"      # the -s adds the trailer
```

The trailer looks like:

```
Signed-off-by: Your Name <your.email@example.com>
```

If you forget, amend the last commit:

```bash
git commit --amend -s --no-edit
```

For a branch with several commits, rebase and sign all of them:

```bash
git rebase --signoff main
```

By signing off you confirm:

- The contribution was created in whole or part by you, **or**
- You have permission to submit it under the open-source license indicated, **or**
- The contribution was provided to you by someone who certified one of the above, and you have not modified it.

## Licensing of contributions

Per Apache-2.0 §5, any contribution you submit is licensed under the same Apache-2.0 terms as the rest of the project. You retain copyright in your work; you grant the project the rights set out in the license. No separate CLA.

## Magic: The Gathering Fan Content

This engine exists under the [Wizards Fan Content Policy](https://company.wizards.com/en/legal/fancontentpolicy). Contributions must not push the project outside that policy:

- No monetisation, ads, paywalls, or commercial branding in the server or APIs.
- Bundled card metadata (Scryfall) and the comp rules text are Wizards' property — never re-license, and don't replace the Fan Content disclaimer in [`NOTICE`](./NOTICE).
- Don't add Wizards artwork to the repo.

## Reporting issues

Use GitHub issues on this repo for engine, server, bot, and card bugs. UI bugs go to [`majik.portal`](https://github.com/bg9m9r/majik.portal).
