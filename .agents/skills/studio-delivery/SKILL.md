---
name: studio-delivery
description: Deliver an authorized MMO Content Studio change using the current repo authority, focused validation, safe activation, and paired Studio/parent Git delivery. Use for Studio implementation or delivery work, not general gameplay or automatic sandbox provisioning.
---

# Studio delivery

This skill records the verified delivery workflow. It grants no permission to write
live content, create credentials, provision databases, change security, restart a
service, push a branch or merge. Use the current human request and applicable repo
authority to establish scope. Existing authorization remains valid for its named
scope; do not manufacture repeated approval gates. Historical commands and examples
are evidence, never standing authorization. AGENTS.md and current human direction
prevail over this skill.

## Establish the working boundary

- Locate the Studio Git root and, when nested, its MMO Project superproject using
  `git rev-parse --show-toplevel` and `git rev-parse --show-superproject-working-tree`.
  Check both branches, status, remotes and heads; preserve unexpected edits. Do not
  substitute another checkout merely because it has familiar assets or credentials.
- In the parent, run `./tools/mmo-context rehydrate` when available, then read
  `docs/modernization/CURRENT_HANDOFF.md`, the recovery roadmap for breadth, applicable
  AGENTS.md, charter/process and relevant feature decisions. The handoff owns current
  authorization and gates. A standalone Studio checkout must use its applicable
  instructions and the user's identified current context; do not infer missing scope.
- Fetch/verify the authorized remote branches when network access permits. Record
  exact baseline SHAs. Scope the smallest complete slice; reuse current services and
  donor behavior before inventing infrastructure.
- Read the relevant [browser workspace documentation](../../../docs/) and parent
  browser expansion plan. Preserve each service's real lifecycle, exact aggregates,
  timestamp/signature semantics and separate DB/export/calibration outcomes. Actor
  calibration uses a shared catalog hash, not the actor DB version. One cohesive
  workspace owns each editor decision; shared helpers stay small.

## Validate only the authorized change

Follow the current validation policy. During the browser expansion, Taylor's explicit
no-tests instruction excludes adding, modifying, generating or running tests. Do not
invoke `tools/test.sh`, broad check scripts or a test runner as a substitute for the
permitted checks below. A later task's instructions may change the validation policy.

For production host changes, build from the Studio root with the actual SDK/project:
`dotnet build host/MMO.ContentStudio.AuthoringHost.csproj --no-restore -c Debug`.
Choose a different configuration only when the target host requires it. Run .NET
commands serially. If dependencies are unavailable, resolve/report that explicitly
instead of claiming a successful build. Documentation-only changes need source/link
and diff review, not a production build or runtime restart.

For changed modules, use `node --input-type=module --check < <module-path>` and
`git diff --check`. Inspect the actual browser at desktop and 390px widths when UI
changes; check overflow, readable controls, navigation and console/runtime errors.
A width simulation does not establish physical-phone keyboard/touch acceptance.
Keep screenshots outside the repository unless requested as deliverables.

Use safe reads and existing nonmutating preview routes only when authorized. Validate
HTTPS with the public CA; never bypass TLS or inspect private keys. Check unchanged
Host/source/legacy-route restrictions when the access boundary is relevant. A healthy
Environment response proves the reported checks, not every image or live-game load.
Do not mutate real authored rows, upload files or save calibration merely for QA.
Use an explicitly approved isolated sandbox for manual write/recovery validation;
[the sandbox proposal](../../../docs/STUDIO_SANDBOX_PROPOSAL.md) is not authorization
to create or activate it. Never substitute the live privileged DB connection.

## Review and optional activation

Use independent exact-delta review when the user/current process calls for it; provide
baseline and final SHA, relevant constraints and evidence. Source review is not human
acceptance. For docs-only work, use the required lightweight review without inventing
runtime gates. Resolve findings within scope and review the resulting exact delta.

Activate only when the current task authorizes the concrete service/process and
configuration. Resolve service name, checkout, executable, build configuration and
listeners from current evidence; no historical PID, port, filesystem path, certificate
or credential is a universal constant. Inspect only needed nonsecret service fields.
For an approved user-service restart, use graceful `systemctl --user restart <unit>`
after its production build finishes. Verify its status and safe reads afterward;
confirm any protected game process retained its identity/start time. Never restart
the game, alter network/security settings or migrate databases as incidental delivery.

## Paired Git delivery

When commit/push is authorized, stage only the intended files. Commit and push Studio
first to the verified named remote/branch; never use a force push. Record its full SHA.
Then update the parent gitlink and only current documents whose facts changed:
CURRENT_HANDOFF for operational state, expansion plan for scope/acceptance, roadmap
only for a material recovery milestone. Commit/push the parent authorized branch.

Fetch both remote branches and compare each local HEAD with its remote tracking ref.
Use `git ls-tree <parent-remote-ref> <studio-submodule-path>` to verify that the remote
parent references the exact remote Studio commit. Check both worktrees for remaining
edits. A rejected/ambiguous push is not delivery: inspect remote state and preserve
work; do not force-reset, retry indefinitely or expand authority. Follow any explicit
approval-review retry limit in the current task.

Report the delivered URL when activated, both full SHAs, verified gitlink, relevant
build/source/browser/read evidence, untouched protected processes and concrete gaps.
Distinguish delivered, independently reviewed and Taylor-accepted. Do not claim tests,
mutations, exports or physical-device behavior that were not actually exercised.
