import { TerrariumModel, chooseBotMove } from "./engine.js";

self.addEventListener("message", event => {
  const { version, state } = event.data;
  const model = new TerrariumModel(false);
  model.solids = new Uint8Array(state.solids);
  model.pieces = state.pieces;
  model.turn = state.turn;
  model.plane = state.plane;
  model.winner = state.winner;
  model.message = state.message;
  model.selectedId = state.selectedId;
  model.enPassantPawnId = state.enPassantPawnId;
  model.enPassantTarget = state.enPassantTarget;
  model.pendingPromotionPieceId = state.pendingPromotionPieceId;
  model.nextId = state.nextId;
  model.history = [];
  model.lastFalls = [];
  self.postMessage({ version, move: chooseBotMove(model, 42) });
});
