# MAKO Command Log

This is an append-only engineering log for meaningful MAKO work sessions. It
records the goal, user-visible changes, important build/test commands, and their
results. Routine inspection commands are intentionally omitted so the log stays
useful.

## 2026-07-10 — Physics2D foundation

Goal: begin a rendering-independent 2D rigid-body engine before attempting 3D
or soft-body physics.

Changes:

- Added fixed-step worlds and static, kinematic, and dynamic bodies.
- Added circle/box collision, gravity, forces, impulses, bounce, and friction.
- Added MAKO bindings, documentation, a headless test, and a visual sandbox.

Verification:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
```

Result: release build succeeded with zero warnings/errors; 14 tests passed.

## 2026-07-10 — Angular rigid bodies

Goal: make boxes rotate and respond to off-center collisions.

Changes:

- Added rotation, angular velocity, torque, inertia, and rotation locking.
- Added oriented-box SAT collision and contact-point impulses.
- Added `Mako2D.rect_rot` and `rect_rot_lines` rendering helpers.
- Added a crooked-box tower and rotated ramp to the sandbox.

Verification: release build succeeded; 14 tests passed.

## 2026-07-10 — Stability and spring joints

Goal: address drifting stacks and clipping, then establish the foundation for a
slime game.

Changes:

- Added two-point box manifolds, internal/adaptive substeps, damping, and sleep.
- Added a regression for a 3,000 px/s circle hitting a 2 px wall.
- Added anchored damped spring joints with runtime inspection and tuning.
- Built the first particle-and-spring slime rig in the sandbox.

Verification: release build succeeded; 14 tests passed, including spring
convergence, stack stability, wake/sleep, and fast-body collision.

## 2026-07-10 — Easy slime API

Goal: apply MAKO's rule: if an API is hard to type, it is hard to understand and
must be made easier.

Changes:

- Added one-call `Physics2D.slime(...)` creation.
- Added `slime_move`, grounded `slime_jump`, `slime_info`, and cleanup.
- Hid particle mass distribution, spring topology, rest-length calculations,
  contact aggregation, and force distribution behind the high-level object.
- Replaced the sandbox's manual spring construction with the easy API.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt tests/physics2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt examples/physics_2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
jq empty src/Mako/registry.json
git diff --check
```

Result: release build succeeded with zero warnings/errors; formatter checks
passed; all 14 test suites passed; registry JSON and diff validation passed.

## 2026-07-10 — Game-feel slime controller

Goal: turn the working slime simulation into a controller suitable for a small
platform game without making ordinary MAKO scripts more complicated.

Changes:

- Added acceleration toward a configured maximum speed.
- Added ground traction and reduced, configurable air control.
- Added coyote time and buffered jump input behind `slime_jump`.
- Added `slime_hold_jump` for variable jump height and early-release cutoff.
- Added deformation/controller state to `slime_info`.
- Turned the sandbox into a small ramp-and-platform obstacle course.
- Added regressions for coyote time, jump buffering, variable jump height, and
  sustained movement speed limits.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt tests/physics2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt examples/physics_2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
jq empty src/Mako/registry.json
git diff --check
```

Result: release build succeeded with zero warnings/errors; formatter checks
passed; all 14 test suites passed; registry JSON and diff validation passed.

## 2026-07-10 — Slime topology fix and runtime command panel

Goal: fix the escaped-particle/torn-outline behavior visible on thin platforms
and use the open HUD space for an embedded MakoUI command log.

Changes:

- Disabled collisions between particles owned by the same slime.
- Capped spring velocity injection per simulation substep.
- Added hard per-spring stretch limits for high-level slimes.
- Added regression checks that settled slime nodes remain above the floor and
  within the configured shape envelope.
- Added `MakoUI.wants_keyboard()` for safe embedded text input.
- Added an in-game MakoUI command/event panel to the Physics2D sandbox.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt tests/physics2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt examples/physics_2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
timeout 5s dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- examples/physics_2d.mko
jq empty src/Mako/registry.json
git diff --check
```

Result: release build succeeded with zero warnings/errors; all 14 test suites
passed; the embedded MakoUI sandbox completed a five-second runtime smoke test
without errors; registry JSON and diff validation passed.

## 2026-07-10 — Continuous slime perimeter and platformer start

Goal: fix platforms entering through the space between slime nodes, then begin
the real slime platformer with mandatory googly eyes.

Changes:

- Derived default hidden collider size from point count, outer radius, and
  maximum spring stretch so adjacent colliders always overlap.
- Increased the default slime outline to 14 points while preserving the outer
  size requested by game code.
- Added `slime_set_position` / `slime_reset` for checkpoints and fall recovery.
- Added perimeter-continuity and full-rig reset regressions.
- Added velocity-reactive googly eyes to the sandbox.
- Created `examples/slime_platformer.mko` with a one-screen level, gaps, raised
  platforms, collectibles, goal state, reset/death tracking, and MakoUI HUD.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt tests/physics2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt examples/physics_2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt examples/slime_platformer.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
timeout 5s dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- examples/slime_platformer.mko
jq empty src/Mako/registry.json
git diff --check
```

Result: release build succeeded with zero warnings/errors; all 14 test suites
passed; formatter, registry, and diff checks passed; the platformer completed
live runtime and visual smoke tests with its MakoUI HUD and googly eyes visible.

## 2026-07-10 — Anti-pancake slime volume

Goal: prevent a valid spring configuration from leaving the slime permanently
flattened across a platform.

Changes:

- Stored the initial polygon area for every high-level slime.
- Added a bounded two-pass area constraint inside each simulation substep.
- Added the easy `shape_recovery` option with a stable default.
- Exposed `area` and `area_ratio` through `slime_info`.
- Added regressions for minimum resting area and readable blob height.
- Visually verified that the platformer slime now recovers as a blob while
  retaining its velocity-reactive googly eyes.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt tests/physics2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt examples/physics_2d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt examples/slime_platformer.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
timeout 5s dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- examples/slime_platformer.mko
jq empty src/Mako/registry.json
git diff --check
```

