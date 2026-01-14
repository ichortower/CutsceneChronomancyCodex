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


## Stream Control

"Streams" are the headline feature of the Codex. They allow you to implement
parallel execution of multiple command lists, giving you a lot more control
over timing than a single command list with only limited support for
simultaneous commands. At its most basic, this allows you to make *any
commands*, even blocking ones, run in actual parallel, the way you may have
wished `beginSimultaneousCommand` worked (but it does not do this).

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
main event				stream 1
----------				--------
viewport 24 30 true
pause 2000
(define stream 1)
move farmer -2 0 3
emote farmer 16
speak Emily "Oh, what did..."
...
(await stream 1)
```

For the sake of the stream command explanations, when they refer to "a stream",
that category also includes the main command list, even though that one does
not have a stream id and can't be used as an argument in the control commands.
For example, `_StreamPause` works on "only the current stream", but that
means it works on the main event as well, and not only inside a stream context.


### `_StreamStart`

`ichortower.CCC_StreamStart <id>`
`ichortower.CCC_StreamBegin <id>` (alias)

This command declares a stream. When it is encountered, it immediately scans
ahead through the command list looking for a matching `_StreamEnd` command, and
gives the intervening commands over to the new stream to begin executing (they
will not actually execute until the stream's event loop reaches them).

The id value can be any string, but it must be locally unique within the event.
You will use the id to reference this stream in other commands that control
existing streams.

Reusing an id will work if the previous stream has already completed (or
halted), but if it is still active, this will error out and the new stream will
be discarded.

### `_StreamEnd`

`ichortower.CCC_StreamEnd`

This command does not do anything on its own (in fact, executing it is an
error, since it means your Starts and Ends are not balanced). It is merely a
signal that the list of commands for a new stream is over.

There is no `id` argument: each stream takes the first balanced \_StreamEnd it
encounters, in order to allow nesting streams and prevent them from crossing
command list boundaries.

### `_StreamAwait`

`ichortower.CCC_StreamAwait <id> [id...]`

This command takes one or more stream ids and blocks until they are all
complete (they have run out of commands and are sitting idle, or they have
been halted by the `_StreamHalt` command).

Use this to guarantee that a particular stream (or streams) has finished before
proceeding. For example, if a stream is making an actor move to a particular
spot, you can await it in order to be sure that actor has arrived. Either they
will already be there, or you'll pause until they are.

A stream cannot await itself (this will cause an error); but there is not yet
any protection against circular awaiting, so be careful not to do that.

### `_StreamPause`

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
this one has a clearer name.

### `_StreamHalt`

`ichortower.CCC_StreamHalt <id> [id...]`

This command terminates the streams specified in the arguments. A halted stream
has its command index set to beyond the end of its command list, and has its
state forcibly set to idle. As a result, it will immediately satisfy any other
stream that is awaiting it, and its id becomes available again for reuse.

A stream *can* halt itself, although I don't know what use case that has.

### `_StreamRestart`

`ichortower.CCC_StreamRestart <id> [id...]`

This command restarts the specified streams. A restarted stream has its command
index set to 0 and its idle state forcibly unset.

This is intended for use on completed or halted streams, but it should work on
actives ones as well.

A stream can restart itself (but see `_StreamLoop`).

### `_StreamLoop`

`ichortower.CCC_StreamLoop`

A shortcut version of `_StreamRestart` which works only on the current stream
and restarts it by setting its command index to 0.

It will work on the main stream, but I don't see much use for it without the
`goto` features promised for 1.7.
