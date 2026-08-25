import test from "node:test";
import assert from "node:assert/strict";
import { TerrariumModel, Side, Kind, Plane, chooseBotMove } from "../docs/js/engine.js";

const tacticalPiece = (id, side, kind, x, y, z = 0) =>
  ({ id, side, kind, position:{ x, y, z }, hasMoved:true });

function tacticalPosition(pieces, turn = Side.BLACK) {
  const game = new TerrariumModel(false);
  game.solids = new Uint8Array(8 * 8 * 16);
  game.pieces = pieces;
  game.turn = turn; game.plane = Plane.XY; game.winner = null;
  game.selectedId = null; game.enPassantPawnId = null; game.enPassantTarget = null;
  game.pendingPromotionPieceId = null; game.nextId = 100;
  game.history = []; game.lastFalls = []; game.message = "";
  return game;
}

test("creates the desktop game's starting position", () => {
  const game = new TerrariumModel();
  assert.equal(game.pieces.length, 32);
  assert.equal(game.pieces.filter(piece => piece.side === Side.WHITE).length, 16);
  assert.equal(game.surfaceZ(3, 3), 7);
  assert.equal(game.turn, Side.WHITE);
});

test("a starting pawn has one- and two-square XY moves", () => {
  const game = new TerrariumModel();
  const pawn = game.pieces.find(piece => piece.side === Side.WHITE && piece.kind === Kind.PAWN && piece.position.x === 4);
  game.select(pawn.id);
  assert.deepEqual(game.legalMoves().map(p => [p.x, p.y, p.z]), [[4, 2, 8], [4, 3, 8]]);
});

test("moves switch turns and undo restores the complete position", () => {
  const game = new TerrariumModel();
  const pawn = game.pieces.find(piece => piece.side === Side.WHITE && piece.kind === Kind.PAWN && piece.position.x === 4);
  game.select(pawn.id);
  assert.equal(game.tryMove({ x:4, y:3, z:8 }), true);
  assert.equal(game.turn, Side.BLACK);
  assert.equal(game.pieceAt({ x:4, y:3, z:8 })?.id, pawn.id);
  assert.equal(game.undo(), true);
  assert.equal(game.turn, Side.WHITE);
  assert.equal(game.pieceAt({ x:4, y:1, z:8 })?.id, pawn.id);
});

test("movement plane rotates knight offsets into XZ", () => {
  const game = new TerrariumModel();
  const knight = game.pieces.find(piece => piece.side === Side.WHITE && piece.kind === Kind.KNIGHT && piece.position.x === 1);
  game.setPlane(Plane.XZ);
  const moves = game.legalMoves(knight);
  assert.ok(moves.some(p => p.y === knight.position.y && p.z !== knight.position.z));
  assert.ok(moves.every(p => p.y === knight.position.y));
});

test("a pawn captures diagonally upward and downward on YZ without a wall latch", () => {
  const game = new TerrariumModel(false);
  game.solids = new Uint8Array(8 * 8 * 16);
  game.pieces = [
    { id:1, side:Side.WHITE, kind:Kind.PAWN, position:{x:3,y:3,z:5}, hasMoved:true },
    { id:2, side:Side.BLACK, kind:Kind.ROOK, position:{x:3,y:4,z:4}, hasMoved:true },
    { id:3, side:Side.BLACK, kind:Kind.BISHOP, position:{x:3,y:4,z:6}, hasMoved:true },
  ];
  game.turn=Side.WHITE; game.plane=Plane.YZ; game.winner=null; game.selectedId=null;
  game.history=[]; game.lastFalls=[];

  const pawn = game.pieces[0];
  assert.equal(game.isWallLatched(pawn.position), false);
  assert.deepEqual(
    game.legalMoves(pawn).filter(move => move.y === 4).map(move => move.z).sort((a,b) => a-b),
    [4, 6],
  );

  for (const [z, capturedId] of [[4, 2], [6, 3]]) {
    const capture = game.cloneForSimulation();
    capture.setPlane(Plane.YZ);
    assert.equal(capture.select(1), true);
    assert.equal(capture.tryMove({x:3,y:4,z}), true);
    assert.equal(capture.pieces.some(piece => piece.id === capturedId), false);
  }

  game.setPlane(Plane.XZ);
  assert.ok(game.legalMoves(pawn).every(move => move.y === pawn.position.y));
});

