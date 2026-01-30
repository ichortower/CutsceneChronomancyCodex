# Event Command Codex - Author Guide

This document explains how to use the event commands added by this mod.


## Contents

* [General Notes](#general-notes)
* [Stream Control](#stream-control)
  * [StreamStart](#streamstart)
  * [StreamEnd](#streamend)
  * [StreamAwait](#streamawait)
  * [StreamPause](#streampause)
  * [StreamHalt](#streamhalt)
  * [StreamRestart](#streamrestart)
  * [StreamLoop](#streamloop)
* [Actor Control](#actor-control)
  * [ActorPathTo](#actorpathto)
  * [ActorAwaitMovement](#actorawaitmovement)
  * [ActorAwaitAnimation](#actorawaitanimation)
  * [ActorHalt](#actorhalt)
* [Viewport Control](#viewport-control)
  * [ViewportMove](#viewportmove)
  * [ViewportAwait](#viewportawait)
  * [ViewportHalt](#viewporthalt)
* [Ambient Light Control](#ambient-light-control)
  * [AmbientLightShift](#ambientlightshift)
  * [AmbientLightAwait](#ambientlightawait)
  * [AmbientLightHalt](#ambientlighthalt)
* [World Control](#world-control)
  * [WorldAdvanceTime](#worldadvancetime)
  * [TemporaryMapTiles](#temporarymaptiles)
  * [TemporaryMapOverride](#temporarymapoverride)
* [Conditional Execution](#conditional-execution)
  * [If/ElseIf/Else/EndIf](#ifelseifelseendif)
* [Event Variables](#event-variables)
  * [VarSet](#varset)
  * [VAR_QUERY](#var_query)
* [Vanilla Command Notes](#vanilla-command-notes)


## General Notes

Here are a few terms and things to know to help you read this document.

* If you are here for the beta, please check out the example text files in
  the beta zip! There are some (fairly simple) scripts in there that may help
  you understand a particular command more clearly than my prose here.\
  Each one is formatted correctly to be used as a test event, so simply copy
  the `.txt` file to your game directory, rename it `test_event.txt`, and
  run `debug rte` in your SMAPI console to run it directly. There is no need
  to patch reload or anything else; it will be read live.
* When a command says that it "blocks" (verb) (or talks about another command
  "blocking"), that means that it will pause at that point in the command
  list and prevent the stream from continuing until some condition is met.
  For example, `move <npc> 3 0 2` blocks, because the optional `true`
  argument was not given, so the stream will wait until the move completes
  before running the next command.


## Stream Control

"Streams" are the headline feature of the Codex. They allow you to implement
parallel execution of multiple command lists, giving you a lot more control
over timing than a single command list with only limited support for
simultaneous commands. At its most basic, this allows you to run almost any
commands simultaneously, although there are some perhaps-unintuitive
restrictions (see [Vanilla Command Notes](#vanilla-command-notes) for details).

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

`ichortower.ECC_StreamStart <id>`
`ichortower.ECC_StreamBegin <id>` (alias)

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

`ichortower.ECC_StreamEnd`

This command does not do anything on its own (in fact, executing it is an
error, since it means your Starts and Ends are not balanced). It is merely a
signal that the list of commands for a new stream is over.

There is no `id` argument: each stream takes the first balanced `StreamEnd` it
encounters, in order to allow nesting streams and prevent them from crossing
command list boundaries.


### `StreamAwait`

`ichortower.ECC_StreamAwait <id> [id...]`

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

`ichortower.ECC_StreamPause <int> [int...]`

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

`ichortower.ECC_StreamHalt <id> [id...]`

This command terminates the streams specified in the arguments. A halted stream
has its command index set to beyond the end of its command list, and has its
state forcibly set to idle. As a result, it will immediately satisfy any other
stream that is awaiting it, and its id becomes available again for reuse.

A stream *can* halt itself, although I don't know what use case that has.


### `StreamRestart`

`ichortower.ECC_StreamRestart <id> [id...]`

This command restarts the specified streams. A restarted stream has its command
index set to 0 and its idle state forcibly unset.

This is intended for use on completed or halted streams, but it should work on
actives ones as well.

A stream can restart itself (but see `StreamLoop`).


### `StreamLoop`

`ichortower.ECC_StreamLoop`

A shortcut version of `StreamRestart` which works only on the current stream
and restarts it by setting its command index to 0.

It will work on the main stream, but I don't see much use for it without the
`goto` features promised for 1.7 (and when we get those, you should just use
`goto`).


## Actor Control

These commands give you more flexibility when controlling actors (characters).


### `ActorPathTo`

`ichortower.ECC_ActorPathTo <actor> <x> <y> <facing> [wait]`

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

`ichortower.ECC_ActorAwaitMovement <actor> [actor...]`

This command blocks until all named event actors have completed their current
movements. This works a lot like vanilla's `waitForAllStationary` (all actors)
and `proceedPosition` (one actor only), but it allows any number of actors,
and it checks for ongoing movement a bit differently.

In particular, this command does not consider a character in a pause step
during an advancedMove to have stopped (`waitForAllStationary` and
`proceedPosition` both do this). This means that using this command to wait for
a looping `advancedMove` will block forever, so do not do this without a plan to
call `ActorHalt` from some other stream.


### `ActorAwaitAnimation`

`ichortower.ECC_ActorAwaitAnimation <actor> [actor...]`

This command is just like `ActorAwaitMovement`, except instead of blocking to
wait for movement to finish, it waits for animations. This accounts for sprite
animations using the `animate` command, as well as `emote`.

Note that just like `ActorAwaitMovement`, this will block forever if it is
awaiting a looping animation, so be careful not to do that without a way to
use `ActorHalt`.


### `ActorHalt`

`ichortower.ECC_ActorHalt [next|waitnext] <actor> [actor...]`

This command stops the movement and animation of all named actors, and removes
any NPCControllers that may have been puppeting them.

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


## Viewport Control

These commands are intended to replace `viewport move` with a version that I
find more sensible: it uses tile units instead of pixels per frame, and you
can queue movements as well as wait for them to complete.

The other forms of vanilla's `viewport` command should still serve; they work
well.

### `ViewportMove`

`ichortower.ECC_ViewportMove <x> <y> <duration> [override] [wait]`

This command sets up a viewport movement.

`x` and `y` are given in *map tiles*, and can be either relative to the current
position or absolute on the map. To use relative values, just use plain
integers (so e.g. `1 2` would mean to move the viewport 1 tile right and 2
tiles down). For absolute ones, prepend the letter `a` (or `A`; it's
case-insensitive), so e.g. `a14 a20` would mean to move the viewport to (14,20).
You may mix and match these, so `a22 3` is valid and means to move to x 22 and
y 3 tiles down from the current position.

`duration` is in milliseconds and determines how long the move will take to
complete.

By default, a viewport move will be queued behind any ongoing moves, and the
command will not block. The optional arguments `override` and `wait` can be
given to change this behavior: `override` will cause the existing queue to be
emptied before starting this move, and `wait` will cause the command to block
until the queue has been finished.

**Note:** the movements set up by this command are totally separate from how
vanilla's `viewport move` moves the viewport. Do not mix and match them.


### `ViewportAwait`

`ichortower.ECC_ViewportAwait`

This command blocks until all queued viewport moves have completed.


### `ViewportHalt`

`ichortower.ECC_ViewportHalt`

This command immediately halts any ongoing viewport moves and empties the
viewport move queue.


## Ambient Light Control

Although vanilla has the `ambientLight` command which lets you set the ambient
light color and intensity at any time, it is merely immediate and there is no
way to transition smoothly. These commands give you the ability to fade it
gradually and queue the operations, just like with the viewport control
commands; you can use this to help simulate things like sunsets.


### `AmbientLightShift`

`ichortower.ECC_AmbientLightShift <red> <green> <blue> <duration> [override] [wait]`

This command sets up a gradual ambient light shift.

`red`, `green`, and `blue` should be integers from 0 to 255, representing the
RGB values of the color that will be **subtracted** from white to generate the
game tint, just as it is with vanilla's `ambientLight` command.

**Note:** The game's draw code has a special case for when all three values of
the ambient light color are 255 (which would normally mean full darkness): this
is treated as full brightness instead, the same as `0 0 0`. I recommend
avoiding this value, and using `254 254 254` instead if you need pitch black;
it's close enough and won't cause flashing in and out of darkness.

`duration` is in milliseconds and determines how long the shift will take to
complete.

Like with `ViewportMove`, by default, the shift will be queued behind any
ongoing shifts, and the command will not block. Just like that command, you can
give the optional argument `override` to first empty the queue before starting,
and you can give the optional argument `wait` to block until the queue empties.


### `AmbientLightAwait`

`ichortower.ECC_AmbientLightAwait`

This command blocks until all queued ambient light shifts have completed.


### `AmbientLightHalt`

`ichortower.ECC_AmbientLightHalt`

This command immediately halts all ongoing ambient light shifts and empties
the light shift queue.


## World Control


### `WorldAdvanceTime`

`ichortower.ECC_WorldAdvanceTime <hhmm>`

This command causes world time to pass when the event finishes. In addition to
advancing the game clock, machines are given processing time and all NPCs are
automatically advanced along their daily schedules to be ready for their next
move (spouses with no schedule will be warped to bed if the target time is
after 2200).

Give the time as an integer between 600 and 2600, but it must be after the
current game time or you will get an error.

In multiplayer, this command has no effect: events don't freeze time in
multiplayer, so time will pass just by watching it.

**Note:** the machine processing and NPC advancing will happen immediately when
this command executes, but the time change will be delayed until the event
actually ends.

**Note:** If possible, you should find a way to warn players ahead of time
that you plan to use this command, and ideally give them a chance to avoid it.
They are likely accustomed to events taking no time, and may have plans for
their day which you may ruin if you surprise them with this.

This command is also available as a trigger action, using the same name, and
should work anywhere actions are accepted (except in multiplayer, as above).
When triggered outside of an event, the time change will be immediate.


### `TemporaryMapTiles`

`ichortower.ECC_TemporaryMapTiles (<layer> <x> <y> <sheet> <index>)+`

This command temporarily replaces map tiles on the current map, reverting them
to their previous state (by reloading the map) when the event ends.

Use layer, x, and y to specify what tile to change. Sheet and index are to
tell what to change that tile to. To remove a tile, use `-1` for index; in this
case, sheet will be ignored, so you can pass something meaningless like `-`.


### `TemporaryMapOverride`

`ichortower.ECC_TemporaryMapOverride (<asset> <x> <y>)+`

This command temporarily applies one or more map overrides to the current
location. The overrides will be removed (and the map reloaded) when the event
ends.

The asset name is expected to be under `Maps/`, so to apply the map asset at
`Maps/foo/bar`, you should specify just `foo/bar`. The x and y values are map
tile coordinates of where to overlay it (the top-left corner, just like you
would specify in e.g. a Content Patcher pack).


## Conditional Execution


### `If`/`ElseIf`/`Else`/`EndIf`

`ichortower.ECC_If <Game state query>`\
`ichortower.ECC_ElseIf <Game state query>`\
`ichortower.ECC_Else`\
`ichortower.ECC_EndIf`

These commands define a set of conditional command blocks. Just like a
conventional programming language, the conditions are checked in order, and as
soon as one is satisfied, that block is executed and the others are discarded
without even being evaluated.

You can put any number of event commands between the control commands, e.g.:

```
ichortower.ECC_If PLAYER_NPC_RELATIONSHIP Current Abigail married
emote Abigail 16
speak Abigail "That's so rude! You're talking about my ${husband^wife^spouse}$!$a"
emote Pierre 40
ichortower.ECC_EndIf
```

In this example, the three commands (emote/speak/emote) are only executed if
the player is married to Abigail. But you can add more blocks, too:

```
ichortower.ECC_If PLAYER_NPC_RELATIONSHIP Current Abigail married
emote Abigail 16
speak Abigail "That's so rude! You're talking about my ${husband^wife^spouse}$!$a"
emote Pierre 40
ichortower.ECC_ElseIf PLAYER_NPC_RELATIONSHIP Current Abigail dating
emote Abigail 12
speak Abigail "Hey! I happen to like that person quite a lot!$a"
ichortower.ECC_Else
speak Abigail "Who? Farmer @?$u"
ichortower.ECC_EndIf
```

In this case, you'll get the first block (`If`..`ElseIf`) if married, the
second block (`ElseIf`..`Else`) if dating, and the third block
(`Else`..`EndIf`) for any other relationship.

You can have as many `ElseIf` blocks as you like, but only one `Else`, and
the `Else` must come last. Violations of this order will parse, but you will
get a warning about the blocks not being reachable.

You *should* be able to nest these commands, but I haven't tested that yet.

**Note:** the game state queries that drive this are evaluated when the
blocks are parsed for execution, so they are "real time" and may reflect
changes to game state that have occurred earlier in the event.


## Event Variables

This command and game state query are intended for use with the Conditional
Execution blocks (see above). You can use them to store and manipulate integer
or string values, then change your event's behavior accordingly.


### `VarSet`

`ichortower.ECC_VarSet <name> <value|expression>`

This command sets a user-defined variable to the value or expression that
follows it. You can use it to save a value for later, or to keep track of a
running total, or other similar actions.

When picking a variable name, there are a few restrictions:

* it must be **alphanumeric only** ([a-zA-Z0-9])
* it must contain at least one letter ([a-zA-Z])
* it must not start with 'ECC' (case-insensitive; this is to prevent
    collisions with planned future features)

Other than those, you can choose whatever you like.

The expression parsing and evaluation proceeds as you might expect, including
operator precedence, but note that it supports only integers and strings (no
floating-point numbers!): the `.` symbol is a low-priority concatenate operator
and may cause confusing results if used by mistake.

**Note**: every resulting value that this command generates is serialized to a
string, including during the parsing and evaluation steps. You might not need
to know this, but if something weird happens, maybe that will help you figure
it out.

See the following table for what symbols are supported. They are listed in
precedence order (so parentheses are highest, then exponent, and so on).

<table>
<tr>
<th>Symbol</th><th>Examples</th><th>Explanation</th>
</tr>

<tr>
<td>

`' "`

</td>
<td>

`'foobar'`

`"text value"`

</td>
<td>

Use `'` or `"` to enclose a string literal. Note that event scripts are given
as long delimited strings, and in typical Content Patcher use you will need to
escape your double-quotes (e.g. `myvar . \"my string\"`) in order for them to
work properly, since they would end the JSON string otherwise.

</td>
</tr>

<tr>
<td>

`()`

</td>
<td>

`(1 + 2) * 3`

</td>
<td>

Use parentheses to enclose operations and increase their precedence. In the
example, `1 + 2` is evaluated first to `3`, then `3 * 3` is evaluated to `9`.
Without the parentheses, `2 * 3` evaluates first to `6`, then `1 + 6` becomes
`7`.

</td>
</tr>

<tr>
<td>

`^`

</td>
<td>

`myvar ^ 2`

</td>
<td>

The exponent operator will work only on integers and will not accept strings.
Be careful not to exceed the positive integer limit of 2^31-1, or this will
return the negative integer limit.

</td>
</tr>

<tr>
<td>

`* \`

</td>
<td>

`myvar * 4`

`myvar * 3 \ 2`

</td>
<td>

Multiply `*` and Divide `\` will work only on integers and will not accept
strings.

**Note**: Backslash `\` is also the JSON escape character, so in typical
Content Patcher use you will need to type two backslashes `\\` to represent
one, just like in vanilla's `quickQuestion`. The parser will also accept `/`,
but in an event context I'm not sure it's possible to provide one, since that
breaks to a new event command.

**Reminder**: floating-point is not supported, so `\` will drop any decimal
portion of its result. `9 \ 2` yields `4`.

</td>
</tr>

<tr>
<td>

`+ -`

</td>
<td>

`myvar + 4`

`-1 * (myvar - 1)`

</td>
<td>

Add `+` and Subtract `-` will work only on integers and will not accept
strings. If you wish to add strings together, use Concatenate `.`.

In addition to binary operation, `-` will work to negate an integer literal
(so `-50` will behave as expected). But at this time, it will not work in
this way on variable names, strings, or anything else, so you may need to
be more explicit to avoid parse errors (e.g. `-1 * myvar` instead of `-myvar`).

</td>
</tr>

<tr>
<td>

`= !=`

</td>
<td>

`myvar = 4`

`(myvar + 1) != 6`

</td>
<td>

Compare two values for equality (`=`) or inequality (`!=`). This accepts
strings or integers (and is a lexical comparison, even for integers, so e.g.
`1 = '1'` will evaluate to `true`).

**Note for programmers**: You can use double-equals `==` for equality if you
want. You're welcome.

</td>
</tr>

<tr>
<td>

`< <= > >=`

</td>
<td>

`myvar < 5`

`myvar >= anothervar`

</td>
<td>

Like `=` and `!=`, but for
less-than/less-than-or-equal/greater-than/greater-than-or-equal, respectively.

</td>
</tr>

<tr>
<td>

`.`

</td>
<td>

`myvar . 'foobar'`

`myvar . 23`

</td>
<td>

The Concatenate (`.`) operator smushes the string representations of the two
surrounding values together. This will happily convert numbers to strings, so
`1 . 'asdf'` yields `'1asdf'`, and likewise `myvar . 15` takes the contents of
`myvar` and appends the string `'15'` to it.

**Note**: this is the lowest-precedence operator, so deploy parentheses if
needed.

**Note**: if you try to use a decimal number (e.g. `3 * 1.5`), the parser will
interpret the dot as this operator. The previous example would yield `35`.

</td>
</tr>

</table>

For example, you could use:

`ichortower.ECC_VarSet myvalue 4`

... which would set a variable called "myvalue" to the value 4. Or:

`ichortower.ECC_VarSet myvalue myvalue + 1`

... to increment its current value. You can combine a lot of stuff:

`ichortower.ECC_VarSet myvalue (1+4) * (12-5) . ' points'`

... which does the math as you would expect (5 \* 7), then concatenates it
with the text to generate the string "35 points".


### `VAR_QUERY`

`ichortower.ECC_VAR_QUERY <value|expression>`

This game state query uses the same expression parsing as `VarSet`, above, but
instead of storing the result into a named variable, it evaluates the
expression and returns true or false: false if it yields the string "false"
(case-insensitive) or the value 0, and true otherwise. Note that this means
that an empty string `""` and maybe some other unintuitive results will
evaluate to true.

This is intended for use with `If`/`ElseIf`/`Else`/`EndIf`. You can use it in
other contexts if you like, but I doubt that it is useful to do so: outside of
an event, you won't be able to access any variables.


## Vanilla Command Notes

There are some vanilla commands which don't fully work with streams. The known
problems are documented here, so you can be aware of them.

### `emote`

When not passing the optional `true` to avoid blocking, this is hardcoded to
advance the main command list when the emote expires. In a stream, you should
always use the optional `true` and then `StreamPause` if you need to block.

### `pause`

This command uses a global field (`Game1.pauseTime`) which is hardcoded to
advance the main command list when it expires, so it cannot be used in any
other stream. Use `precisePause` or `StreamPause` instead.

### `quickQuestion`

In order to execute the scripts, this command injects them hardcodedly into
the main command list, so I would not try using it in a stream if I were you.

### `speak`

To proceed, this command relies on the `DialogueBox` class, which is hardcoded
to advance the main command list when it closes. As a result, this command
cannot be used in any other stream. There is no replacement at this time.

### `speed`

When used with NPC actors, this command behaves as expected in any stream.
When used with a farmer, the speed change is local to the stream, and movements
in other streams will not see the value.

