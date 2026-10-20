# Arachnids authoring support

Source-only addition, matching game migration123 and `content/arachnids.sql`. The existing dynamic Insight family/specimen forms and Lore lifecycle remain the sole authoring path. Mirrored validation permits Arachnids at base Insight7 with only `giant_spider`, and Spider Fang at Insight1 for20 XP/4 points. The existing shared 180-milestone preview, export, reference checks and separate draft/published storage remain unchanged.

`tests/ArachnidsRegression` verifies real load, validation,180-row preview, preview/save/reload, draft isolation and publication through the existing catalog refresh interface in a guarded disposable fixture. Game's actual Lore/Mob export proof separately verifies serialized output. No bespoke editor or runtime mechanics were added.

Host/regression builds pass with zero warnings/errors; lifecycle proof passes. Independent Astra review of9782422..4e0425d found no blocking/correctness findings. Its minor stale-family-label observation is corrected in both native and browser hints. No live SQL/publication/service changes. Taylor owns acceptance; previous live Studio9782422 is unchanged.
