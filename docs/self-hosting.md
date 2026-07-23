# Self-hosting MAKO

MAKO is being bootstrapped from the current C# implementation into a compiler
written in hosted MAKO. The existing implementation is stage 0: it remains the
reference compiler until the MAKO implementation can compile itself.

## Bootstrap stages

1. Hosted runtime foundations — implemented
2. Tokens and lexer — implemented
3. AST and parser — implemented; parity fixtures growing
4. Name resolution and type checking — core semantic pass implemented
5. HIR and MIR lowering — initial typed pipeline implemented
6. MIR optimization — initial pass pipeline implemented
7. Native code generation
8. Reproducible self-compilation

The kernel project demonstrated that typed MKO can compile for a freestanding
environment. It is not part of this bootstrap: the self-hosted compiler is a
normal hosted program with file, string, collection, and process facilities.

## Hosted runtime boundary

[`selfhost/runtime.mko`](../selfhost/runtime.mko) is the portability boundary
between compiler code and the current interpreter runtime. It provides:

- growable text construction;
- character and identifier classification;
- normalized source-line access;
- string-keyed symbol maps;
- explicit success/error results;
- source input and output helpers.

Lexer and parser code should use this layer where practical. Its initial
representation uses MAKO lists and dictionaries. Later native runtime work can
replace those internals without changing the compiler stages above it.

## Token and lexer boundary

[`selfhost/lexer.mko`](../selfhost/lexer.mko) mirrors the reference lexer in
hosted MAKO. A token is currently a dictionary with `kind`, `value`, `line`,
`col`, `end_line`, and `end_col` fields. Accessor functions define the stable
interface that the self-hosted parser should consume.

The lexer recognizes MAKO keywords, literals, comments, operators,
punctuation, escaped strings, and template strings. It also tracks source
positions and reports malformed numbers, unexpected characters, unterminated
strings, and unterminated block comments.

## AST and parser boundary

[`selfhost/parser.mko`](../selfhost/parser.mko) introduces dictionary-backed AST
nodes with a mandatory `kind` field and stable field accessors. The
recursive-descent parser handles scripts, namespaces, imports, packages, typed
functions and methods, structs and struct literals, top-level and local
constants, `main`, blocks, generalized assignments, returns, printing,
conditionals, loops, lambdas, exceptions, `run`, `input`, calls, indexing,
field access, lists, dictionaries, unary operators, and the full binary
precedence ladder.

Template strings are lowered into alternating `template_text` and
`template_expression` nodes. Embedded expressions use the normal expression
parser, including calls, indexing, nested quoted strings, and operator
precedence. Frontend parity fixtures live in
`tests/fixtures/selfhost_parser_cases.json`; this corpus should continue to
grow alongside name resolution and type checking.

## Semantic checking boundary

[`selfhost/checker.mko`](../selfhost/checker.mko) performs the first semantic
pass over the self-hosted AST. It collects packages, structs, functions, and
constants; creates isolated function and lambda scopes; resolves identifier
uses; and reports duplicate or missing declarations.

The type pass checks annotated assignments, fixed-width integer literal
ranges, recursively nested collection literals, function and method arguments,
typed lambda bodies, return values, all-path return coverage, boolean
conditions, numeric/string operators, const reassignment, struct fields, and
missing or unknown struct members. Its public API returns all discovered
diagnostics:

```mako
issues = BootstrapChecker.check(source);
```

Parity hardening still needs complete integer-width behavior near 64-bit
limits, callable lambda signatures at call sites, flow-sensitive type merging,
and source spans on AST nodes and semantic diagnostics.

## Typed HIR boundary

[`selfhost/hir.mko`](../selfhost/hir.mko) lowers semantically valid ASTs into
structured `mako.hir 1`. Every expression carries an inferred type. Functions,
parameters, structs, globals, bindings, calls, collection and struct literals,
templates, lambdas, and structured control-flow regions remain explicit.

The lowerer runs the resolver and checker first and refuses to emit HIR when
diagnostics exist. `BootstrapHir.emit_json(source)` provides deterministic
machine-readable output for parity fixtures and the upcoming MIR lowerer.

Compiler orchestration should execute inside a function rather than directly
inside `main`: MAKO top-level bindings are true globals and are intentionally
visible to functions, so a helper assignment with the same name can update
one.

## MIR boundary

[`selfhost/mir.mko`](../selfhost/mir.mko) lowers `mako.hir 1` into
`mako.mir 1`. Mutable bindings become explicit loads and stores, expressions
produce typed temporaries, and structured `if`, `while`, and `for` regions
become labeled basic blocks with branch, jump, return, and unreachable
terminators.

The MIR validator rejects duplicate blocks and temporary definitions, missing
terminators and block targets, and undefined temporary operands. Collection,
struct, template, call, input, and mutation operations have explicit MIR
instructions. `break` and `continue` lower through an explicit loop-target
stack. `try` introduces body and catch successors, `throw` terminates its
block, catch variables load the current exception, and a try without a catch
uses a rethrow terminator.

## MIR optimization boundary

[`selfhost/optimizer.mko`](../selfhost/optimizer.mko) validates MIR, folds
block-local constants, simplifies constant branches, removes unreachable
blocks, eliminates dead pure instructions, and validates the result again.
Block-local propagation is intentionally conservative until dominance
analysis is implemented; dead-code dependency tracking spans the full
function.

`BootstrapOptimizer.emit_json(source)` emits deterministic optimized
`mako.mir 1` for backend fixtures.

Run its regression coverage with:

```bash
mko tests/selfhost_runtime.mko
mko tests/selfhost_lexer.mko
mko tests/selfhost_parser.mko
mko tests/selfhost_checker.mko
mko tests/selfhost_hir.mko
mko tests/selfhost_mir.mko
mko tests/selfhost_optimizer.mko
mko check selfhost/runtime.mko
mko check selfhost/lexer.mko
mko check selfhost/parser.mko
mko check selfhost/checker.mko
mko check selfhost/hir.mko
mko check selfhost/mir.mko
mko check selfhost/optimizer.mko
```

## First compatibility rule

Every self-hosted stage should have fixtures that can be processed by both the
C# reference implementation and the MAKO implementation. Compare structured
outputs rather than implementation details. This keeps the bootstrap
incremental and makes divergences visible before the compiler depends on them.
