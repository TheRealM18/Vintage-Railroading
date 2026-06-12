# 8. Troubleshooting

[← Commands](07-commands.md) · [Wiki home](README.md)

Common breakages when running or authoring, and how to spot the cause.

## A new cart won't spawn / crashes on spawn

- **`attachmentPoints` at the top level of the shape file.** VS expects attachment points
  on an **element** — a lowercase `attachmentpoints` array *inside* one of the elements
  (e.g. the frame/body), not at the root of the shape JSON. A top-level `attachmentPoints`
  causes `Failed deserializing <shape>.json: Exception thrown by the target of an
  invocation` and the entity loads invisible. Move the `Seat-driver` AP into an element.
- **Missing `Seat-driver` attachment point in the shape.** Seat creation runs at spawn for
  `EntityTrain`; without a valid `Seat-driver` AP it NREs. Open the shape and confirm an
  attachment point named exactly `Seat-driver` exists.
- **Missing `attributes` / `mountAnimations` block.** `EntityTrain` extends `EntityBoat`,
  whose init reads these and throws without them. Copy the block from `train.json` verbatim.
- **Duplicate entity `code`.** Two entity JSONs sharing one `code` (e.g. both `"train"`)
  collide and one silently overrides the other. Give every vehicle a unique `code`.

## A cart spawns but won't couple / won't follow

- **Not `class: EntityTrain`.** Only `EntityTrain` carries the follow + coupling logic. A
  cart on a different class will just sit there.
- **Not placed on the network.** The follower offsets along the track from the leader's
  segment; a vehicle that isn't on a segment (`vrrSegId` unset) has nothing to follow
  along. Spawn it onto a rail.
- **Leader/follower reversed.** `/vrrcouple` makes the *nearer* vehicle follow the
  *farther* one. If the wrong one is leading, reposition and re-couple.
- **Too far apart.** `/vrrcouple` only considers trains within 12 blocks of you and of
  each other's neighbourhood. Bring them closer.

## The consist fell apart after reloading the world

Expected, for now: couplings are stored as an entity id, which is not stable across
save/load. Re-couple after loading. A persistent consist id is planned.

## The follower jitters or floats

- **Gravity left on.** Ensure `passivephysicsmultibox` has `gravityFactor: 0.0`. The
  position is set from the track each tick; gravity fights it.
- **Mixed gauges in one line.** Keep a consist on a single gauge.

## Track won't join / two segments won't connect

- Snapping may be **off**. Run `/vrrsnap` to toggle it back on, then lay the joining
  endpoint within ~0.75 blocks of the existing node.

## Track laid close together keeps merging into one line

- The opposite case: snapping is **on** and fusing nearby endpoints. Run `/vrrsnap` to
  turn it off while you lay parallel track, then turn it back on.

## The locomotive tilts the wrong way on hills

- The grade pitch sign is a known "verify in game" item. If models nose the wrong way on
  slopes, that is a one-line sign flip in the renderer code (`PitchFromHeading` for the
  entity, and the tie/rail pitch in the mesh builder). It does not affect coupling.

## Textures show as missing / wrong

- The shape's texture **keys** must match the entity's `client.textures` keys. The shipped
  set is `dark`, `glass`, `iron`, `metal`, `red`. Reuse those keys, or update both sides
  together.

---

[← Commands](07-commands.md) · [Wiki home](README.md)
