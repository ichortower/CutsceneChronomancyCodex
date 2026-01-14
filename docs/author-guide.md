# Cutscene Chronomancy Codex - Author Guide

This document explains how to use the event commands added by this mod.


## Contents

* [General Notes](#general-notes)
* [Stream Control](#stream-control)
* [Actor Control](#actor-control)
* [Viewport Control](#viewport-control)
* [World Control](#world-control)


## General Notes

Here are a few terms and things to know to help you read this document.

* If you are here for the beta, please check out the example text files in
  the beta zip! There are some (fairly simple) scripts in there that may help
  you understand a particular command more clearly than my prose here.\
  Each one is formatted correctly to be used as a test event, so simply copy
  the `.txt` file to your game directory, rename it `test_event.txt`, and
  run `debug rte` in your SMAPI console to run it directly. There is no need
  to patch reload or anything else; it will be read live.
* When a command says that it "blocks" (or talks about another command
  "blocking"), that means that it will pause at that point in the command
  list and prevent the event from continuing until some condition is met.
  For example, `move <npc> 3 0 2` blocks, because the optional `true`
  argument was not given, so the event will wait until the move completes
  before running the next command.


## Stream Control

"Streams" are the headline feature of the Codex. They allow you to implement
parallel execution of multiple command lists, giving you a lot more control
over timing than a single command list with only limited support for
simultaneous commands. At its most basic, this allows you to make *any
commands*, even blocking ones, run in parallel or wait for each other without
messing up your main event flow.

The quick overview is that you declare a stream and give it a list of commands.
At that time, the stream begins executing, but does not block the main command
list, which continues from the command following the stream. Since a stream is
independent of any other command list, you can use blocking and non-blocking
commands as you desire within it, as if it were its own main event script.

When a stream exhausts its command list, it becomes idle. You can restart it
if desired, or overwrite it with new commands, or issue a command from any
other stream that will wait for it to reach this state.

The power this gives you might not be obvious, so let's consider an example:

```
(example to follow when fully baked)
```

When a stream command explanation refers to "a stream", the main command list
should also be considered a stream, even though it does not have a stream id
and can't be targeted by stream command arguments. For example, `StreamPause`
works on "the current stream", but that means it works on the main event as
well, and not only inside a stream context.


### `StreamStart`

`ichortower.CCC_StreamStart <id>`
`ichortower.CCC_StreamBegin <id>` (alias)

This command declares a stream. When it is encountered, it immediately scans
ahead through the command list looking for a matching `StreamEnd` command, and
gives the intervening commands over to the new stream to begin executing (they
will not actually execute until the stream's event loop reaches them).

The id value can be any string, but it must not already be in use by another
active stream within this event: attempting to reuse an active id will error
out, and the new stream will be discarded. Once a stream has completed or
halted, it becomes inactive, and you may reuse its id with a new `StreamStart`
command to replace it; just make sure you don't have any future commands that
need to control the old stream!

After starting a stream, you use the same id you gave it in order to issue
commands from any other stream (`StreamAwait`, `StreamHalt`, etc.).


### `StreamEnd`

`ichortower.CCC_StreamEnd`

This command does not do anything on its own (in fact, executing it is an
error, since it means your Starts and Ends are not balanced). It is merely a
signal that the list of commands for a new stream is over.

There is no `id` argument: each stream takes the first balanced `StreamEnd` it
encounters, in order to allow nesting streams and prevent them from crossing
command list boundaries.


### `StreamAwait`

`ichortower.CCC_StreamAwait <id> [id...]`

This command takes one or more stream ids and blocks until they are all
complete (they have run out of commands and are sitting idle, or they have
been halted by the `StreamHalt` command).

Use this to guarantee that a particular stream (or streams) has finished before
proceeding. For example, if a stream is making an actor move to a particular
spot, you can await it in order to be sure that actor has arrived. Either they
will already be there, or you'll pause until they are.

A stream cannot await itself (this will cause an error); but there is not yet
any protection against circular awaiting, so be careful not to do that.


### `StreamPause`

`ichortower.CCC_StreamPause <int> [int...]`

This command is like `pause`, but instead of using a global timer, this blocks
execution only on the current stream.

The arguments are any number of integers, each meaning a number of
milliseconds. This command will choose one of the values at random, and pause
for that length of time; so with one value it will behave just like vanilla
`pause`, but you can have a random delay by giving multiple options, if you
like.

This command works by replacing itself with an instance of `precisePause`, the
undocumented vanilla event command which (as it turns out) has the correct
behavior already. You can use `precisePause` instead if you like, but I think
this one has a clearer name, and this one allows randomness.


### `StreamHalt`

`ichortower.CCC_StreamHalt <id> [id...]`

This command terminates the streams specified in the arguments. A halted stream
has its command index set to beyond the end of its command list, and has its
state forcibly set to idle. As a result, it will immediately satisfy any other
stream that is awaiting it, and its id becomes available again for reuse.

A stream *can* halt itself, although I don't know what use case that has.


### `StreamRestart`

`ichortower.CCC_StreamRestart <id> [id...]`

This command restarts the specified streams. A restarted stream has its command
index set to 0 and its idle state forcibly unset.

This is intended for use on completed or halted streams, but it should work on
actives ones as well.

A stream can restart itself (but see `StreamLoop`).


### `StreamLoop`

`ichortower.CCC_StreamLoop`

A shortcut version of `StreamRestart` which works only on the current stream
and restarts it by setting its command index to 0.

It will work on the main stream, but I don't see much use for it without the
`goto` features promised for 1.7 (and when we get those, you should just use
`goto`).


## Actor Control

These commands give you more flexibility when controlling actors (characters).


### `ActorPathTo`

`ichortower.CCC_ActorPathTo <actor> <x> <y> <facing> [wait]`

This command tells any actor (farmer or NPC) to move to the given map
coordinates, by using the pathfinder to figure out how to get there instead of
relying on you giving them directions.

This is very useful if you have been using streams and an actor has been
halted at an unknowable point along a looping `advancedMove`, or any similar
situation where you don't know where they are exactly but want them to reach
a fixed point. Or if you just don't want to do the math yourself!

Note that while the farmer will consider NPCs to be obstacles and will
navigate around them (using the positions they were in when the route was
calculated), NPCs will walk through each other. I may address this in the
future, if I can figure out how.


### `ActorAwaitMovement`

`ichortower.CCC_ActorAwaitMovement <actor> [actor...]`

This command blocks until all named event actors have completed their current
movements. This works a lot like vanilla's `waitForAllStationary` (all actors)
and `proceedPosition` (one actor only), but it allows any number of actors,
and it checks for ongoing movement a bit differently.

In particular, this command does not consider a character in a pause step
during an advancedMove to have stopped (`waitForAllStationary` and
`proceedPosition` both do this). This means that using this command to wait for
a looping `advancedMove` will block forever, so do not do this without a plan to
call `ActorHalt` from some other stream.


### `ActorHalt`

`ichortower.CCC_ActorHalt [next|waitnext] <actor> [actor...]`

This command stops the movement of all named actors, and removes any
NPCControllers that may have been puppeting them.

The first argument is optional and can be one of two special strings in order
to change the behavior (both case-insensitive):

* `next`: actors will be allowed to finish the current leg of their movement
    before halting.
* `waitnext`: as `next`, but this command will also block until the movements
    complete. this is done by inserting an `ActorAwaitMovement` command.

In general, it is best to use the optional argument, in order to have your
characters stop squarely on tiles, instead of stopping in between them (being
"off the grid" can cause problems with positioning).

Likewise, in general I advise using `waitnext` over `next`, since some moves
may not halt correctly without `ActorAwaitMovement` to help unstick them.
But if you are already awaiting the movement in another stream, `next` will
suffice.
