# 4. Coupling & Consists

[← Driving](03-driving.md) · [Wiki home](README.md) · [Next: Authoring →](05-authoring-rolling-stock.md)

This page explains **how coupling behaves** in play. For the JSON you must write so a new
cart couples correctly, see
[Authoring Rolling Stock](05-authoring-rolling-stock.md) and the focused
[Coupling JSON Contract](06-coupling-json-contract.md).

## How following works

A coupled car keeps a fixed **arc-length gap** behind its **leader** *along the track* —
around curves, over grades, and through junctions. It does this by sampling the point
that is `gap` metres behind the leader's position on the network, every server tick, and
snapping itself there. It mirrors the leader's speed only so its model pitches correctly
on slopes; it does **not** read the throttle.

Because the follower rides the exact same path the leader walked, you get correct
cornering and hill-climbing for free — there is no separate physics to tune.

## Coupling two vehicles (current method)

Coupling is currently driven by a command (interaction-based coupling is planned):

1. Spawn two vehicles on the **same track**, near each other.
2. Stand between them.
3. Run **`/vrrcouple`** — the two nearest trains within 12 blocks are linked. The nearer
   one becomes the **follower** of the farther one.
4. Drive the **leader**; the follower trails it at the gap it had when you coupled.

To detach:

- **`/vrruncouple`** — detaches the nearest coupled train from its leader.

The coupling **gap** is taken from the two vehicles' spacing at the moment you couple
(clamped to a sane range), so they do not jump together on coupling.

## State that makes it work

Each vehicle stores two coupling values as synced attributes (see the
[Coupling JSON Contract](06-coupling-json-contract.md) for the exact keys):

- **Leader id** — the entity id of the car it follows. `0` means "not coupled" — a
  standalone vehicle or the lead locomotive, which reads the throttle normally.
- **Coupling gap** — the metres it keeps behind its leader.

A vehicle with a leader id of `0` is a **leader**; any vehicle pointing at another's id is
a **follower**. This is all the data the follow system needs.

## Chains (consists of 3+)

The follow math already supports a **follower-of-a-follower**: car C follows car B, which
follows locomotive A, and each simply trails the one ahead. However, the current
`/vrrcouple` command only links the two nearest vehicles as a **pair**. To build a longer
consist by hand you would couple A+B, then position C and couple B+C. A proper consist
identifier and chain-aware linking are planned so that picking up or uncoupling a
mid-train car behaves cleanly.

## Current limitations (be aware)

These are known and on the roadmap; design your test setups around them:

- **Pairs only via command.** Interaction-based (right-click car-to-car) coupling is not
  in yet; use `/vrrcouple`.
- **Leader/follower choice is positional.** `/vrrcouple` makes the *nearer* vehicle the
  follower of the *farther* one. If that is backwards for your layout, reposition and
  re-couple.
- **Couplings do not persist across a reload.** The leader is stored as an entity id,
  which is not stable across save/load, so consists drop apart when the world reloads.
  Re-couple after loading. (A persistent consist id is planned.)
- **Reverse/push is unverified.** The math should place cars behind by the gap whether the
  leader goes forward or back, so a consist *should* reverse fine — but this has not been
  fully verified in long, curved push moves yet.
- **Picking up a mid-consist car** auto-uncouples the cars behind it instead of re-linking
  the gap.

---

[← Driving](03-driving.md) · [Wiki home](README.md) · [Next: Authoring →](05-authoring-rolling-stock.md)
