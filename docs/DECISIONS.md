# Decisions

## ADR-014 — Bound physics validation and proceed toward the MVP

- v0.1.0-poc is the preserved source baseline. Follow-up work starts on codex/physics-validation, preserving the normal physics-OFF preview.
- Add non-collinear feature-enabled/disabled controls to prove append/IK excitation, and run the typed stateful service on the real supplied motion. Numerical replay remains distinct from independent physical accuracy or continuous visual signoff.
- Once these bounded checks finish, the next product ticket is MVP-01 multi-character scene/shared clock, physics OFF. Do not keep delaying scene work for unrestricted physics research. Physics UI integration has separate seek/retirement/error/visual gates.
- The complete P0 MVP scope and first scene-service acceptance criteria are fixed in MVP_SCOPE.md; later UI, background, graphics and save/restore requirements are retained.

## ADR-001 — Engine and first verification target (2026-11-18)

- Unity: 6000.3.28f1, revision 8f082d2fe476, installed locally.
- Project seeded from the installed 3D cross-platform URP template. On first import Unity upgraded URP to 17.3.0 and Test Framework to 1.6.0. Record resolved packages in manifest.json and packages-lock.json.
- First build uses Windows x64 Mono to separate basic Player verification from native MMD plugin/AOT compatibility. Windows IL2CPP support is installed but its build is not yet tested.
- UnityMMDTools is the provisional PMX experiment dependency. Final loader adoption remains gated on VMD/runtime compatibility and distribution checks. Stage 0 cannot pass until external PMX and VMD work in the built Player.

## ADR-002 — Loader candidates (documentation plus targeted source inspection)

- unity-mmd-loader: documented workflow places PMX/textures and VMD under Assets and uses importers/Timeline. This proves an editor import workflow, not arbitrary external PMX construction in Player. Source inspection and experiment still required.
  https://github.com/yohawing/unity-mmd-loader/blob/main/docs/HOW_TO_USE.md
- UnityMMDTools: documented scripted importer and VMD-to-AnimationClip conversion. Runtime solver support does not by itself prove runtime external model loading.
  https://github.com/CandidumGames/UnityMMDTools
- UnityPMXRuntimeLoader: documents LoadPMXModel(path); potential external loader reference. Current Unity/URP compatibility, animation support and physics are unverified. README states physics work was unfinished. Do not adopt without source/license inspection.
  https://github.com/hobosore/UnityPMXRuntimeLoader

Source revisions inspected on 2026-11-18:

- unity-mmd-loader 8928de9076c79df6dc1263f990725785c245d581: MmdEditorPmxLoader uses UnityEditor, Undo and AssetDatabase. This entry point is editor-only; it does not prove that other package entry points cannot support Player.
- UnityMMDTools db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4 (0.5.1): Runtime/PMX/PMXImporter.cs exposes Import(string), File.OpenRead, PMXReader.Read, and BuildUnityObjects without UnityEditor in that file. UMTResources is supplied at runtime. Prioritized for an actual Player experiment; source inspection alone is not a successful runtime test. Package restoration/Editor compilation passed.
- UnityPMXRuntimeLoader 3b97fe0a4db15311794ee30996bfacaf6a9b07be: PMXModelLoader calls MMDModel.ImportModel(path). Its old runtime/URP compatibility remains untested; held as fallback.

## ADR-003 — Test assets and collaboration

- Gene_light.pmx and textures: original source and pinned revision/hashes are in test/models/Gene/SOURCE.json. Retain original notices; personal noncommercial local verification only for now. Do not bundle in application.
- Whiplash: local supplied VMD/audio; original rules prohibit redistribution and commercial use and require attribution for published videos. Keep outside Git/Assets/build.
- Local-model collaboration was revoked by the user. No further local-model calls or delegation. All project implementation is written and verified directly by cloud Codex.
- Historical first attempt exceeded CLI/Ollama context (47,070 requested vs 16,384 available). A shortened retry returned text but could not edit under its read-only policy. Its completion claim was rejected: missing imports/invalid APIs, backend not applied, directory creation absent and report-write failure could exit success. No local draft was applied to the project. Original unapplied output remains ignored in .local-work/ only.

## ADR-004 — GPU smoke verification

- Merely checking the configured pipeline asset is insufficient. Hidden Player windows did not initialize a pipeline instance through the ordinary display loop during the initial probe.
- Use URP's supported RenderPipeline.StandardRequest to render a camera into a temporary RenderTexture, read back a PNG, verify nonempty pixels/no magenta fallback, and require an actual pipeline instance. This passed on Direct3D12/RTX 4090.
- Baseline first attempts are recorded as failures, followed by the corrected probe passing. No graphics negative control must fail explicitly.
- Removed template-only payment, analytics, multiplayer, 2D, navigation, collaboration, Rider and visual-scripting package dependencies. Keep only packages needed for the current scope plus Unity runtime modules.

