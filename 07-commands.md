# 7. Commands Reference

[← Coupling JSON Contract](06-coupling-json-contract.md) · [Wiki home](README.md) · [Next: Troubleshooting →](08-troubleshooting.md)

All commands require the chat privilege and a player.

| Command | What it does |
|---------|--------------|
| `/vrrgauge` | Cycle the active gauge used for newly laid track (narrow / metre / standard / broad). |
| `/vrrnode` | Place track via command: run once to set point A, move and run again to lay a segment A→B. (The Track Layer item is the preferred, survival-friendly way — see [Laying Track](02-laying-track.md).) |
| `/vrrsnap` | Toggle endpoint snapping on/off. Off = lay track close together without auto-joining. |
| `/vrrswitch` | Stand within 4 blocks of a junction node and cycle its through-route to the next branch. |
| `/vrrcouple` | Couple the two nearest trains within 12 blocks; the nearer becomes the follower of the farther. |
| `/vrruncouple` | Detach the nearest coupled train from its leader. |
| `/vrrtrain` | Legacy train spawn (superseded by the placer item). |

## Typical coupling session

```
# lay a straight test track with the Track Layer first, then:
/vrrgauge            # (optional) pick a gauge before laying track
# spawn loco with the placer item on the rail
# spawn a second vehicle on the same rail, a few blocks behind
# stand between them:
/vrrcouple           # links them; nearer one follows the farther
# sit in the loco, drive with W/S; the car trails it
/vrruncouple         # when you want to separate them
```

---

[← Coupling JSON Contract](06-coupling-json-contract.md) · [Wiki home](README.md) · [Next: Troubleshooting →](08-troubleshooting.md)
