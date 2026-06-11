# Vintage Railroading — Wiki

Free-form, multi-gauge railroads for Vintage Story 1.22.x. This wiki covers both
**playing** with the mod (laying track, driving, coupling) and **authoring** new rolling
stock (locomotives and carts) whose JSON is set up correctly to work with the coupling
system.

> Status: the mod is feature-complete enough to build and run a working railroad. A few
> systems (interaction-based coupling, coupling persistence) are still command-driven or
> in progress — those caveats are called out where they apply.

## Pages

1. [Getting Started](01-getting-started.md) — install, the items, and your first train.
2. [Laying Track](02-laying-track.md) — the Track Layer, snapping, grades, junctions.
3. [Spawning & Driving a Train](03-driving.md) — placing, mounting, controls, pickup.
4. [Coupling & Consists](04-coupling.md) — how to couple cars and how following works.
5. [Authoring a New Cart or Locomotive](05-authoring-rolling-stock.md) — **the JSON
   guide**: entity files, shapes, the seat, and exactly what coupling needs.
6. [The Coupling JSON Contract](06-coupling-json-contract.md) — a focused reference for
   the attributes and conventions a vehicle must satisfy to couple correctly.
7. [Commands Reference](07-commands.md) — every `/vrr*` command.
8. [Troubleshooting](08-troubleshooting.md) — common breakages and how to spot them.

## Core idea (read this first)

Every train in Vintage Railroading is described by a tiny **1D state** rather than free
3D physics:

- `SegmentId` — which track segment the vehicle is on.
- `Distance` — how many metres along that segment it sits.
- `Speed` — metres/second along the track.

Each server tick the vehicle advances `Distance` by `Speed`, then projects that 1D
position onto the segment's curve to get a real `(x, y, z)`, heading, and pitch. When it
runs off the end of a segment it continues onto the connected one at the shared node.

**Coupling is built directly on this model.** A coupled car does not do its own physics —
it simply asks: *"where is the point N metres behind my leader, along the track?"* and
places itself there every tick. That is why coupling follows curves, grades, and
junctions automatically: it is the same path-walking the leader already uses.

Understanding this 1D model makes the rest of the wiki — especially the authoring
pages — much easier to follow.
