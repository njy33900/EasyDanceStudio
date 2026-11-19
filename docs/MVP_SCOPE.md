# v1.0 MVP scope and next implementation ticket

2026-11-19. Follows specification sections 3.1, 8.1/8.2 and 12. This fixes the implementation target; it does not declare the MVP complete.

## Baseline decisions

- Continue with Unity 6000.3.28f1 / URP and pinned UnityMMDTools. Actual external PMX/VMD loading, minimal visible morph/IK behavior, Mono/IL2CPP execution, native inventory/notices and documented support limits are the existing PoC foundation.
- Preserve all specification P0 MVP features: two independently placed characters with distinct motions on one clock, PMX background, viewport/object manipulation, camera/light/basic graphics controls, JSON save/restore and recoverable imports.
- Physics remains OFF by default with visible support limits. The typed stateful service is an opt-in integration candidate. Cancellable/scalable seek, model retirement/error recovery and visual/collision validation must precede a user-facing physics switch. Complete MMD physics equivalence is not a prerequisite to implementing scene management.
- Keep precise direct interpolation. Official-converter trajectory comparisons require matched inputs and state-history policies; do not reduce interpolation precision to force parity.
- PMX is the required background format. GLB, audio and other P1 items follow core P0 work. No MP4 export, editable multi-track timeline, motion blending or plugin loader in v1.
- Test content remains external to Unity Assets, Git and distribution. Same-PC native deployment checks do not replace the final clean-machine/MSVC-runtime release check.

## MVP-01 — multi-character scene service (next)

Related IDs: MMD-104 and the shared playback portion of MMD-102/MMD-111.

Implement a runtime scene registry with stable object IDs, independent imported-asset owners and one shared playback clock. Importing a second object must not release or modify the first object's resources. Per-object VMD replacement stays transactional. Sampling each registered motion from a shared time must not also permit its component's automatic clock to advance.

First validation runs in a separate actual Windows Player before expanding UI. Load two external models, assign distinct VMD fixtures, place the roots independently, and verify both advance from the same clock while producing distinct source-driven poses. Verify shared pause/seek/loop, failed import preserving both objects, removal releasing only the selected owner, and surviving-object playback after removal. Physics stays OFF for this ticket.

Exclude production UI redesign, JSON persistence, background import, physics integration and graphics controls from this first ticket. Maintain the existing single-model preview as a regression path while introducing the scene service. Later tickets connect selection/editing UI, background, graphics, save/restore and integration/performance validation in the specification's order.

## Remaining release acceptance

The scene service is only one part of v1.0. Completion still requires the specified complete EXE workflow, malformed-asset resilience, persistence round trips, sample-scene performance reporting, known-limit presentation and distribution validation. Current diagnostic success is not a substitute for those acceptance criteria.
