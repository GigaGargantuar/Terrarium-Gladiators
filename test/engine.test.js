import test from "node:test";
import assert from "node:assert/strict";
import { TerrariumModel, Side, Kind, Plane } from "../docs/js/engine.js";

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