Result: release build succeeded with zero warnings/errors; all 14 test suites
passed; formatter, registry, and diff checks passed; release runtime and visual
smoke tests confirmed that the slime recovers its blob silhouette after landing.

## 2026-07-10 — Foundry foundation and first real export

Goal: establish MAKO's MakoUI-based game builder and produce the first game
artifact through a reusable backend.

Changes:

- Added Foundry's project model, `foundry.json`, validation, target registry,
  staged output, build logging, and artifact metadata.
- Added `mko foundry [project]` GUI and `--term` status output.
- Added `mko build [project] --target ... --output ...` for direct and CI builds.
- Added ready/planned/later target states for Linux, Windows, AppImage, Android,
  macOS, Web, VR, and consoles.
- Implemented self-contained Linux x64 folder exports.
- Built the slime platformer, verified only `main.mko` was bundled for an
  explicit single-script project, and launched the exported artifact itself.
- Added complete Foundry documentation and CLI help.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- foundry examples/slime_platformer.mko --term
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- build examples/slime_platformer.mko --target linux-x64 --output /tmp/mako-foundry-test
timeout 5s /tmp/mako-foundry-test/slime-platformer-linux-x64/slime-platformer
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
jq empty src/Mako/registry.json
git diff --check
```

Result: release build succeeded with zero warnings/errors; all 14 test suites
passed; Foundry detected every target state; the Linux exporter produced a
self-contained artifact containing only `main.mko` plus runtime payload; build
metadata validated; and the exported launcher completed a live smoke test.

## 2026-07-10 — Physics3D engine redesign

Goal: replace the inherited prototype architecture with a readable, scalable,
game-facing Physics3D core while retaining compatibility with older scripts.

Changes:

- Replaced all-pairs narrow-phase work with sweep-and-prune broad-phase bounds.
- Replaced seven-point capsule/box sampling with a continuous closest-segment solve.
- Added easy `moving_box`, `moving_sphere`, and `moving_capsule` constructors.
- Added runtime `gravity`, lightweight `position`/`velocity`/`transform` reads,
  `overlap_sphere`, and nearest-hit `raycast` queries.
- Made characters inherit moving-platform velocity and report `ground_body`.
- Kept adaptive fast-body/character subdivision and correct rotated inertia.
- Expanded collision, query, moving-platform, and easy-API regressions.
- Updated the interactive Physics3D demo with a moving platform and rewrote the
  Physics3D reference around the redesigned API and solver guarantees.

## 2026-07-10 — Physics3D gameplay systems

Goal: add readable collision rules and joints that can drive real game logic
without exposing bitmask math or low-level solver objects to MAKO scripts.

Changes:

- Added named body layers with `layer` and reversible `ignore_layer` rules.
- Added non-solid trigger volumes, `is_triggered`, body trigger state, and
  per-character trigger contact lists.
- Added optional named-layer filtering to `raycast` and `overlap_sphere`.
- Added center-to-center 3D spring joints with automatic rest-length capture,
  runtime tuning, inspection, counting, cleanup, and body-removal safety.
- Added a dedicated Physics3D gameplay regression suite.
- Updated the interactive demo with a hanging spring and visible trigger zone.
- Updated both the repository reference and landing-page documentation.

## 2026-07-10 — Physics3D demo finish pass

- Added the 4096×3072 `Images/Skybox.png` cubemap to the main Physics3D demo.
- Made skybox paths resolve relative to the running script and installed the
  shared Images folder alongside global examples.
- Added eight-second launched-projectile lifetimes in the main demo and
  twelve-second spawned-body lifetimes in the sandbox, retaining depth cleanup.
- Added `MakoUI.preview` as a real clipped ImGui image widget and replaced the
  sandbox's raw overlay plus fake blank-line layout.
- Added a rotated end-contact regression proving capsules collide along their
  complete segment rather than as a sphere at the body center.
- Fixed sideways capsule/plane manifolds to retain both equal-height endpoint
  contacts, preventing one-ended rocking and visible floor embedding.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt --check tests/physics3d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt --check tests/physics3d_collision.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt --check tests/physics3d_character.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt --check examples/physics_3d.mko
git diff --check
```

## 2026-07-10 — Physics3D readability and stability pass

Goal: turn the inherited Physics3D prototype into a MAKO-style API that is easy
to type and understand, while fixing its most important motion and collision
problems and rebuilding its examples and tests.

Changes:

- Added short dynamic constructors plus `static_box`, `static_sphere`,
  `static_capsule`, and `floor`; retained the old typed calls for compatibility.
- Replaced dictionary-first character setup with positional `character` and
  readable `character_tune` calls.
- Added `material` so bounce and friction no longer require a long constructor.
- Corrected angular physics by rotating the inverse inertia tensor between local
  and world space for torque and contact impulses.
- Added adaptive rigid-body substeps and subdivided character movement to stop
  fast objects from crossing thin geometry.
- Corrected character ground snapping and stale contacts after body removal.
- Rewrote all three Physics3D suites and both interactive examples around the
  easy API, including fast-projectile and fast-character clipping regressions.
- Added a dedicated Physics3D guide and linked it from the README.

Verification commands:

```bash
dotnet build src/Mako/Mako.csproj -c Release --nologo
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- test tests
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt --check tests/physics3d.mko
dotnet run --project src/Mako/Mako.csproj -c Release --no-build -- fmt --check examples/physics_3d.mko
git diff --check
```
