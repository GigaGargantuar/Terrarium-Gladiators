# Terrarium Gladiators

A local single-player isometric chess prototype built with MonoGame. The board is an
8×8×16 play space: an 8×8×8 destructible cube occupies the lower half, and the
standard chess position begins on its top face at Z8.

## Browser / GitHub Pages edition

The browser port is a dependency-free static site in [`docs/`](docs/). It keeps the
desktop game's three movement planes, destructible terrain, gravity and tower rules,
promotion, castling, en passant, undo behavior, and Black bot. It also adds responsive
mouse, touch, and keyboard controls.

To preview it locally, serve the repository root with any static HTTP server and open
`/docs/`. For example, with Python installed:

```powershell
python -m http.server 8080
```

Then visit `http://localhost:8080/docs/`. Opening `index.html` directly is not
recommended because browser module security rules differ for `file://` URLs.

Deployment is configured in [`.github/workflows/pages.yml`](.github/workflows/pages.yml).

**Required one-time setup:** before the first deployment, open the repository's
**Settings → Pages → Build and deployment** and set **Source** to **GitHub Actions**.
GitHub returns a `Get Pages site failed: Not Found` error until this is enabled.
Afterward, every push to `main` tests the rules engine and publishes the repository
root; its landing page redirects into `docs/`. All site links are relative, so project Pages URLs such as
`https://owner.github.io/repository/` work without a hard-coded base path.

Run the browser rule tests with:

```powershell
npm test
```

## Play

For a packaged release, extract the entire ZIP and double-click
`TerrariumGladiators.exe`. Keep the `Content` folder beside the executable.

To run from source:

```powershell
dotnet run
```

Click a piece, choose its movement plane, then click a highlighted destination.
Choose same-device PvP, Player vs Bot (White player), or Bot vs Bot from the
mode selector. The bot searches one reply deep, evaluates material and piece
activity, takes mate in one, and avoids allowing an opponent mate in one
whenever a safe move exists. It always analyzes direct capture replies and
strongly penalizes lines that lose more of its own material than they take, so
it rescues threatened pieces and declines unfavorable trades.
The desktop window is freely resizable; the game preserves its aspect ratio and
keeps mouse picking aligned at every size.
The game starts in borderless fullscreen at the desktop's current resolution.

Move colors predict the landing result: teal is an ordinary safe move, yellow
is a direct excavation or a survivable crater-producing impact, and red means
the moving piece cannot survive the fall. A 4+ cell fall is yellow when another
piece will break it; otherwise it is red.

- `1`, `2`, `3`: switch between XY, XZ, and YZ movement planes
- `Space`: cycle the movement plane
- `M`: toggle the optional Minesweeper addon (restarts the match)
- `X`: cycle the selected piece's scouting pattern; `S`: scan for free
- Left-drag sideways over the world: orbit continuously around the board
- Left-drag vertically: orbit through the full −90° to +90° vertical arc
- Up / Down arrows: translate the camera along world Z
- `Q`, `E`: rotate the camera by 90 degrees
- Middle mouse button: toggle two-layer focus
- Mouse wheel outside layer focus: zoom the perspective camera in or out
- Mouse wheel while layer focus is active: select adjacent windows from Z0–Z1
  through Z14–Z15; every other terrain layer is hidden. Chess pieces remain
  fully visible on every layer, and legal destinations remain visible.
- `Tab` / `Shift+Tab`: cycle through the current player's pieces
- `U`: undo
- `R`: restart
- `H`: rules overlay
- Right-click: deselect
- `Esc`: close rules or quit

## Implemented rules

- Standard pieces begin in the standard chess arrangement.
- Rook, Bishop, Queen, King, and Knight movement rotates into the chosen plane.
- Pawns move normally on XY. On either XZ or YZ, every pawn may hop straight up
  one cell without a wall latch; an unmoved pawn may hop two cells. On YZ, a
  wall-latched pawn can also advance one rank while climbing or descending one
  Z cell and capture an enemy on either diagonal. Empty climb destinations must
  remain latched or supported. Upward excavation still requires a wall latch.
