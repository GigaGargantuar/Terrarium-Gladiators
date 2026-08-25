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
