# Quests in the browser

`/studio/quests.html` follows the accepted desktop/mobile workspace shell. Labeled
step and transition rows replace desktop pipe-delimited editing. The existing
Quest service, validator and repository remain the decision owners. No objectives,
rewards, Dialogue editing or gameplay changes are introduced.

Every authored field is retained: stable quest ID, display name, schema version;
step ID/name/order; transition ID/order and source/target status/nullable step ID.
Order values are loaded exactly and may be edited directly. Explicit Move up/down
reorders and renumbers that list from zero, as stated beside the controls. Remove
and rename retain references for explicit repair. Active states require a step;
Not Started/Completed require none. Changing status never silently clears a step.
Step suggestions refresh as IDs/names change; missing/unknown references remain
visible. Invalid values and over-limit collections are never truncated.

The validator owns reachability, unreachable steps/transitions, dead-end steps,
start/completion paths, persisted character-state/pending settlement protection
and published Dialogue reference checks. A GET diagnostics route runs the existing
preview service against a saved definition only; no mutation/export is invoked.
Its definition version is checked against the loaded form and stale asynchronous
results are ignored. Its signature is never installed as Apply permission. Dirty
edits mark analysis stale; Preview validates current edits. Save Draft, Publish,
Disable and Delete receive their distinct existing request DTOs and exact version/
operation-specific signature. All errors block Apply, including reference errors
that an older valid_for_draft flag may not classify as draft-blocking.

The existing domain normalizes whitespace, ID casing and explicit row order.
Preview exposes the actual normalized draft and a complete before/after payload
comparison, including schema and same-count row edits, supplementing the service's
count-only change summary. Data is never removed by the browser. There is no
Save & Publish. Save Draft changes Published to Draft and does not request export;
Publish, Disable and Delete request the existing Quest catalog export. Delete
requires Disabled state and no blocking references. DB commit and export outcome
are separate; neither implies live game restart or hot reload.

Conflicts and missing/uncertain write results retain local edits and lock another
apply until explicit reload/compare. Reload verification failure is HTTP 500 so a
possibly committed mutation cannot be blindly resubmitted. Browser database errors
omit host exception detail. Existing access/CSRF restrictions are unchanged.
Hash Back/Forward retains drafts; document departure warns about unsaved changes.

No tests are added, modified, generated or run. Delivery uses production build,
JS syntax, independent source review, actual Chrome desktop/390px inspection and
CA-valid read-only resource/API checks. No live quests are saved/published/disabled/
deleted for QA. Mutation, concurrency and export paths are source-reviewed but not
exercised against live data; physical-phone acceptance remains separate.

Delivery inspection loaded the existing Published A Meal Delayed definition with
both steps and all three transitions intact. Saved GET diagnostics reported a start
and reachable completion, no unreachable steps/transitions or dead ends, and the
real Save Draft blockers: one completed character quest state plus published
corren_hale_dialogue/harlan_wick_dialogue references. No restrictions were changed.
Chrome desktop/390px inspection observed no overflow or runtime exceptions. A local
unsaved step addition marked the draft dirty and analysis stale; it was never sent.
Screenshots are under /tmp/studio-quests-visual; selected saved-data views are in
Library. Mutation and export behavior was not exercised.
