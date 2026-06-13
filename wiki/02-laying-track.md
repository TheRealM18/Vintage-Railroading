# 2. Laying Track

[← Getting Started](01-getting-started.md) · [Wiki home](README.md) · [Next: Driving →](03-driving.md)

Track in Vintage Railroading is **spline-based** — segments are smooth Hermite curves
between nodes, not block-by-block rails. You lay it with the **Track Layer** tool.

## Click-to-click laying (chaining)

1. Hold the Track Layer.
2. **Right-click** a block where the track should start → "Track START set."
3. **Right-click** another block where it should end → a curved segment is laid between
   the two points.
4. The end of that segment automatically becomes the start of the next one, so you can
   **keep right-clicking** to chain continuous track across the landscape.
5. **Sneak (Shift) + right-click** clears the pending point and ends the chain.

Each click's curve direction comes from the way you are **facing** at that click, so you
shape curves by turning before you click. Track rides the **top surface** of the block
you click.

## Snapping (joining track)

When a new endpoint lands within the snap distance (default **0.75 blocks**) of an
existing node, it **fuses** onto that node and inherits its tangent, so the join is
smooth and the two segments become genuinely connected (trains can traverse across it).

If you want to lay track *close together without joining* — parallel sidings, a yard, a
double main — toggle snapping off:

- `/vrrsnap` — toggles endpoint snapping on/off. While off, endpoints are placed exactly
  where you stand and never auto-join. Toggle it back on when you actually want to
  connect into existing track.

## Grades (slopes)

Track is no longer forced flat. If your start and end clicks are at different heights,
the segment becomes a smooth grade. The slope is spread evenly across the whole segment
so there is no mid-curve hump or dip. Grades are clamped to ±100% (45°). The chat reply
reports the grade percentage and rise when you lay a sloped segment.

Both the rails/ties and the vehicles themselves tilt to follow the grade — locomotives
and cargo cars alike, since both compute pitch from the same track heading.

## Junctions & switches

Where three or more segment ends meet at one node, you have a **junction**. By default a
train picks the **smoothest** exit (the branch that best continues its heading), which
keeps it going "straight through." To choose a different branch:

- `/vrrswitch` — stand within 4 blocks of a junction node and run it to cycle the
  junction's through-route to the next branch. Trains traversing that node will then take
  the selected branch.

A coupled cargo car follows its leader through whichever branch the leader took, because
it walks the same path — you do not switch cars individually.

See [Commands](08-commands.md) for the full list.

---

[← Getting Started](01-getting-started.md) · [Wiki home](README.md) · [Next: Driving →](03-driving.md)
