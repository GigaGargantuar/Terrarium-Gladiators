import { TerrariumModel, chooseBotMove } from "./engine.js";

self.addEventListener("message", event => {
  const { version, state } = event.data;
  const model = new TerrariumModel(false);
  model.solids = new Uint8Array(state.solids);
  model.mines = new Uint8Array(8 * 8 * 16);
  model.botSafeMarks = new Set(state.botSafeMarks ?? []);
  model.botMineFlags = new Set(state.botMineFlags ?? []);
  for (const encoded of model.botMineFlags) {
    const [x, y, z] = encoded.split(",").map(Number);
    model.mines[model.solidIndex(x, y, z)] = 1;
  }
  model.revealedClues = new Set(state.revealedClues ?? []);
  model.cavernProtected = new Set(state.cavernProtected ?? []);
  model.disturbedTerrain = new Set(state.disturbedTerrain ?? []);
  model.minesweeperEnabled = !!state.minesweeperEnabled;
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