## ADR-005 — External PMX experiment result

- Pinned UnityMMDTools 0.5.1 compiled and built for Windows x64 Mono. PMXImporter.Import(filePath) successfully constructed Gene from test/models/Gene outside Assets in the built EXE. All ten referenced textures loaded and a GPU-rendered PNG was inspected.
- Preserve the package's URP Unlit fallback shader/variants in GraphicsSettings at build time because runtime-only models cannot provide build-time material references. This is a compatibility baseline, not final toon shading.
- Live physics is disabled. The native Windows DLL is present in the Player; active Bullet physics behavior has not been tested.
- Copy package LICENSE.md, Third Party Notices.md, and bundled dictionary COPYING/AUTHORS into build ThirdPartyNotices/UnityMMDTools. These cover package MIT and declared MIT/zlib/NAIST dependencies; a release-wide distribution audit remains pending.
- No package source modifications were necessary for the successful PMX experiment. Keep the dependency pinned in manifest and lock file. Package source is not duplicated into Assets.

## ADR-006 — VMD diagnostic and reference-first validation

- The diagnostic VMD consumer evaluates UMT's non-baked curve data and invokes the existing UMT constraint/IK solver. It does not implement a new VMD parser or physics engine.
- Pinned UMT 0.5.1 sparse Euler values are radians, as shown by BuildSparseBoneKeyframes and its angular unwrap. Convert to degrees when calling Quaternion.Euler. The initial consumer missed this; fixed after visual review and independently checked against source quaternion keys. No third-party package edits were made.
- Official UMTDemo instead uses baked IK + normalized/continuity-corrected quaternion curves. User requested comparing existing players before further custom expansion; prioritize this official path and independent Babylon/saba interpolation checks. Detailed source review: VMD_REFERENCE_REVIEW.md.
- Current real-time and static-pose checks passing do not establish full interpolation, morph appearance, model compatibility or physics accuracy. Do not mark the full stage-0 gate complete.

## ADR-007 — Official baked path as provisional preview baseline

- UMTDemo's actual unmodified VMDModelPlayableBehaviour is included under Assets/MMDStudio/ThirdParty/UMTDemo, pinned with SHA-256 and MIT notices. Only its model evaluator is exercised; camera/timeline types in the same original file are not validated features. The source and license are preserved; build output contains its notices.
- An explicit baked-reference mode converts using bakeIKToFK=true and bakePhysicsToFK=false, then evaluates the official behaviour without a second live constraint/IK solve. Start-MmdPoc.ps1 now selects this mode by default. The diagnostic CLI keeps its sparse default for backwards-compatible regression checks.
- Measured sparse/baked differences exceed diagnostic tolerances, so the paths are not declared equivalent. Reference playback is preferred provisionally for a known implementation contract, not because full correctness or superior fractional interpolation has been proved. See TASKS.md and artifacts/vmd-comparison.md.
- Three fresh-process single-run conversion measurements were collected. Report retained Unity allocations as snapshots only; the Mono process-peak API returned zero and is explicitly unavailable.
- The independent Python oracle parses VMD2 bone records directly and uses Bezier inversion + shortest-path quaternion Slerp. It checks selected FK channels and reports a preceding-key-control alternative, since inspected Babylon/Saba revisions differ in this detail. It does not run either upstream engine or validate entire solved poses.

## ADR-008 — Correct the oracle; prefer verified direct quaternion preview

- Oracle v1 incorrectly treated first-block byte 3 as rotation x1. Babylon's actual loader/parser documents that bytes 2/3 may be physics markers, and reads the intact rotation block at offsets 48/52/56/60. Oracle v2 follows this source; the old ~0.6-degree claim is withdrawn. UMT's repeated-block reading did not require a patch.
- The unmodified UMT bake separately drops non-Rotatable constrained-bone rotation channels. Record this as a pinned-package compatibility limit rather than modifying reference source or forcing the runtime to match an incomplete pose.
- Direct mode evaluates existing UMT-parsed keys using per-axis Bezier weighting and Mathematics math.slerp, retains UMT morph/IK handling and explicitly solves the runtime pose once. This removes Euler approximation and preserves the checked Gene shoulder constraint.
- Preview defaults to direct based on 1,310 selected-FK numeric checks over the full duration plus targeted constraint/seek/render verification. Full MMD-engine parity, all constraint variants, all morphs and generic model IK policy are still unverified. Other modes remain as diagnostics.
- Import with createAvatar=false: no Humanoid retargeting is required for VMD-to-MMD skeleton playback. This avoids unnecessary cloned bones on the package's unsuccessful avatar-mapping path.

