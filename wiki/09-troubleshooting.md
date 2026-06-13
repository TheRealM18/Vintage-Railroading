# 9. Troubleshooting

[← Commands](08-commands.md) · [Wiki home](README.md)

Common breakages when running or authoring, and how to spot the cause.

## "Don't know how to instantiate entity of type 'EntityCargo' / 'EntityTrain'"

The entity class is **not registered**. Both `EntityTrain` and `EntityCargo` must be
registered in the mod system (`api.RegisterEntity("EntityCargo", typeof(EntityCargo))`).
The shipped mod registers both; you hit this only if you stripped a registration or a
build did not include the latest mod system. Likewise each storage behavior code
(`woodstorage`, `fuelstorage`, `fluidstorage`) must be registered with
`RegisterEntityBehaviorClass`, or an entity that lists it will fault on load.

## A cargo car seats me instead of opening storage (or vice versa)

- A **cargo car** (`EntityCargo`) has **no seat** — right-click with an **empty hand**
  opens storage. If nothing opens, the car is probably missing its storage behavior in one
  of the behavior lists, or you are holding an item (holding an item suppresses the
  open-storage interaction so the wrench-pickup still works).
- A **locomotive** that *also* has storage opens storage on empty-hand/non-sneak and seats
  you on **sneak + empty hand**. If it seats you when you wanted storage, make sure the
  storage behavior is listed **before** `creaturecarrier` in the behavior arrays — the
  storage behavior must run first to claim the click.

## A new cart won't spawn / crashes on spawn

- **(Locomotive) `attachmentPoints` at the top level of the shape file.** VS expects
  attachment points on an **element** — a lowercase `attachmentpoints` array *inside* an
  element, not at the root. A top-level `attachmentPoints` causes `Failed deserializing
  <shape>.json` and the entity loads invisible. Move the `Seat-driver` AP into an element.
- **(Locomotive) Missing `Seat-driver` attachment point.** Seat creation runs at spawn for
  `EntityTrain`; without a valid `Seat-driver` AP it NREs. **Cargo cars do not need this** —
  if you are building a cargo car, you should be using `EntityCargo`, which has no seat.
- **(Locomotive) Missing `attributes` / `mountAnimations` block.** `EntityTrain` extends
  `EntityBoat`, whose init reads these and throws without them. Copy the block from
  `train.json` verbatim. (Cargo cars do not extend boat and do not need mount animations.)
- **Wrong class for the job.** If you built a non-driven cart as `EntityTrain` and it keeps
  demanding seat parts, switch it to `EntityCargo` and delete the seat machinery — that is
  the supported path for cargo.
- **Duplicate entity `code`.** Two entity JSONs sharing one `code` collide and one silently
  overrides the other. Give every vehicle a unique `code`.

## The Coupler tool doesn't couple anything

- **You only clicked one vehicle.** Coupling takes two clicks: right-click the **leader**
  (you'll see "selected … as leader"), then right-click the **follower**. One click only
  selects.
- **You clicked the same vehicle twice.** The second click must be a *different* vehicle.
- **You clicked air/a block between vehicles.** That **clears** the selection by design —
  aim directly at each vehicle's body.
- **The tool broke.** Couplers have limited durability (copper 80 → steel 640 actions);
  check it isn't worn out, and remember each couple *and* uncouple costs 1.
- **To uncouple:** sneak + right-click the coupled vehicle. A plain click won't uncouple.

## A cart spawns but won't couple / won't follow

- **Not a rail-vehicle class.** Only `EntityTrain` and `EntityCargo` carry the follow +
  coupling logic (via `IRailVehicle`). A cart on any other class will just sit there.
- **Not placed on the network.** The follower offsets along the track from the leader's
  segment; a vehicle that isn't on a segment (`vrrSegId` unset) has nothing to follow
  along. Spawn it onto a rail.
- **Leader/follower reversed.** The Coupler makes the *first-clicked* vehicle the leader
  and the *second-clicked* its follower. If that's backwards, clear the selection
  (right-click air) and redo in the right order.
- **Out of reach.** The Coupler acts on the vehicle you actually right-click, so aim
  directly at each car.
- **A cargo car parks and never moves on its own.** That is correct — a cargo car has no
  throttle. It only moves when coupled behind a leader you actually drive.

## Storage GUI opens but items won't move in/out

The storage behaviors open the vanilla block-entity inventory dialog bound to a block
position under the entity. The window opens, but if slot moves do not sync for the entity,
the fix is switching to the entity-inventory dialog style. If you see the GUI but items
snap back, this is the thing to check.

## A loaded car lost its cargo when I picked it up

By design the car **drops** its contents into the world on pickup so they are not silently
destroyed — look on the ground where the car was. (For a fluid tanker the liquid drops in
its container-stack form.)

## The consist fell apart after reloading the world

Expected, for now: couplings are stored as an entity id, which is not stable across
save/load. Re-couple after loading. A persistent consist id is planned.

## The follower jitters or floats

- **Gravity left on.** Ensure `passivephysicsmultibox` has `gravityFactor: 0.0`. The
  position is set from the track each tick; gravity fights it.
- **`interpolateposition` server-side.** It is a **client-only** smoothing behavior — list
  it only under `client.behaviors`. The cargo car also re-projects onto the spline every
  render frame, so the curve is always the final word; if you still see jitter, confirm
  interpolation is not duplicated server-side.
- **Mixed gauges in one line.** Keep a consist on a single gauge.

## A vehicle tilts the wrong way on hills

- The grade-pitch sign was corrected so vehicles nose **up** going uphill and **down**
  going downhill (`PitchFromHeading` negates the vertical heading component, since VS pitch
  is positive-nose-down). Both classes share the same convention. If a future model still
  looks inverted, it's a one-line sign flip in `PitchFromHeading` — it does not affect
  coupling or movement, only the visual tilt.

## Track won't join / two segments won't connect

- Snapping may be **off**. Run `/vrrsnap` to toggle it back on, then lay the joining
  endpoint within ~0.75 blocks of the existing node.

## Track laid close together keeps merging into one line

- The opposite case: snapping is **on** and fusing nearby endpoints. Run `/vrrsnap` to
  turn it off while you lay parallel track, then turn it back on.

## Textures show as missing / wrong

- The shape's texture **keys** must match the entity's `client.textures` keys. The shipped
  locomotive set is `dark`, `glass`, `iron`, `metal`, `red`; the cargo cars use keys like
  `iron`, `dark`, `metal`, `wood`, `log`, `bark`. Reuse the keys, or update both sides
  together. The shipped cargo cars use placeholder textures until dedicated art is added.

## How to get more diagnostic detail

Run `/vrrdebug` to toggle verbose logging. With it on, the mod prints `[vrr]`-prefixed
lines for entity init, per-tick movement state, client render projection, and interaction
results to the server/client log — useful when a vehicle won't move, won't render, or
won't couple. It's off by default so normal play stays quiet; turn it off again when done.

---

[← Commands](08-commands.md) · [Wiki home](README.md)