- En passant is available only on the immediate reply to a two-square pawn
  move and captures the passed pawn at its actual elevation.
- An unmoved King and Rook may castle on XY when they remain aligned on their
  home rank and every cell between them is empty. Check restrictions are not
  applied because this prototype does not enforce check.
- A pawn reaching the enemy's far Y rank promotes at any elevation. Play pauses
  after landing so White can choose Queen, Rook, Bishop, Knight, or the pawn-only
  `(1,1,1)`-sliding Trishop; the bot chooses automatically. A pawn-origin piece
  must then return to its own home Y rank before its added true-3D movement
  awakens.
- Any piece that reaches the enemy back rank awakens true-3D movement that is
  always available regardless of the selected plane. Promoted Bishops, Rooks,
  and Queens add Trishop rays; Kings add one-step space diagonals; Knights add
  every `(1,1,2)` permutation to their ordinary `(0,1,2)` jumps.
- Empty-cell destinations from true-3D movement are shown as small cubes centered
  in their cells; plane-based destinations retain their oriented flat markers.
- The opt-in Minesweeper addon generates a fresh randomized minefield at 15%
  density for every match below a guaranteed-safe top terrain layer. Its one-cell clue shell
  expands the 8×8×16 play volume to 10×10×18.
  Pregame zero-clue flood fills carve stable cavern systems beneath the surface.
- A selected piece can freely scout with one chosen component of its movement
  pattern. Scouting reveals number clues without consuming the turn.
- Standing above a mine is safe. Excavating or impact-cratering its terrain cell
  detonates it, destroying pieces in a 3×3×3 volume while leaving neighboring
  terrain intact.
- A falling piece that collides with a mine is destroyed at that contact point;
  its projectile path ends immediately and cannot continue through the blast.
- Non-pawns may excavate reachable solid cells on their own Z level or above.
  Rooks, Bishops, and Queens stop one cell before the excavated cell; Knights
  excavate without moving; Kings enter the newly opened cell immediately.
- Unsupported drops of one cell are safe and allow pieces to form towers.
- A drop of two or more cells is an impact: the first terrain cell shatters or
  the first piece is squashed.
- If an impact punches through unsupported terrain, the destroyed cell becomes
  the start of a new fall segment. Distance and damage reset there, then apply
  normally to each later impact.
- Falling pieces behave as vertical projectiles: every collision is resolved at
  contact, then any resulting cave-in or terrain fall settles before the next
  projectile segment is traced. Multi-impact falls therefore update the visible
  environment several times instead of committing all damage at the end.
- A piece falling four or more cells also perishes unless another piece breaks
  its fall.
- Moving a piece carries every contiguous piece above it as a tower. If that
  transported tower falls two or more cells onto terrain, its moved bottom
  piece is squashed by the pieces above it and the impact leaves a crater.
- Sliding Rooks, Bishops, and Queens sweep their carried towers through the
  whole path, where terrain or any non-carried piece blocks an intermediate
  step. At the landing cells above a captured tower base, a same-side piece
  obstructs the colliding carried member: that member and everything above it
  detach and fall normally. An opposing piece in that landing cell is captured.
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
tiles. Its perspective camera begins side-aligned at 45 degrees, supports
continuous horizontal orbit and a pole-safe 180-degree vertical orbit from
underneath to overhead, translates along world Z with the arrow keys, and zooms
with the mouse wheel outside layer focus. Mine clues are centered in their cells
and built from depth-tested cuboids whose local plane continuously faces the camera.
It uses depth-tested cube faces, sculpted piece meshes, and true two-layer
isolation. Piece falls animate with acceleration
and a landing bounce; captures and crater changes are committed visually only
at impact, and pieces that perish fade during their fall.