test("a wall-latched pawn can hop upward and settle back safely", () => {
  const game = new TerrariumModel(false);
  game.solids = new Uint8Array(8 * 8 * 16);
  game.setSolid(3, 3, 4, true);
  game.setSolid(4, 3, 5, true);
  game.pieces = [
    { id:1, side:Side.WHITE, kind:Kind.PAWN, position:{x:3,y:3,z:5}, hasMoved:true },
  ];
  game.turn=Side.WHITE; game.plane=Plane.XZ; game.winner=null; game.selectedId=null;
  game.history=[]; game.lastFalls=[];

  const hop = {x:3,y:3,z:6};
  assert.ok(game.legalMoves(game.pieces[0]).some(move => move.x===hop.x && move.y===hop.y && move.z===hop.z));
  assert.equal(game.select(1), true);
  assert.equal(game.tryMove(hop), true);
  assert.deepEqual(game.pieces[0].position, {x:3,y:3,z:5});
  assert.ok(game.lastFalls.some(fall => fall.pieceId===1 && fall.from.z===6 && fall.to.z===5));
});

test("moving the bottom piece carries its contiguous tower", () => {
  const game = new TerrariumModel(false);
  game.solids = new Uint8Array(8 * 8 * 16);
  game.setSolid(3, 1, 4, true);
  game.pieces = [
    { id:1, side:Side.WHITE, kind:Kind.ROOK, position:{x:1,y:1,z:5}, hasMoved:true },
    { id:2, side:Side.WHITE, kind:Kind.KNIGHT, position:{x:1,y:1,z:6}, hasMoved:true },
    { id:3, side:Side.BLACK, kind:Kind.BISHOP, position:{x:1,y:1,z:7}, hasMoved:true },
  ];
  game.turn=Side.WHITE; game.plane=Plane.XY; game.winner=null; game.selectedId=null;
  game.history=[]; game.lastFalls=[];

  assert.equal(game.select(1), true);
  assert.equal(game.tryMove({x:3,y:1,z:5}), true);
  assert.deepEqual(
    game.pieces.sort((a,b) => a.position.z-b.position.z).map(piece => piece.position),
    [{x:3,y:1,z:5}, {x:3,y:1,z:6}, {x:3,y:1,z:7}],
  );
});

test("a tower base can capture beneath another tower and knocks off blocked members", () => {
  const game = new TerrariumModel(false);
  game.solids = new Uint8Array(8 * 8 * 16);
  game.pieces = [
    { id:1, side:Side.WHITE, kind:Kind.ROOK, position:{x:0,y:0,z:0}, hasMoved:true },
    // Obstruction is compared with the carried member, not the moving base.
    { id:2, side:Side.BLACK, kind:Kind.PAWN, position:{x:0,y:0,z:1}, hasMoved:true },
    { id:3, side:Side.WHITE, kind:Kind.BISHOP, position:{x:0,y:0,z:2}, hasMoved:true },
    { id:4, side:Side.BLACK, kind:Kind.KNIGHT, position:{x:3,y:0,z:0}, hasMoved:true },
    { id:5, side:Side.BLACK, kind:Kind.QUEEN, position:{x:3,y:0,z:1}, hasMoved:true },
    { id:6, side:Side.WHITE, kind:Kind.KING, position:{x:7,y:0,z:0}, hasMoved:true },
    { id:7, side:Side.BLACK, kind:Kind.KING, position:{x:7,y:7,z:0}, hasMoved:true },
  ];
  game.turn=Side.WHITE; game.plane=Plane.XY; game.winner=null; game.selectedId=null;
  game.enPassantPawnId=null; game.enPassantTarget=null; game.pendingPromotionPieceId=null;
  game.nextId=8; game.history=[]; game.lastFalls=[]; game.message="";

  const target = {x:3,y:0,z:0};
  assert.ok(game.legalMoves(game.pieces[0]).some(move =>
    move.x===target.x && move.y===target.y && move.z===target.z));
  assert.equal(game.select(1), true);
  assert.equal(game.tryMove(target), true);

  assert.equal(game.pieces.some(piece => piece.id===4), false, "captured base survives");
  assert.deepEqual(game.pieces.find(piece => piece.id===1)?.position, target);
  assert.deepEqual(game.pieces.find(piece => piece.id===5)?.position, {x:3,y:0,z:1});
  assert.deepEqual(game.pieces.find(piece => piece.id===2)?.position, {x:2,y:0,z:0});
  assert.deepEqual(game.pieces.find(piece => piece.id===3)?.position, {x:2,y:0,z:1});
  assert.match(game.message, /obstruction knocked 2 tower piece/i);
});

