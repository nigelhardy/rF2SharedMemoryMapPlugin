# rFactor 2 Internals Shared Memory Map Plugin

This plugin mirrors exposed rFactor 2 internal state into shared memory buffers.  Essentially, this is direct memcpy of rFactor 2 internals.

Reading shared memory allows creating  external tools running outside of rFactor 2 and written in languages other than C++ (C# sample is included).  It also allows keeping CPU time impact in rFactor 2 process to the minimum.

#### This work is based on:
  * rF2 Internals Plugin sample #7 by ISI/S397 found at: https://www.studio-397.com/modding-resources/
  * rF1 Shared Memory Map Plugin by Dan Allongo found at: https://github.com/dallongo/rFactorSharedMemoryMap

## Features
Plugin uses double buffering and offers optional weak synchronization on global mutexes.

Plugin is built using VS 2015 Community Edition, targeting VC12 (VS 2013) runtime, since rF2 comes with VC12 redist.

## Refresh Rates:
* Telemetry - 90FPS, but it appears that game sends same values twice, so effective rate is 50FPS (provided there's no mutex contention).  This might be due to my particular processor speed, so your mileage may vary.
* Scoring - 5FPS.
* Extended - 5FPS and on tracked callback by the game.

## Monitor
Plugin comes with rF2SMMonitor program that shows how to access exposed internals from C# program.  It is also useful for visualization of shared memory contents and general understanding of rFactor 2 internals.

## Memory Buffer Uses
  * Recommended: Simply copy rF2StateHeader part of the buffer, and check mCurrentRead variable.  If it's true, use this buffer, otherwise use the other buffer.  See `Monitor\rF2SMMonitor\rF2SMMonitor\MainForm.cs MainUpdate` method for example of use in C# (ignore mutex).
  * Synchronized: use mutex to make sure buffer is not overwritten (this is best effort activity, not a guarantee.  See comnents in C++ code for exact details). Generally, _do not use this method if you are visualizing rF2 internals_ and not doing any analysis that requires buffer to be complete.  Example: Crew Chief will not be happy if there are two copies of the vehicles in the buffer, but it does not matter in most other cases.  This use requires full understanding of how plugin works, and could cause FPS drop if not done right.  See `Monitor\rF2SMMonitor\rF2SMMonitor\MainForm.cs MainUpdate` method for example of use in C#
  * Basic: If half refresh rate is enough, and you can tolerate partially overwritten buffer once in a while, simply read one buffer and don't bother with double buffering or mutex.

## Support this project
If you would like to support this project, you can donate [here.](http://thecrewchief.org/misc.php?do=donate)

# Release history

3/22/2017 - v1.1.0.1

  Plugin:
  * Replaced rF2State::mInRealTime with mInRealTimeFC and mInRealTimeSU values, to distiguish between InRealtime state reported via ScoringUpdate, and via Enter/ExitRealtime calls.
  * rF2State::mCurrentET is no longer updated between Scoring Updates, and matches value last reported by the game.

  Monitor:
  * Extended monitor to dislay more information
  * Implemented correct "Best Split" time calculation logic.

2/26/2017 - v1.0.0.1

  Fixed synchronization of:
  * rF2State::mElapsedTime
  * rF2State::mLapStartET
  * rF2State::mLapNumber

  This eliminates the gap those values had between telemetry and scoring updates.

01/31/2017 - v1.0.0.0
  * Plugin: Added damage and invulnerability tracking
  * Monitor: Added phase and damage tracking and logging


1/18/2017 v0.5.0.0 - Initial release