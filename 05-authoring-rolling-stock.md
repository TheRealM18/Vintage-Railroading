# 5. Authoring a New Cart or Locomotive

[← Coupling](04-coupling.md) · [Wiki home](README.md) · [Next: Coupling JSON Contract →](06-coupling-json-contract.md)

This is the page you want if you are **building new rolling stock**. It walks through the
files you create and, critically, **what makes a vehicle work with the coupling system**.

> **Key fact up front:** at the time of writing there is a single vehicle entity class,
> **`EntityTrain`**, used by the one shipped locomotive. Coupling state lives on that
> class. The most reliable way to add a new cart today is to **reuse `EntityTrain`** for
> your cart's entity and give it a different shape/textures/box. A cart is, mechanically,
> a train that you choose not to drive (you couple it behind a locomotive). Everything in
> this guide assumes `class: EntityTrain`. Making a *separate* cart class is possible but
> means re-implementing the network-follow + coupling logic, and is not covered here.

## The files you create

For one new vehicle ("flatcar" in this example) under
`assets/vintagerailroading/`:

```
entities/flatcar.json                  <- the entity definition (copy train.json)
shapes/entity/flatcar.json             <- the 3D model (must have a Seat AP; see below)
itemtypes/flatcarplacer.json           <- (optional) an item to spawn it
recipes/grid/flatcarplacer.json        <- (optional) how to craft that item
lang/en.json                           <- add display names
```

The **entity** and **shape** are the parts that matter for coupling. The placer/recipe
are only how the player obtains and spawns it.

## Step 1 — the entity JSON (copy the locomotive, then change cosmetics)

Start from the shipped `entities/train.json` and change only what differs for your cart.
The structure below is the real, working locomotive entity, annotated:

```jsonc
{
  "code": "flatcar",            // <- MUST be unique. Do NOT reuse "train".
  "class": "EntityTrain",       // <- keep this; it provides movement + coupling
  "tags": [ "inanimate", "vehicle" ],

  // EntityTrain extends EntityBoat, which REQUIRES this attributes block to exist
  // (EntityBoat.Initialize reads mountAnimations etc. and NREs without it).
  "attributes": {
    "shouldSwivelFromMotion": false,
    "speedMultiplier": 1,
    "swimmingOffsetY": 0,
    "mountAnimations": { "idle": "sitflooridle", "ready": "", "forwards": "", "backwards": "" }
  },

  "hitboxSize":    { "x": 3.0, "y": 4.5 },
  "deadHitboxSize":{ "x": 3.0, "y": 4.5 },
  "eyeHeight": 2.5,
  "canClimb": false,

  "behaviorConfigs": {
    "passivephysicsmultibox": {
      "collisionBoxes": [ { "x1": -1.0, "y1": 0, "z1": -1.5, "x2": 1.0, "y2": 2.5, "z2": 1.5 } ],
      "groundDragFactor": 1, "airDragFallingFactor": 0.5,
      "gravityFactor": 0.0          // <- 0 gravity; the network drives position, not physics
    },
    "creaturecarrier": {
      "seats": [ { "apName": "Seat-driver", "controllable": true, "bodyYawLimit": null, "eyeHeight": 1 } ]
    },
    "selectionboxes": { "selectionBoxes": [ "Seat-driver" ] }
  },

  "client": {
    "renderer": "Shape",
    "shape": { "base": "vintagerailroading:entity/flatcar" },   // <- your shape
    "textures": {                                               // <- keys must match shape
      "iron":  { "base": "game:block/metal/ingot/iron" },
      "dark":  { "base": "game:block/stone/rock/basalt1" },
      "red":   { "base": "game:block/metal/plate/copper" },
      "metal": { "base": "game:block/metal/plate/iron" },
      "glass": { "base": "game:block/glass/plain" }
    },
    "behaviors": [
      { "code": "repulseagents" },
      { "code": "passivephysicsmultibox" },
      { "code": "interpolateposition" },   // client-only; smooths position between ticks
      { "code": "selectionboxes" },
      { "code": "creaturecarrier" }
    ]
  },

  "server": {
    "behaviors": [
      { "code": "repulseagents" },
      { "code": "passivephysicsmultibox" },
      { "code": "selectionboxes" },
      { "code": "creaturecarrier" }
    ]
  }
}
```

### What you may safely change per vehicle
- `code` (must be unique), the `shape.base`, the `textures` map, the `hitboxSize` /
  `deadHitboxSize`, the `collisionBoxes`, and `eyeHeight`.

### What you must NOT remove (or coupling/mounting breaks)
- `"class": "EntityTrain"` — this is what makes it ride the network and respond to
  coupling. A cart with a different class will not follow a leader.
- The whole `attributes` block with `mountAnimations` — without it the underlying
  EntityBoat init throws.
- The `creaturecarrier` seat and the matching `Seat-driver` attachment point in the shape
  (see Step 2). Even a non-driveable cart needs a valid seat AP or seat creation NREs.
- `gravityFactor: 0.0` — the vehicle's position is set from the track each tick; leaving
  gravity on fights that.

## Step 2 — the shape JSON (must contain the seat attachment point)

Your model lives at `shapes/entity/flatcar.json`. The one hard requirement for it to work
with `EntityTrain` is an **attachment point named exactly `Seat-driver`** on whichever
element should be the seat/origin (on the locomotive it is on the cab body). The seat
behavior and the selection box both reference `Seat-driver` by name; if it is missing,
sitting on the vehicle NREs.

The shape's **texture keys** (the `#name` faces use, and the `textures` block at the top
of the shape) must match the keys your entity `client.textures` declares. The shipped
locomotive uses `dark`, `glass`, `iron`, `metal`, `red` — reuse those keys and you can
reuse the same texture map.

> Tip: the simplest path to a working cart is to copy the locomotive shape, delete the
> elements you do not want (e.g. boiler, stack), keep a flat deck, and keep the
> `Seat-driver` attachment point exactly where it is.

## Step 3 — registering the class

`EntityTrain` is already registered by the mod, so you do **not** register anything new
when you reuse it — the game maps your `"class": "EntityTrain"` automatically. You only
add a new registration if you write a brand-new C# class (out of scope here).

## Step 4 — spawning your cart

You have two options:

- **Reuse the existing placer logic by adding a new placer item** that spawns your entity
  code. The shipped placer spawns the hard-coded entity `vintagerailroading:train`. To
  spawn `vintagerailroading:flatcar` you would point a placer at that code. (If you only
  need it for testing, you can also spawn via the game's creative entity spawn tools.)
- **Spawn one, then couple it** behind a locomotive with `/vrrcouple`
  (see [Coupling](04-coupling.md)).

## Step 5 — make sure it couples

If your cart uses `"class": "EntityTrain"` and is placed on the network, **it already
satisfies the coupling contract** — the leader/gap attributes and the follow logic are
part of `EntityTrain`. There is no per-entity "can couple" flag to set. Place it on the
same track as a locomotive and `/vrrcouple`.

For the precise list of what the coupling system reads and requires, continue to the
[Coupling JSON Contract](06-coupling-json-contract.md).

## Lang entries

Add display names so your items/entities are not shown by their codes:

```json
{
  "item-flatcarplacer": "Flatcar",
  "entity-flatcar": "Flatcar"
}
```

---

[← Coupling](04-coupling.md) · [Wiki home](README.md) · [Next: Coupling JSON Contract →](06-coupling-json-contract.md)