test("a carried piece captures an opposing piece above the captured tower base", () => {
  const game = tacticalPosition([
    tacticalPiece(1, Side.WHITE, Kind.ROOK, 0, 0),
    tacticalPiece(2, Side.WHITE, Kind.PAWN, 0, 0, 1),
    tacticalPiece(3, Side.BLACK, Kind.KNIGHT, 3, 0),
    tacticalPiece(4, Side.BLACK, Kind.QUEEN, 3, 0, 1),
    tacticalPiece(5, Side.WHITE, Kind.KING, 7, 0),
    tacticalPiece(6, Side.BLACK, Kind.KING, 7, 7),
  ], Side.WHITE);

  const target = {x:3,y:0,z:0};
  assert.ok(game.legalMoves(game.pieces[0]).some(move =>
    move.x===target.x && move.y===target.y && move.z===target.z));
  game.select(1);
  assert.equal(game.tryMove(target), true);

  assert.equal(game.pieces.some(piece => piece.id===3 || piece.id===4), false);
  assert.deepEqual(game.pieces.find(piece => piece.id===1)?.position, target);
  assert.deepEqual(game.pieces.find(piece => piece.id===2)?.position, {x:3,y:0,z:1});
  assert.match(game.message, /Carried White Pawn captured Black Queen/);
});

test("capturing the king ends the game", () => {
  const game = new TerrariumModel(false);
  game.solids = new Uint8Array(8 * 8 * 16);
  game.pieces = [
    { id:1, side:Side.WHITE, kind:Kind.ROOK, position:{x:0,y:0,z:0}, hasMoved:false },
    { id:2, side:Side.WHITE, kind:Kind.KING, position:{x:7,y:0,z:0}, hasMoved:false },
    { id:3, side:Side.BLACK, kind:Kind.KING, position:{x:0,y:4,z:0}, hasMoved:false },
  ];
  game.turn=Side.WHITE; game.plane=Plane.XY; game.winner=null; game.selectedId=null; game.history=[]; game.lastFalls=[];
  game.select(1);
  assert.equal(game.tryMove({x:0,y:4,z:0}), true);
  assert.equal(game.winner, Side.WHITE);
});

test("bot checks every opponent reply for mate even when evaluation is sampled", () => {
  const game = new TerrariumModel(false);
  game.solids = new Uint8Array(8 * 8 * 16);
  game.pieces = [
    { id:1, side:Side.WHITE, kind:Kind.ROOK, position:{x:0,y:0,z:0}, hasMoved:false },
    { id:2, side:Side.WHITE, kind:Kind.KING, position:{x:7,y:7,z:0}, hasMoved:false },
    { id:3, side:Side.WHITE, kind:Kind.QUEEN, position:{x:7,y:1,z:0}, hasMoved:false },
    { id:4, side:Side.BLACK, kind:Kind.KING, position:{x:0,y:4,z:0}, hasMoved:false },
    { id:5, side:Side.BLACK, kind:Kind.QUEEN, position:{x:6,y:1,z:0}, hasMoved:false },
  ];
  game.turn=Side.BLACK; game.plane=Plane.XY; game.winner=null; game.selectedId=null;
  game.enPassantPawnId=null; game.enPassantTarget=null; game.pendingPromotionPieceId=null;
  game.nextId=6; game.history=[]; game.lastFalls=[]; game.message="";

  const move = chooseBotMove(game, 1);
  assert.ok(move);

  const after = game.cloneForSimulation();
  after.setPlane(move.plane);
  assert.equal(after.select(move.pieceId), true);
  assert.equal(after.tryMove(move.target), true);

  for (const plane of Object.values(Plane)) {
    after.setPlane(plane);
    for (const piece of after.pieces.filter(piece => piece.side === Side.WHITE)) {
      for (const target of after.legalMoves(piece)) {
        const reply = after.cloneForSimulation();
        reply.setPlane(plane);
        reply.select(piece.id);
        reply.tryMove(target);
        assert.notEqual(reply.winner, Side.WHITE, "bot allowed an opponent mate in one");
      }
    }
  }
});

test("bot preserves a threatened queen instead of accepting an unfavorable trade", () => {
  const game = tacticalPosition([
    tacticalPiece(1, Side.BLACK, Kind.KING, 7, 7),
    tacticalPiece(2, Side.WHITE, Kind.KING, 7, 0),
    tacticalPiece(3, Side.BLACK, Kind.QUEEN, 3, 3),
    tacticalPiece(4, Side.WHITE, Kind.ROOK, 2, 0),
    tacticalPiece(5, Side.WHITE, Kind.PAWN, 2, 2),
  ]);

  // The pawn attacks the queen and is protected by the rook. Direct captures
  // must be checked even with the smallest browser reply-sampling budget.
  const move = chooseBotMove(game, 1);
  assert.equal(move?.pieceId, 3);
  assert.notDeepEqual(move?.target, { x:2, y:2, z:0 });
});

test("bot move selection supports White for Bot vs Bot mode", () => {
  const game = tacticalPosition([
    tacticalPiece(1, Side.WHITE, Kind.KING, 7, 0),
    tacticalPiece(2, Side.BLACK, Kind.KING, 7, 7),
    tacticalPiece(3, Side.WHITE, Kind.ROOK, 0, 0),
    tacticalPiece(4, Side.BLACK, Kind.PAWN, 0, 3),
  ], Side.WHITE);

  assert.ok(chooseBotMove(game, 4));
});