## ADR-009 — Runtime picker and transactional probe workspace

- Use Windows GetOpenFileNameW in the Windows Player with a correctly sized Unicode OPENFILENAME structure and unmanaged filename buffer. Preserve working directory and distinguish cancellation from native errors. No editor-only file dialog API enters the Player.
- Keep the active model/motion until a candidate has loaded successfully. Restore camera and prior pose/playback on controlled failures; pause during native selection and skip the interrupted frame's elapsed time on resume.
- Track imported meshes, materials, textures and avatar in a per-model lifetime component. Release these and the root on replacement/clear; unload unused resources after failure. Controlled Player checks verify cleanup, while exhaustive malformed-file/native-converter lifetime behavior remains open.
- UI Toolkit is used for the prototype workspace. Loading feedback renders before synchronous conversion; asynchronous parsing/conversion and production layout remain future work. Original rest rotations and PMX IK flags must be used for motion reassignment rather than current animated state.

## ADR-010 — Camera reference reuse with explicit limits

- Reuse pinned UMT ConvertCamera and the already licensed unmodified UMTDemo evaluator at 60 Hz. Read source frame times only to hold one-frame camera cuts; fractional curve evaluation otherwise smears the final half-frame before a cut. Verified this correction against independently read raw camera records.
- Reject projection-off and positive-distance keys because the converter omits projection curves and discards distance sign. Do not silently claim support. Disclose UMT's merged XYZ movement interpolation and 60 Hz approximation; the tested Whiplash camera happens to use equal XYZ controls.
- Camera sampling follows the character playback clock, clamps at the camera track end and supports seek/pause/stop through that clock. Detach restores the prior camera; model replacement retires the camera track. Camera-only standalone playback and automatic model/camera alignment are not implemented.
- Numeric checks cover raw camera target/distance/FOV and a shortest-Euler rotation policy. They do not establish parity with an executed external engine or every VMD convention. The observed 15-second framing clips Gene partly; preserve authored camera data and report this compatibility limit.

## ADR-011 — Complete expression ownership and explicit morph limits

- Each probe clip owns the full expression pose: zero imported renderer blendshape weights before sampling that clip, including baked-reference. A bone-only replacement must not retain a previous motion's blink/mouth values. Future manual-expression blending needs an explicit compositing policy.
- Validate expression weights, baked mesh changes and actual GPU renders together; binding counts alone cannot prove visible deformation. The controlled Gene fixture verifies two vertex morphs and one reachable leg IK chain with source toggles. Do not generalize this to nested groups, unsupported morph types, all angle limits or other models.
- Report non-Vertex/non-Group morph types as unsupported in the existing model warning area. The pinned converter supports vertex curves and direct Group-to-Vertex offsets; it does not animate the model's material/UV morphs through this path. Dedicated group testing remains open.

## ADR-012 — Separate backends and deterministic physics experiment

- Preserve separate Mono/IL2CPP Player and evidence directories. Verify compiled backend in runtime reports and restore the project backend after building. Optional unsupported metrics must not prevent loading; process peak memory is unavailable on IL2CPP.
- Use UMT's public fixed-step SolveWithPhysics only in an explicit diagnostic entry point. Leave UI playback physics OFF until full-motion/reference behavior and seek cost are reviewed.
- Reset/preroll requires ResetToBindPose, which recreates native contexts. ResetPhysics alone failed the controlled Gene replay test. Full reset plus frame-0 seeding and fixed preroll repeats within the checked threshold for Gene/Sandrone in each backend; cross-backend physical-pose parity is not claimed.
- Native PE/export/notice inventory and same-PC runs are partial distribution evidence. MSVC runtime dependencies and clean-machine deployment remain release requirements.

## ADR-013 — Separate reproducibility from physical accuracy

- Full-duration diagnostics compare every replay pose and collect selected GPU checkpoints. Also report directly weighted bones separately from helper bones; a helper's lack of vertex weights does not establish harmlessness.
- Controlled comparisons use the actual pinned official physics converter and unchanged demo evaluator with matching frame rate, warmup and seed. Execution success and numeric agreement are separate report fields. Current agreement fails and must not be promoted to a passed physics gate.
- Preserve the validated interactive physics-OFF behavior while isolating the frame-2 discrepancy. An adapter pose-preservation change that did not improve measured agreement was reverted rather than retained as an assumed fix.
