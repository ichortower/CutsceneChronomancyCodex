# Cutscene Chronomancy Codex

This Stardew Valley mod is a framework (of sorts) which is designed to unlock
fabulous new time powers for mod authors to use when writing events. If you
have ever found yourself frustrated or confused by the flow of event scripts,
or just by a single command that's difficult to use (\*cough\* `viewport`),
then this mod may be for you.

Here are some of the things you can do with these commands:

* Stop some actors' movements, either immediately or at the next stop,
    and without having to stop all of them
* Tell an actor to go to a specific tile without having to write up the path
    instructions yourself, or even know what tile they are standing on
* Control the viewport with reasonable units and queueing
* Set up parallel command lists that run without blocking the event loop
* Loop, stop, restart, and/or wait for parallel lists to finish executing
* Cause an event to "take time" by advancing the world time when it completes
    (correctly processing machines and advancing NPCs along their schedules)
* Make temporary map tile changes or apply map overrides that last only until
    the current event ends

If these things appeal to you, head over to the [author guide](author-guide.md)
for details!
