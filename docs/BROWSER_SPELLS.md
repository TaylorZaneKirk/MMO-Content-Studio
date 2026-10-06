# Browser Spells

`/studio/spells.html` authors all 67 fields in MagicSpellDraft using the existing
MagicSpellAuthoringService. Combat identity/tier/element/requirements/shard cost,
damage and exact integer-tenths cast XP are included, along with cast/target modes,
Air force/displacement and all six mastery fields, Earth manifestation/capacity/
lifetime/weight, Fire ignition/burning/damage/cooldown and Water manifestation/slicks.
Every type-specific group remains visible and retains values when the effect changes.
Explicit clear actions remove only their named group. Blank nullable values remain
null and numeric zero remains zero; no desktop-style hidden-field cleanup occurs.

Presentation covers the icon, matter texture/scale, ordered projectile/impact/splash/
burning/slick frames and every FPS/scale field, source facing, rotation, homing toggle/
strength and cast/impact/splash sound references. Missing canonical references remain
editable. Frame add/remove/move actions preserve order and null/empty distinctions.
XP displays a decimal derived with integer arithmetic while storing integer tenths.

## Lifecycle

One Spells workspace owner handles selection, complete draft, preview/apply and
recovery. Existing service DTOs, timestamps, validation and signatures remain the
authority. Save Draft, Save & Publish (including new definitions), Publish, Disable
and Delete are present. Saved-only actions require clean drafts; Delete requires
Disabled. Preview diffs cover all scalar and array fields. Apply uses its immutable
reviewed payload. Busy/dirty navigation and stale-response guards retain local edits.
Transport/server/version/missing-target mutation failures require explicit reload/
compare, with use-server or keep-local-on-current-version choices.

The current Spell service requests no catalog export for any action. Browser results
state database commit, no requested export and no game restart separately. Database
unavailable errors return 503; thrown postcommit verification errors reach the common
500 uncertain-outcome boundary. No gameplay, repository or service lifecycle rules
were changed by this browser slice.

## Confined media and playback

Existing media is selected by searchable canonical res://assets/ identities. No new
upload/write route exists. Spell media resolves only beneath the configured asset
root, rejects traversal, foreign paths and linked ancestry/descendants, and bounds
PNG to 16 MiB and audio to 32 MiB. Responses validate and serve one bounded byte
snapshot. PNG uses the existing full validator; WAV/OGG/MP3 format headers are checked
and browser decoding reports unavailable/unsupported codec data. Browser responses
never provide host roots. Audio supports byte ranges and native playback controls.

The preview canvas supports phase/frame selection, play/pause, inspection zoom and
travel-direction rotation using source artwork facing. Impact/Splash play once and
can replay; projectile/hazards loop. Authored scale and inspection zoom are distinct.
Homing settings are shown; this is source artwork inspection, not a trajectory or
combat/manifestation simulator. Local preview controls do not dirty the definition.
Edits clear the snapshot, cancel playback and remove audio sources. Async successes
cannot restore stale pixels; missing frames in the current snapshot/phase stop
playback and report their index even if loading outlasts a frame interval. Images
are cached as bounded in-flight promises to avoid repeated concurrent decodes.

## Validation and remaining acceptance

The production host built with zero warnings/errors. JavaScript module syntax and
whitespace checks passed. No tests were added, changed, generated or run. Independent
source review found two playback defects, fixed before delivery; final exact-commit
review is recorded in the parent handoff.

CA-valid reads covered Spells routes, options, image/audio catalogs and all prior
workspace pages. All eight saved spells were previewed nonmutatingly for all five
actions: current Published definitions passed supported operations and Delete was
correctly rejected. All saved definitions remained identical. All 76 unique authored
images returned 200. Three existing WAV resources returned 200; Chrome decoded a
WAV selected only into an unsaved local draft. OGG/MP3 decoding has not been exercised
because the inspected sound selections were WAV. Wrong Host, excluded source and
legacy LAN API stayed denied; image/audio traversal and host-path requests were 404.

Actual Chrome desktop and 390px inspection covered combat, Air/Earth/Fire/Water
forms, exact XP, projectile/hazard preview and native audio. No page overflow or
runtime exceptions were observed. Playback kept Saved state; a local sound edit
became Unsaved. Screenshots are retained in `/tmp/studio-spells-visual`.

Only the approved Studio service restarted with unchanged configuration. No game
restart, live authored/calibration QA write, network/security change or merge.
Physical-phone, live mutations and runtime content loading remain acceptance gaps;
this service has no export operation to exercise. Stop after Spells, before Environment.