test("the Minesweeper addon is opt-in and builds a safe-top clue field", () => {
  const game = new TerrariumModel();
  assert.equal(game.minesweeperEnabled, false);
  assert.equal(game.mines.some(Boolean), false);
  assert.equal(game.revealedClues.size, 0);

  game.setMinesweeperEnabled(true);
  assert.equal(game.minesweeperEnabled, true);
  assert.ok(game.mines.some(Boolean));
  for (let x=0;x<8;x++) for (let y=0;y<8;y++) {
    assert.equal(game.mineAt(x,y,7), false, "top terrain layer must be mine-free");
    assert.equal(game.solidAt(x,y,7), true, "caverns must not breach the starting surface");
  }
  assert.ok([...game.revealedClues].some(cell => cell.startsWith("-1,")), "outer clue shell is revealed");
  let carved=0;for(let x=0;x<8;x++)for(let y=0;y<8;y++)for(let z=0;z<7;z++)if(!game.solidAt(x,y,z))carved++;
  assert.ok(carved>0, "zero regions carve pregame caverns");
  assert.equal(game.resolveTerrainGravity([]),false,"pregame cavern ceilings stay stable until nearby disturbance");
});

test("promoted movement is true 3D and independent of movement plane", () => {
  const game = tacticalPosition([
    {...tacticalPiece(1, Side.WHITE, Kind.BISHOP, 3, 3, 3), promoted:true},
    tacticalPiece(2, Side.WHITE, Kind.KING, 7, 0),
    tacticalPiece(3, Side.BLACK, Kind.KING, 7, 7),
  ], Side.WHITE);
  const destination={x:4,y:4,z:4};
  for (const plane of Object.values(Plane)) {
    game.setPlane(plane);
    assert.ok(game.legalMoves(game.pieces[0]).some(move =>
      move.x===destination.x&&move.y===destination.y&&move.z===destination.z));
  }
  game.pieces[0].promoted=false;
  game.setPlane(Plane.XY);
  assert.equal(game.legalMoves(game.pieces[0]).some(move => move.z!==3), false);
});

test("Trishop is a pawn-only underpromotion with (1,1,1) sliding", () => {
  const game = tacticalPosition([
    tacticalPiece(1, Side.WHITE, Kind.PAWN, 2, 7),
    tacticalPiece(2, Side.WHITE, Kind.KING, 7, 0),
    tacticalPiece(3, Side.BLACK, Kind.KING, 7, 7),
  ], Side.WHITE);
  game.pendingPromotionPieceId=1;
  assert.equal(game.promote(Kind.TRISHOP), true);
  assert.equal(game.pieces[0].promoted, true);
  assert.equal(game.pieces[0].kind, Kind.TRISHOP);
  assert.ok(game.legalMoves(game.pieces[0]).some(move => move.x===3&&move.y===6&&move.z===1));
});

test("scouting reveals a selected movement component without spending the turn", () => {
  const game = new TerrariumModel(); game.setMinesweeperEnabled(true);
  const rook=game.pieces.find(piece=>piece.side===Side.WHITE&&piece.kind===Kind.ROOK);
  game.select(rook.id); const before=game.revealedClues.size;
  assert.equal(game.scout("orthogonal"), true);
  assert.equal(game.turn, Side.WHITE);
  assert.equal(game.selectedId, rook.id);
  assert.ok(game.revealedClues.size>=before);
  assert.match(game.message,/Scouting is free/);
});

test("excavating a mine blasts pieces in 3x3x3 without collateral terrain damage", () => {
  const game=tacticalPosition([
    tacticalPiece(1,Side.WHITE,Kind.ROOK,3,3,4),
    tacticalPiece(2,Side.BLACK,Kind.KNIGHT,4,4,3),
    tacticalPiece(3,Side.BLACK,Kind.KING,7,7),
  ],Side.WHITE);
  game.minesweeperEnabled=true;game.mines=new Uint8Array(8*8*16);game.revealedClues=new Set();
  game.setSolid(3,3,3,true);game.setSolid(2,3,3,true);game.mines[game.solidIndex(3,3,3)]=1;
  const events=[];game.removeTerrain({x:3,y:3,z:3},events);
  assert.equal(game.pieces.some(piece=>piece.id===1||piece.id===2),false);
  assert.equal(game.solidAt(2,3,3),true,"blast must not remove neighboring terrain");
  assert.equal(game.mineAt(3,3,3),false);
  assert.match(events.join(" "),/Mine detonated/);
});
