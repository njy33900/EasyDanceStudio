# Controlled deformation support

This records POC-06B coverage for pinned UMT db35d9cb80ad and the external Gene model. Counts and bindings do not imply whole-engine MMD parity.

| Channel | Current path | Verified coverage |
| --- | --- | --- |
| Vertex morph | Unity blendshape weights | Gene まばたき / あ: seven times each, 0..100 scaling, fractional linear interpolation, actual mesh and GPU image changes |
| Group morph | Converter handles direct references to Vertex morphs | Source inspected; dedicated playback acceptance pending. Nested/non-Vertex groups not claimed |
| Material / UV / Bone / Flip / Impulse morph | Not implemented by this VMD playback path | Import warns when such types are present; no visual support claimed |
| IK toggles | Stepped VMD on/off values applied before UMT solve | Controlled 左足ＩＫ on/off/on; target distance reduction and deterministic seek |
| General IK / angle limits | Existing UMT runtime solver | Generic chain/model correctness pending; finite left-knee/leg rotations are a limited check |
| Unaddressed IK defaults | Existing experimental direct-mode policy | Gene/Whiplash retains only addressed controllers; original model defaults available in direct-default-ik. This is not a generic compatibility policy |
| Live physics | Disabled in normal UI playback | POC-06C adds controlled fixed-step/reset-preroll diagnostics; full physics accuracy remains unverified. See PLAYER_PHYSICS_VALIDATION.md |

The active motion owns the complete expression pose: reset imported renderer weights to zero, then evaluate that clip's channels. This prevents previous clips leaving an expression on unaddressed channels. Manual expressions/blending are not implemented; a future compositing policy must explicitly replace this rule.

The probe creates small external VMD2 fixtures with fixed CP932 bytes for the known Gene names, identity head rotations, linear expression ramps and a left-foot target displaced by (+0.08,+0.04,0) Unity meters. The target is reachable. Both ON and OFF use the same target; only the toggle changes, so the distance comparison isolates IK. Original source files remain unchanged.

Evidence lives in artifacts/poc-deformation.json, poc-deformation.log, deform-neutral.png, deform-active.png, deform-ik-on.png and deform-ik-off.png. Diagnostic files are excluded from Git and Player assets. See TASKS.md and TEST_PLAN.md for run results and commands.
