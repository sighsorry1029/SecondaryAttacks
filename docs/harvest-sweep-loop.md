# Harvest Sweep Loop

`harvestSweep` keeps a spinning copied secondary attack loop active until resources run out, the weapon changes, or secondary attack is pressed again.

## Loop

The loop uses normal player movement input rather than forcing forward movement. The active animation is rewound from `loopEnd` to `loopStart`; `animationSpeed` scales the loop while active, and `moveSpeedFactor` controls player movement speed during the spin.

## Example

```yml
Global:
  harvestSweep:
    loopStart: 0.4
    loopEnd: 0.6
    animationSpeed: 1.0
    moveSpeedFactor: 0.75
```

Harvest runs once each time the copied sweep reaches `loopEnd`. The harvest center and radius come from the equipped scythe primary attack; vanilla Scythe uses a Farming-scaled radius from 1.5m to 2.5m.
