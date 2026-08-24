# Gravity Chess

A local single-player isometric chess prototype built with MonoGame. The board is an
8×8×16 play space: an 8×8×8 destructible cube occupies the lower half, and the
standard chess position begins on its top face at Z8.

## Play

For a packaged release, extract the entire ZIP and double-click
`GravityChess.exe`. Keep the `Content` folder beside the executable.

To run from source:

```powershell
dotnet run
```

Click a piece, choose its movement plane, then click a highlighted destination.
You play White; Black is controlled by a bot that searches one reply deep,
evaluates material and piece activity, takes mate in one, and avoids allowing an
opponent mate in one whenever a safe move exists.
The desktop window is freely resizable; the game preserves its aspect ratio and
keeps mouse picking aligned at every size.
The game starts in borderless fullscreen at the desktop's current resolution.

Move colors predict the landing result: teal is an ordinary safe move, yellow
is a direct excavation or a survivable crater-producing impact, and red means
the moving piece cannot survive the fall. A 4+ cell fall is yellow when another
piece will break it; otherwise it is red.

- `1`, `2`, `3`: switch between XY, XZ, and YZ movement planes
- `Space`: cycle the movement plane
- Left-drag sideways over the world: orbit continuously around the board
- Left-drag vertically: adjust camera elevation between 30 and 60 degrees
- Up / Down arrows: translate the camera along world Z
- `Q`, `E`: rotate the camera by 90 degrees
- Middle mouse button: toggle single-layer focus
- Mouse wheel while layer focus is active: select Z0–Z15; every other layer
  of terrain becomes fully transparent. Chess pieces remain fully visible on
  every layer, and legal destination highlights remain visible at every depth.
- `Tab` / `Shift+Tab`: cycle through the current player's pieces
- `U`: undo
- `R`: restart
- `H`: rules overlay
- Right-click: deselect
- `Esc`: close rules or quit

## Implemented rules

- Standard pieces begin in the standard chess arrangement.
- Rook, Bishop, Queen, King, and Knight movement rotates into the chosen plane.
- Pawns move normally on XY. XZ and YZ are wall actions and are available only
  while a pawn is latched beside solid terrain. On YZ, a pawn advances one rank
  while climbing or descending one Z cell, and may capture an enemy on either
  of those diagonals. Empty climb destinations must remain latched or supported.
- A wall-latched pawn may hop straight up one cell on XZ or YZ to consume its
  turn, then settle safely. On its first move it may hop two cells instead; the
  resulting two-cell drop creates a crater. It may also excavate solid terrain
  directly above itself without moving.
- En passant is available only on the immediate reply to a two-square pawn
  move and captures the passed pawn at its actual elevation.
- An unmoved King and Rook may castle on XY when they remain aligned on their
  home rank and every cell between them is empty. Check restrictions are not
  applied because this prototype does not enforce check.
- A pawn reaching the enemy's far Y rank promotes at any elevation. Play pauses
  after landing so White can choose Queen, Rook, Bishop, or Knight; the bot
  chooses automatically.
- Non-pawns may excavate reachable solid cells on their own Z level or above.
  Rooks, Bishops, and Queens stop one cell before the excavated cell; Knights
  excavate without moving; Kings enter the newly opened cell immediately.
- Unsupported drops of one cell are safe and allow pieces to form towers.
- A drop of two or more cells is an impact: the first terrain cell shatters or
  the first piece is squashed.
- A piece falling four or more cells also perishes unless another piece breaks
  its fall.
- Moving a piece carries every contiguous piece above it as a tower. If that
  transported tower falls two or more cells onto terrain, its moved bottom
  piece is squashed by the pieces above it and the impact leaves a crater.
- Sliding Rooks, Bishops, and Queens sweep their carried towers through the
  whole path. If terrain overhangs the path, the first colliding member and all
  members above it stop at the last clear cell, detach, and fall normally.
  Knights jump and carry the tower upward from its base only until the first
  member whose destination intersects terrain. That member and the entire
  section above it stay behind and then obey gravity.
- When a piece drops two or more cells onto a stationary tower, it lands on top
  of that tower. The stationary tower's bottom piece is squashed into a new
  crater, the falling tower survives, and the combined survivors compress into
  one stack.
- When a flat solid 3×3 shelf has no solid cells anywhere in the 3×3 layer
  immediately beneath it, its center cell falls. Falling terrain squashes the
  first piece in its path, including a King.
- Capturing or squashing a King wins. Check and checkmate constraints are not
  enforced in this prototype.

The world renderer uses true 3D geometry rather than screen-space isometric
tiles. Its orthographic camera begins side-aligned at 45 degrees, supports
continuous orbit, clamps elevation between 30 and 60 degrees, and translates
along world Z with the arrow keys.
It uses depth-tested cube faces, sculpted piece meshes, and true single-layer
isolation. Piece falls animate with acceleration
and a landing bounce; captures and crater changes are committed visually only
at impact, and pieces that perish fade during their fall.
