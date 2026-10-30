# Instructions for every AI agent working on Tamaneen (Codex, Claude, or any other)

These rules let several AI assistants work on this project one after another without losing work or breaking each other's changes. Follow them exactly.

## 1. Before you start any task

1. Work on the **integration branch** `claude/brave-bardeen-3nott4`. The repository's default branch belongs to a different project, so always use this branch.
2. Get the latest state (`git fetch` and `git pull`), then read, in this order:
   1. `docs/HANDOFF.md`: what the project is, how to run it, the 10 code rules (§5), status, roadmap.
   2. `docs/PROGRESS.md`: **the shared task board and work log**. This is the memory between agents.
3. Pick the task the owner asked for. If the owner said "continue", take the first `todo` task in `docs/PROGRESS.md`.
   - Check the board first: if the task is `in-progress` by another agent, do not touch it.
   - Ask the owner when it is unclear.

## 2. While you work

1. **Claim the task first.** Create your task branch from the integration branch, with this name:
   - `codex/<task-id>-<short-name>` for Codex, e.g. `codex/T-501-continue-care`;
   - `claude/<task-id>-<short-name>` for Claude.
   On that branch, set the task to `in-progress`, with the agent name, the date and the branch.
   Then make the claim visible to every agent: push the branch and open a draft pull request into the integration branch straight away.
2. Follow every rule in `docs/HANDOFF.md` §5:
   - Arabic and English text for everything a user sees;
   - row-level security, plus an integration test, for any patient data;
   - patient text goes through the crisis gate;
   - encryption;
   - audit;
   - If-Match on shared records;
   - INotifier with catalog texts in both languages;
   - `docs/api-v1.md` and both TypeScript type files updated.
3. Use the design: the screen's spec object in `docs/design/spec/g*.json`, its mockup in `docs/design/mockups/`, and `docs/design/spec/guide.md`. Do not invent features.
4. Write tests together with the code.

## 3. Before you finish

1. Run the checks in `docs/HANDOFF.md` §4 that your environment supports. Say exactly which ones ran, and the counts.
   - GitHub Actions (`.github/workflows/tamaneen-ci.yml`) runs the full backend, integration and frontend suites on every pull request.
   - A pull request may be merged only when that check is green.
2. Update `docs/PROGRESS.md` **in the same pull request**:
   - set the task to `done`, or `blocked` with the reason;
   - add one entry to the work log at the top: date, agent, task id, branch or PR, what changed, the test results, and anything the next agent must know;
   - add new tasks you discovered, with the next free id.
3. Open a pull request **into `claude/brave-bardeen-3nott4`**. Keep commits small and described.
   - Never force-push the integration branch.
   - Never rewrite its history.

## 4. Never

- Never commit secrets, real patient data, or keys other than the dev keys already documented.
- Never disable or skip a test to make CI green. Fix the cause.
- Never weaken a safety rule:
  - the crisis gate;
  - row-level security;
  - audit;
  - the Production startup guards.
- Never leave `docs/PROGRESS.md` out of date. The next agent depends on it.
