# MAKO Standard Library

Every builtin, always available — no imports needed.

## Utility

| Function | Description |
|---|---|
| `type(v)` | Type name: `"number"`, `"string"`, `"bool"`, `"list"`, `"dict"`, `"fn"`, `"none"` |
| `to_num(v)` | Convert to number (errors if impossible) |
| `to_str(v)` | Convert to string |
| `assert(cond, msg)` | Throw `msg` if `cond` is falsy |
| `exit(code)` | End the program |

## Math

| Function | Description |
|---|---|
| `abs(x)` `floor(x)` `ceil(x)` `round(x)` `sqrt(x)` | The classics |
| `pow(x, y)` | x to the power y |
| `max(a, b)` `min(a, b)` | Larger / smaller of two |
| `clamp(v, lo, hi)` | Pin `v` into `[lo, hi]` |
| `lerp(a, b, t)` | Linear interpolation: `a + (b-a)*t` |
| `sign(x)` | -1, 0, or 1 |
| `sin(x)` `cos(x)` `tan(x)` | Radians |
| `atan2(y, x)` | Angle of vector (y, x) |
| `pi()` | 3.14159… |
| `random()` | Random float 0–1 |
| `random_int(lo, hi)` | Random integer, inclusive both ends |

## Geometry & collision

| Function | Description |
|---|---|
| `dist(x1, y1, x2, y2)` | Distance between two 2D points |
| `dist3d(x1,y1,z1, x2,y2,z2)` | Distance between two 3D points |
| `rects_overlap(x1,y1,w1,h1, x2,y2,w2,h2)` | 2D AABB overlap test |
| `box3d_overlap(min1,max1, min2,max2)` (6 args each, min/max form) | 3D AABB overlap test — matches `Mako3D.object_bounds()`'s output |
| `circles_overlap(x1,y1,r1, x2,y2,r2)` | Circle overlap test |
| `point_in_rect(px,py, x,y,w,h)` | Point inside rectangle? |

## Pathfinding (game AI)

The grid is a list of rows; each row a list where `0`/falsy = walkable and
truthy = wall.

| Function | Description |
|---|---|
| `find_path(grid, sx, sy, ex, ey)` | A\* over a 4-connected grid. Returns a list of `[x, y]` steps — start excluded, goal included. `[]` if unreachable. |
| `line_of_sight(grid, x1, y1, x2, y2)` | `true` if no wall cell lies on the straight line between the two cells. |

## Strings

| Function | Description |
|---|---|
| `len(s)` | Length |
| `upper(s)` `lower(s)` `trim(s)` | Case / whitespace |
| `contains(s, sub)` `starts_with(s, p)` `ends_with(s, p)` | Tests |
| `replace(s, old, new)` | Replace all occurrences |
| `split(s, sep)` | String → list |
| `join(list, sep)` | List → string |
| `slice(s, start, end)` | Substring, end-exclusive |

## Lists

| Function | Description |
|---|---|
| `len(xs)` | Length |
| `push(xs, v)` `pop(xs)` | Append / remove last (in place) |
| `first(xs)` `last(xs)` | End elements |
| `reverse(xs)` | Reversed copy |
| `has(xs, v)` | Membership test |
| `slice(xs, start, end)` | Sub-list, end-exclusive |

## Dicts

| Function | Description |
|---|---|
| `len(d)` | Number of keys |
| `keys(d)` `values(d)` | Lists of keys / values |
| `has(d, key)` | Key exists? |
| `get(d, key, default)` | Value or default if missing |
| `remove(d, key)` | Delete a key |
| `merge(a, b)` | New dict; b's keys win |

## Higher-order (take a lambda)

| Function | Description |
|---|---|
| `map(xs, fn)` | Transform each element |
| `filter(xs, fn)` | Keep elements where fn is truthy |
| `reduce(xs, fn, init)` | Fold to a single value |
| `sort_by(xs, fn)` | Sort by fn's key |
| `each(xs, fn)` | Call fn per element |
| `any(xs, fn)` `all(xs, fn)` | Boolean tests |

## Files

| Function | Description |
|---|---|
| `read(path)` | Whole file as a string |
| `write(path, text)` | Overwrite file |
| `append(path, text)` | Append to file |
| `lines(path)` | File as a list of lines |
| `exists(path)` | File exists? |
| `delete(path)` | Delete file |

## System

| Function | Description |
|---|---|
| `time()` | Unix timestamp (seconds) |
| `sleep(seconds)` | Pause execution |
| `env(name)` | Environment variable (or none) |
| `args()` | List of command-line arguments passed after the script path |

## JSON

| Function | Description |
|---|---|
| `json_encode(v)` | Encode a value (dict/list/string/number/bool/none) as a JSON string |
| `json_decode(s)` | Parse a JSON string into MAKO values (throws on malformed input) |

See [Net](net.md) for HTTP requests, which return JSON-friendly responses.
