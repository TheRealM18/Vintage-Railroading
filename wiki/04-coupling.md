# 4. Coupling & Consists

[← Driving](03-driving.md) · [Wiki home](README.md) · [Next: Cargo Cars & Storage →](05-cargo-and-storage.md)

This page explains **how coupling behaves** in play. For the JSON you must write so a new
vehicle couples correctly, see
[Authoring Rolling Stock](06-authoring-rolling-stock.md) and the focused
[Coupling JSON Contract](07-coupling-json-contract.md).

## What can couple to what

Coupling works on the shared **`IRailVehicle`** interface, which **both** vehicle classes
implement. That means:

- A **locomotive** (`EntityTrain`) can lead.
- A **cargo car** (`EntityCargo`) can follow.
- A cargo car can also follow **another cargo car**, and a locomotive can even follow a
  cargo car — the system does not care about the class, only that both are rail vehicles.

So a realistic consist — `loco → log car → coal cart → fluid tanker` — is just a chain of
each vehicle following the one ahead.

## How following works

A coupled car keeps a fixed **arc-length gap** behind its **leader** *along the track* —
around curves, over grades, and through junctions. It does this by sampling the point
that is `gap` metres behind the leader's position on the network, every server tick, and
snapping itself there. It mirrors the leader's speed only so its model pitches correctly
on slopes; it does **not** read the throttle.

Because the follower rides the exact same path the leader walked, you get correct
cornering and hill-climbing for free — there is no separate physics to tune. A cargo car
has **no** movement of its own at all: with no leader, its speed is forced to zero and it
parks where it sits.

## Coupling two vehicles (the Coupler tool)

Coupling is done with a **Coupler** — a durability tool that comes in four metal tiers
(copper, tin-bronze, iron, steel) that differ only in how many couplings they last before
wearing out. Craft one (see [Authoring → crafting tree](06-authoring-rolling-stock.md))
or grab one from the **VRR Other** creative tab.

To couple:

1. Spawn two vehicles on the **same track**, near each other (any mix of loco and cargo).
2. Hold a Coupler and **right-click the vehicle you want to be the leader** — it is
   *selected* (you'll see "selected #… as leader").
3. **Right-click the second vehicle** — it becomes the **follower** of the first, at the
   gap they're currently spaced. This costs **1 durability** and clears the selection.
4. Drive the **leader** (a locomotive if you want the consist to move); the follower
   trails it.

To uncouple:

- **Sneak + right-click** a coupled vehicle with the Coupler — it detaches from its leader
  (also costs 1 durability).

Right-clicking empty air or a block with the Coupler **clears** a pending selection, so
it's easy to start over if you picked the wrong leader.

> The old `/vrrcouple` and `/vrruncouple` chat commands have been **removed** — the Coupler
> tool replaces them. Coupling is now a survival action with a material cost.

The coupling **gap** is taken from the two vehicles' spacing at the moment you couple
(clamped to a sane range), so they do not jump together on coupling.

## State that makes it work

Each vehicle stores two coupling values as synced attributes (see the
[Coupling JSON Contract](07-coupling-json-contract.md) for the exact keys):

- **Leader id** (`vrrLeader`) — the entity id of the vehicle it follows. `0` means "not
  coupled" — a standalone vehicle or the lead locomotive.
- **Coupling gap** (`vrrGap`) — the metres it keeps behind its leader.

A vehicle with a leader id of `0` is a **leader**; any vehicle pointing at another's id is
a **follower**. This is all the data the follow system needs, and both classes expose it
identically through `IRailVehicle`.

## Chains (consists of 3+)

The follow math already supports a **follower-of-a-follower**: car C follows car B, which
follows locomotive A, and each simply trails the one ahead. `EntityCargo`'s leader lookup
explicitly accepts either an `EntityTrain` or another `EntityCargo` as its leader, so
chains of cargo cars work. To build a longer consist by hand you couple A→B (select A,
click B), then select B and click C, and so on — each Coupler click links one follower to
one leader. A proper consist identifier and chain-aware linking are planned so that
picking up or uncoupling a mid-train car behaves cleanly.

## Current limitations (be aware)

These are known and on the roadmap; design your test setups around them:

- **One link per click pair.** The Coupler links one selected leader to one clicked
  follower. Longer consists are built link by link.
- **Leader/follower is chosen by click order.** The **first** vehicle you click is the
  leader; the **second** becomes its follower. If you pick them in the wrong order, clear
  the selection (right-click air) and redo.
- **Couplings do not persist across a reload.** The leader is stored as an entity id,
  which is not stable across save/load, so consists drop apart when the world reloads.
  Re-couple after loading. (A persistent consist id is planned.)
- **Reverse/push is unverified.** The math should place cars behind by the gap whether the
  leader goes forward or back, so a consist *should* reverse fine — but this has not been
  fully verified in long, curved push moves yet.
- **Picking up a mid-consist car** auto-uncouples the cars behind it instead of re-linking
  the gap.

---

[← Driving](03-driving.md) · [Wiki home](README.md) · [Next: Cargo Cars & Storage →](05-cargo-and-storage.md)
