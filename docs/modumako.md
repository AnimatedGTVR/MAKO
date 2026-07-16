# ModuMAKO compiler

ModuMAKO combines MAKO authoring syntax with Modularity's `ModuNode`,
lifecycle, input, engine, and object facades. The compiler emits ModuCPP;
Modularity's existing toolchain then owns native compilation and the runtime
ABI.

## Responsibility boundary

MAKO is the coding language. Modularity is the engine.

Inside a ModuMAKO script, Modularity owns:

- UI and inspector controls
- 2D and 3D physics
- rendering and scene objects
- keyboard, mouse, and controller input
- audio and other engine services
- lifecycle scheduling and the native runtime

Standalone packages such as `MakoUI`, `Mako2D`, `Mako3D`, `Physics2D`, and
`Physics3D` are not available to ModuMAKO. They remain useful when MAKO runs as
its own application runtime, but an engine script must use `ModuEngine`,
`ModuInput`, and Modularity's facades instead. The compiler rejects accidental
runtime mixing with an explicit error.

```sh
mko modumako player.modumako -o PlayerMovement.moducpp
```

Modularity desktop builds bundle the matching compiler at `Tools/mko` and use
it before searching `PATH`. Developers can override the compiler explicitly
with `MODUMAKO_COMPILER`; packagers can set CMake's
`MODULARITY_MODUMAKO_COMPILER` path.

Native recompiles link into a staging directory before the editor promotes the
new library. A syntax or native compile failure therefore leaves the last good
hot-reload binary untouched.

The complete `examples/modularity_character.modumako` sample uses Modularity's
WASD, sprint, jump, mouse-look, rigidbody, capsule collider, and standalone
movement systems with persistent per-object ModuMAKO state.

Modularity's language service detects the `using ModuMAKO;` script environment
and supplies engine-aware completion, signature help, and hover information.
Qualified completion includes the `Input.*` facade, while MAKO-style fields and
loop variables remain available as document symbols.

To run the character sample in a scene:

1. Attach `modularity_character.mko` to the player object.
2. Add a camera and set its type to Player.
3. Place a static box collider beneath the character. A rendered plane without
   a collider is visual only and the rigidbody will fall through it.
4. Enter Play mode and use WASD, Sprint, Jump, and mouse look.

## Script shape

```mako
using ModuMAKO;
using ModuEngine;
using ModuInput;

script "PlayerMovement" : ModuNode;

camera: SceneObj = none;
walkSpeed = 4;
_verticalVelocity = 0;

Begin() {
    Ensure.obj;
    Ensure.Rigidbody3D(obj);
}

TickUpdate(dt) {
    move = Input.WASDMovement();
    direction = Movement.Direction(move, camera);
    obj.Rigidbody3D.Accelerate(direction, walkSpeed, 20, 25);
}
```

`ModuMAKO`, `ModuEngine`, and `ModuInput` lower to the matching ModuCPP
modules. Fields whose names begin with `_` are private runtime state; other
top-level fields are public and remain available to Modularity's inspector.
Engine references should have an explicit type, such as `SceneObj`. Numeric,
boolean, string, `Vector2`, `Vector3`, `Vector4`, and `Color(...)` fields can be
inferred. `Color(...)` correctly uses Modularity's native `Vector4` value type.

```mako
_spawnOffset = Vector3(0, 1, 0);
_hudTint = Color(0.2, 0.7, 1, 1);
```

State that must survive between lifecycle calls belongs in private script
fields. Modularity's standalone character movement types have short MAKO
aliases, so a controller can retain one settings, state, and debug instance:

```mako
_movementSettings: MovementSettings = StandaloneMovementSettings();
_movementState: MovementState = StandaloneMovementState();
_movementDebug: MovementDebug = StandaloneMovementDebug();

TickUpdate() {
    TickStandaloneMovement(
        _movementState,
        _movementSettings,
        dt,
        _movementDebug
    );
}
```

Keep these custom engine values private with the `_` prefix. They are runtime
state rather than inspector fields, and each object receives its own copies.

Local variables may also use MAKO type annotations:

```mako
samples: i32 = 3;
enabled: boolean = true;
velocity: Vector3 = Vector3(0, 0, 0);
```

ModuMAKO supports the fixed-width integer types (`i8` through `i64` and `u8`
through `u64`), `isize`, `usize`, `f32`, `f64`, and the usual MAKO aliases
such as `int`, `float`, `str`, and `boolean`.

The compiler recognizes Modularity's runtime and editor hooks, including
`Begin`, `TickUpdate`, `Update`, `Spec`, `TestEditor`, collision hooks, and
inspector/editor-window hooks. MAKO-style `if condition {}` and `while
condition {}` are lowered to ModuCPP conditions, while `and`, `or`, `not`, and
`none` are lowered to their native equivalents.

MAKO string interpolation is also available. Values are formatted through
ModuCPP's type-aware `TextR` helper:

```mako
AddLog("speed={walkSpeed}, grounded={grounded}", Type.Info);
```

Control-flow blocks may be written across multiple lines or kept compact:

```mako
if health <= 0 { AddLog("down", Type.Warning); }
else if health < 25 { AddLog("low", Type.Warning); }
else { AddLog("ready", Type.Success); }

while warmup < 3 { warmup = warmup + 1; }

for checkpoint in _checkpoints {
    AddLog("checkpoint={checkpoint}", Type.Info);
}

for tag in objectTags { AddLog("tag={tag}", Type.Info); }
```

Typed list fields and locals use Modularity's native `List<T>` or `Array<T>`
containers. Homogeneous number, string, and boolean lists infer their element
type. Empty or mixed lists need an annotation so their native type is clear:

```mako
_checkpoints: List<f32> = [1, 2.5, 4];
_spawnWeights = [1, 0.5, 2];

Begin() {
    labels: List<str> = ["start", "finish"];
    push(labels, "checkpoint");
    labelCount: i64 = len(labels);
    finalLabel: str = last(labels);
    removedLabel: str = pop(labels);
    for label in labels { AddLog(label, Type.Info); }
}
```

`push(collection, value)` appends through the native container, and
`len(collection)` returns its current size as an `i64`. `first`, `last`, and
`pop` retain MAKO's checked-empty behavior, while `has` tests membership.

## Current milestone

This compiler slice covers fields, hooks, calls, assignments, conditions,
`for ... in range(...)` loops, and general iterable loops, including compact
single-line loop bodies. Typed and homogeneous inferred list literals lower to
Modularity's native containers. Structs, mixed list literals, lambdas, and full
static engine-facade validation still need dedicated lowering passes.
Unsupported top-level syntax fails with a ModuMAKO error instead of producing
misleading native code.
