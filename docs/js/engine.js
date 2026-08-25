export const Side = Object.freeze({ WHITE: "White", BLACK: "Black" });
export const Kind = Object.freeze({
  PAWN: "Pawn", KNIGHT: "Knight", BISHOP: "Bishop",
  ROOK: "Rook", QUEEN: "Queen", KING: "King", TRISHOP: "Trishop",
});
export const Plane = Object.freeze({ XY: "XY", XZ: "XZ", YZ: "YZ" });
export const Outcome = Object.freeze({ SAFE: "Safe", CRATER: "CraterSurvived", EXCAVATION: "Excavation", FATAL: "Fatal" });

export const v = (x, y, z) => ({ x, y, z });
const add = (a, b) => v(a.x + b.x, a.y + b.y, a.z + b.z);
const sub = (a, b) => v(a.x - b.x, a.y - b.y, a.z - b.z);
const mul = (a, n) => v(a.x * n, a.y * n, a.z * n);
const eq = (a, b) => !!a && !!b && a.x === b.x && a.y === b.y && a.z === b.z;
const key = p => `${p.x},${p.y},${p.z}`;
const clonePiece = p => ({ id: p.id, side: p.side, kind: p.kind, position: { ...p.position }, hasMoved: p.hasMoved, promoted: !!p.promoted, pawnOrigin:!!p.pawnOrigin });
const other = side => side === Side.WHITE ? Side.BLACK : Side.WHITE;
const planes = [Plane.XY, Plane.XZ, Plane.YZ];

export class TerrariumModel {
  constructor(initialize = true, minesweeperEnabled = false) {
    this.solids = new Uint8Array(8 * 8 * 16);
    this.mines = new Uint8Array(8 * 8 * 16);
    this.revealedClues = new Set();
    this.safeMarks = new Set();
    this.mineFlags = new Set();
    this.botSafeMarks = new Set();
    this.botMineFlags = new Set();
    this.scanObservations = [];
    this.cavernProtected = new Set();
    this.disturbedTerrain = new Set();
    this.pieces = [];
    this.history = [];
    this.lastFalls = [];
    this.lastTerrainChanges = [];
    this.lastPieceRemovals = [];
    this.minesweeperEnabled = minesweeperEnabled;
    if (initialize) this.reset();
  }

  solidIndex(x, y, z) { return x * 128 + y * 16 + z; }
  solidAt(x, y, z) { return this.solids[this.solidIndex(x, y, z)] === 1; }
  setSolid(x, y, z, value) { this.solids[this.solidIndex(x, y, z)] = value ? 1 : 0; }
  mineAt(x, y, z) { return x>=0&&x<8&&y>=0&&y<8&&z>=0&&z<16&&this.mines?.[this.solidIndex(x,y,z)]===1; }

  reset(mineSeed=null) {
    this.solids = new Uint8Array(8 * 8 * 16);
    for (let x = 0; x < 8; x++) for (let y = 0; y < 8; y++) for (let z = 0; z < 8; z++) this.setSolid(x, y, z, true);
    if(this.minesweeperEnabled)this.generateMinefield(mineSeed??TerrariumModel.randomMineSeed());else{this.mines=new Uint8Array(8*8*16);this.revealedClues=new Set();this.cavernProtected=new Set();this.disturbedTerrain=new Set()}
    this.safeMarks=new Set();this.mineFlags=new Set();this.botSafeMarks=new Set();this.botMineFlags=new Set();this.scanObservations=[];
    this.pieces = [];
    this.nextId = 1;
    this.addArmy(Side.WHITE, 0, 1);
    this.addArmy(Side.BLACK, 7, 6);
    this.turn = Side.WHITE;
    this.plane = Plane.XY;
    this.winner = null;
    this.selectedId = null;
    this.enPassantPawnId = null;
    this.enPassantTarget = null;
    this.pendingPromotionPieceId = null;
    this.message = "White to move — select a piece, then a glowing cell.";
    this.lastFalls = [];
    this.lastTerrainChanges = [];
    this.lastPieceRemovals = [];
    this.history = [];
  }

  addArmy(side, homeY, pawnY) {
    const back = [Kind.ROOK, Kind.KNIGHT, Kind.BISHOP, Kind.QUEEN, Kind.KING, Kind.BISHOP, Kind.KNIGHT, Kind.ROOK];
    for (let x = 0; x < 8; x++) {
      this.pieces.push({ id: this.nextId++, side, kind: back[x], position: v(x, homeY, 8), hasMoved: false, promoted:false, pawnOrigin:false });
      this.pieces.push({ id: this.nextId++, side, kind: Kind.PAWN, position: v(x, pawnY, 8), hasMoved: false, promoted:false, pawnOrigin:false });
    }
  }

  static randomMineSeed(){let seed;if(globalThis.crypto?.getRandomValues){const value=new Uint32Array(1);globalThis.crypto.getRandomValues(value);seed=value[0]}else seed=(Date.now()^Math.floor(Math.random()*0x100000000))>>>0;if(seed===TerrariumModel.lastMineSeed)seed=(seed+1)>>>0;TerrariumModel.lastMineSeed=seed;return seed}

  generateMinefield(seed) {
    this.mines = new Uint8Array(8*8*16); this.revealedClues = new Set();this.cavernProtected=new Set();this.disturbedTerrain=new Set();
    let state=seed>>>0,random=()=>{state=(Math.imul(state,1664525)+1013904223)>>>0;return state/0x100000000};
    const eligible=[];for(let x=0;x<8;x++)for(let y=0;y<8;y++)for(let z=0;z<7;z++)eligible.push(this.solidIndex(x,y,z));
    for(let i=eligible.length-1;i>0;i--){const swap=Math.floor(random()*(i+1));[eligible[i],eligible[swap]]=[eligible[swap],eligible[i]]}
    const mineCount=Math.round(eligible.length*.2);for(let i=0;i<mineCount;i++)this.mines[eligible[i]]=1;
    // The one-cell shell makes the clue volume 10 x 10 x 18 (10 x 18 x 10
    // when written in the game's X/Z/Y display order).
    for(let x=-1;x<=8;x++)for(let y=-1;y<=8;y++)for(let z=-1;z<=16;z++)
      if(x<0||x>7||y<0||y>7||z<0||z>15)this.revealedClues.add(key(v(x,y,z)));
    this.carveZeroCaverns();
    for(let x=0;x<8;x++)for(let y=0;y<8;y++)for(let z=0;z<8;z++)if(this.solidAt(x,y,z))
      for(let dx=-1;dx<=1;dx++)for(let dy=-1;dy<=1;dy++)for(let dz=-1;dz<=1;dz++)if(!this.solidAt(x+dx,y+dy,z+dz)&&x+dx>=0&&x+dx<8&&y+dy>=0&&y+dy<8&&z+dz>=0&&z+dz<7)this.cavernProtected.add(key(v(x,y,z)));
  }

  clueAt(p) {
    if(p.x< -1||p.x>8||p.y< -1||p.y>8||p.z< -1||p.z>16||this.mineAt(p.x,p.y,p.z))return null;
    let count=0;for(let dx=-1;dx<=1;dx++)for(let dy=-1;dy<=1;dy++)for(let dz=-1;dz<=1;dz++)
      if((dx||dy||dz)&&this.mineAt(p.x+dx,p.y+dy,p.z+dz))count++;
    return count;
  }

  carveZeroCaverns() {
    const zeros=[];for(let x=0;x<8;x++)for(let y=0;y<8;y++)for(let z=0;z<7;z++)if(!this.mineAt(x,y,z)&&this.clueAt(v(x,y,z))===0)zeros.push(v(x,y,z));
    const zeroKeys=new Set(zeros.map(key)),visited=new Set();
    for(const start of zeros){if(visited.has(key(start)))continue;const queue=[start];visited.add(key(start));
      for(let i=0;i<queue.length;i++){const cell=queue[i];this.setSolid(cell.x,cell.y,cell.z,false);this.revealedClues.add(key(cell));
        for(let dx=-1;dx<=1;dx++)for(let dy=-1;dy<=1;dy++)for(let dz=-1;dz<=1;dz++){if(!(dx||dy||dz))continue;const n=add(cell,v(dx,dy,dz));if(n.x<0||n.x>7||n.y<0||n.y>7||n.z<0||n.z>=7||this.mineAt(n.x,n.y,n.z))continue;this.setSolid(n.x,n.y,n.z,false);this.revealedClues.add(key(n));const nk=key(n);if(zeroKeys.has(nk)&&!visited.has(nk)){visited.add(nk);queue.push(n)}}
      }
    }
  }

  cloneForSimulation() {
    const copy = new TerrariumModel(false);
    copy.solids = this.solids.slice();
    copy.mines = this.mines?.slice()??new Uint8Array(8*8*16);
    copy.revealedClues = new Set(this.revealedClues??[]);
    copy.safeMarks = new Set(this.safeMarks??[]);copy.mineFlags = new Set(this.mineFlags??[]);
    copy.botSafeMarks = new Set(this.botSafeMarks??[]);copy.botMineFlags = new Set(this.botMineFlags??[]);
    copy.scanObservations = (this.scanObservations??[]).map(observation=>({...observation,origin:{...observation.origin},cells:[...observation.cells]}));
    copy.cavernProtected = new Set(this.cavernProtected??[]);copy.disturbedTerrain=new Set(this.disturbedTerrain??[]);
    copy.pieces = this.pieces.map(clonePiece);
    copy.turn = this.turn; copy.plane = this.plane; copy.winner = this.winner;
    copy.message = this.message; copy.selectedId = this.selectedId;
    copy.enPassantPawnId = this.enPassantPawnId;
    copy.enPassantTarget = this.enPassantTarget && { ...this.enPassantTarget };
    copy.pendingPromotionPieceId = this.pendingPromotionPieceId;
    copy.minesweeperEnabled = this.minesweeperEnabled;
    copy.nextId = this.nextId; copy.history = []; copy.lastFalls = [];copy.lastTerrainChanges=[];copy.lastPieceRemovals=[];
    return copy;
  }

  cloneForBotSearch() {
    const copy=this.cloneForSimulation();copy.mines=new Uint8Array(8*8*16);
    for(const encoded of this.botMineFlags??[]){const[x,y,z]=encoded.split(",").map(Number);if(TerrariumModel.isInside(v(x,y,z))&&this.solidAt(x,y,z))copy.mines[copy.solidIndex(x,y,z)]=1}
    return copy;
  }

  get selected() { return this.pieces.find(p => p.id === this.selectedId) ?? null; }
  setPlane(plane) { this.plane = plane; this.message = `Movement plane rotated to ${plane}.`; }
  select(id) {
    const piece = this.pieces.find(p => p.id === id);
    if (!piece || this.winner) return false;
    if (piece.side !== this.turn) { this.message = `It is ${this.turn}'s turn.`; return false; }
    this.selectedId = id; this.message = `${piece.side} ${piece.kind} selected on ${this.plane}.`; return true;
  }
  clearSelection() { this.selectedId = null; }
  toggleCellMark(cell,mineFlag){if(!this.minesweeperEnabled||!TerrariumModel.isInside(cell)||this.pieceAt(cell))return false;const encoded=key(cell),selected=mineFlag?this.mineFlags:this.safeMarks,other=mineFlag?this.safeMarks:this.mineFlags;other.delete(encoded);const enabled=!selected.has(encoded);if(enabled)selected.add(encoded);else selected.delete(encoded);this.message=enabled?`${TerrariumModel.cellName(cell)} marked ${mineFlag?"as a suspected mine":"safe to reveal"}. Marking is free.`:`Marker removed from ${TerrariumModel.cellName(cell)}.`;return true}

  static isInside(p) { return p.x >= 0 && p.x < 8 && p.y >= 0 && p.y < 8 && p.z >= 0 && p.z < 16; }
  pieceAt(p) { return this.pieces.find(piece => eq(piece.position, p)) ?? null; }
  isSolid(p) { return TerrariumModel.isInside(p) && this.solidAt(p.x, p.y, p.z); }
  isEmpty(p) { return TerrariumModel.isInside(p) && !this.isSolid(p) && !this.pieceAt(p); }

  legalMoves(piece = this.selected) {
    if (!piece || this.winner) return [];
    const result = [];
    const [a, b] = this.planeAxes(this.plane);
    const tri=this.spaceDiagonalDirections();
    if (piece.kind === Kind.ROOK) this.addSliding(piece, result, [a, mul(a, -1), b, mul(b, -1),...(piece.promoted?tri:[])]);
    else if (piece.kind === Kind.BISHOP) this.addSliding(piece, result, [add(a,b), add(a,mul(b,-1)), add(mul(a,-1),b), add(mul(a,-1),mul(b,-1)),...(piece.promoted?tri:[])]);
    else if (piece.kind === Kind.QUEEN) this.addSliding(piece, result, [a,mul(a,-1),b,mul(b,-1),add(a,b),add(a,mul(b,-1)),add(mul(a,-1),b),add(mul(a,-1),mul(b,-1)),...(piece.promoted?tri:[])]);
    else if(piece.kind===Kind.TRISHOP)this.addSliding(piece,result,tri);
    else if (piece.kind === Kind.KING) {
      this.addStepping(piece, result, [a,mul(a,-1),b,mul(b,-1),add(a,b),add(a,mul(b,-1)),add(mul(a,-1),b),add(mul(a,-1),mul(b,-1)),...(piece.promoted?tri:[])]);
      this.addCastlingMoves(piece, result);
    } else if (piece.kind === Kind.KNIGHT) {
      this.addStepping(piece, result, [add(mul(a,2),b),add(mul(a,2),mul(b,-1)),add(mul(a,-2),b),add(mul(a,-2),mul(b,-1)),add(mul(b,2),a),add(mul(b,2),mul(a,-1)),add(mul(b,-2),a),add(mul(b,-2),mul(a,-1)),...(piece.promoted?this.spaceKnightOffsets():[])]);
    } else this.addPawnMoves(piece, result);
    const seen = new Set();
    return result.filter(target => { const k = key(target); if (seen.has(k)) return false; seen.add(k); return this.canTransportTower(piece, target); });
  }

  setMinesweeperEnabled(enabled,mineSeed=null){this.minesweeperEnabled=!!enabled;this.reset(mineSeed)}

  spaceDiagonalDirections(){const r=[];for(const x of [-1,1])for(const y of [-1,1])for(const z of [-1,1])r.push(v(x,y,z));return r}
  spaceKnightOffsets(){const r=[];for(let axis=0;axis<3;axis++)for(const two of [-2,2])for(const a of [-1,1])for(const b of [-1,1]){const q=[a,b];q.splice(axis,0,two);r.push(v(q[0],q[1],q[2]))}return r}

  scoutPatterns(piece=this.selected){if(!piece)return[];const patterns=[];
    if([Kind.ROOK,Kind.QUEEN,Kind.KING].includes(piece.kind))patterns.push("orthogonal");
    if([Kind.BISHOP,Kind.QUEEN,Kind.KING].includes(piece.kind))patterns.push("plane-diagonal");
    if(piece.kind===Kind.TRISHOP||piece.promoted&&[Kind.ROOK,Kind.BISHOP,Kind.QUEEN,Kind.KING].includes(piece.kind))patterns.push("space-diagonal");
    return patterns;
  }
  scanDirections(piece,pattern){
    if(pattern==="orthogonal")return[v(1,0,0),v(-1,0,0),v(0,1,0),v(0,-1,0),v(0,0,1),v(0,0,-1)];
    if(pattern==="plane-diagonal"){const directions=[];for(const x of[-1,0,1])for(const y of[-1,0,1])for(const z of[-1,0,1])if(Math.abs(x)+Math.abs(y)+Math.abs(z)===2)directions.push(v(x,y,z));return directions}
    return this.spaceDiagonalDirections();
  }
  scout(pattern){const piece=this.selected;if(!this.minesweeperEnabled||!piece||!this.scoutPatterns(piece).includes(pattern)||this.winner)return null;const cells=this.scanDirections(piece,pattern).map(direction=>add(piece.position,direction)).filter(TerrariumModel.isInside),encodedCells=[...new Set(cells.map(key))],mineCount=cells.filter(cell=>this.mineAt(cell.x,cell.y,cell.z)).length;
    this.scanObservations=(this.scanObservations??[]).filter(observation=>!(observation.side===piece.side&&eq(observation.origin,piece.position)&&observation.pattern===pattern));this.scanObservations.push({side:piece.side,origin:{...piece.position},pattern,cells:encodedCells,mineCount});
    this.message=`${piece.side} ${piece.kind} scouted ${pattern}: ${mineCount} mine(s). Scouting is free.`;return mineCount;
  }

  addPawnMoves(piece, result) {
    const forward = v(0, piece.side === Side.WHITE ? 1 : -1, 0), lateral = v(1,0,0);
    if (this.plane !== Plane.XY) { this.addWallPawnMoves(piece, result, forward); return; }
    const one = add(piece.position, forward);
    if (this.isEmpty(one)) {
      result.push(one); const two = add(one, forward); const rank = piece.side === Side.WHITE ? 1 : 6;
      if (!piece.hasMoved && piece.position.y === rank && this.isEmpty(two)) result.push(two);
    }
    for (const diagonal of [add(one,lateral), add(one,mul(lateral,-1))]) {
      const target = this.pieceAt(diagonal);
      if (TerrariumModel.isInside(diagonal) && target && target.side !== piece.side) result.push(diagonal);
    }
    const e = this.enPassantTarget;
    if (e && Math.abs(e.x-piece.position.x) === 1 && eq(e, add(one, mul(lateral, Math.sign(e.x-piece.position.x))))) {
      const vulnerable = this.pieces.find(p => p.id === this.enPassantPawnId);
      if (vulnerable?.kind === Kind.PAWN && vulnerable.side !== piece.side && eq(vulnerable.position, v(e.x,piece.position.y,piece.position.z)) && this.isEmpty(e)) result.push({ ...e });
    }
  }

  addWallPawnMoves(piece, result, forward) {
    const wallLatched = this.isWallLatched(piece.position),carriedIds=this.upwardCarriedIds(piece),vacatedByTower=cell=>{const occupant=this.pieceAt(cell);return !!occupant&&carriedIds.has(occupant.id)};
    const oneUp = add(piece.position, v(0,0,1));
    if (this.isEmpty(oneUp)||vacatedByTower(oneUp)) { result.push(oneUp); const twoUp = add(piece.position,v(0,0,2)); if (!piece.hasMoved&&(this.isEmpty(twoUp)||vacatedByTower(twoUp))) result.push(twoUp); }
    else if (wallLatched && this.isSolid(oneUp)) result.push(oneUp);
    if (this.plane !== Plane.YZ) return;
    for (const dz of [-1,1]) {
      const target = add(add(piece.position, forward), v(0,0,dz));
      if (!TerrariumModel.isInside(target)) continue;
      const occupant = this.pieceAt(target);
      if (occupant ? occupant.side !== piece.side : wallLatched && !this.isSolid(target) && this.canPawnRest(target)) result.push(target);
    }
  }

  towerAbove(piece) {
    const result = [];
    for (let z = piece.position.z + 1; z < 16; z++) { const above = this.pieceAt(v(piece.position.x,piece.position.y,z)); if (!above) break; result.push(above); }
    return result;
  }

  upwardCarriedIds(piece){const tower=this.towerAbove(piece);return new Set(tower.length&&(piece.kind===Kind.PAWN||tower[0].side===piece.side)?tower.map(member=>member.id):[])}

  canTransportTower(piece, target) {
    const destination = this.moveDestination(piece, target, this.isSolid(target));
    const transport = this.planTowerTransport(piece, destination);
    if (!transport.members.length) return true;
    const carriedIds = new Set(transport.members.map(m => m.piece.id)), occupied = new Set([key(destination)]);
    for (const member of transport.members) {
      const to = member.destination, k = key(to);
      if (!TerrariumModel.isInside(to) || this.isSolid(to) || occupied.has(k)) return false;
      occupied.add(k); const occupant = this.pieceAt(to);
      if (occupant && !carriedIds.has(occupant.id) && occupant.id !== piece.id && occupant.side===member.piece.side) return false;
    }
    return true;
  }

  planTowerTransport(piece, destination) {
    const tower = this.towerAbove(piece); if (!tower.length) return { members: [], knockedOff: 0 };
    const movingIds=new Set([piece.id,...tower.map(member=>member.id)]),obstructed=(member,cell,landing)=>{const occupant=this.pieceAt(cell);return this.isSolid(cell)||!!occupant&&!movingIds.has(occupant.id)&&(!landing||occupant.side===member.side)};
    const delta = sub(destination, piece.position);
    if (eq(delta,v(0,0,0))) return { members: tower.map(p => ({piece:p,destination:{...p.position}})), knockedOff:0 };
    if (piece.kind === Kind.KNIGHT) {
      const obstruction = tower.findIndex(member => obstructed(member,add(member.position,delta),true));
      return { members:tower.map((p,i)=>({piece:p,destination: obstruction < 0 || i < obstruction ? add(p.position,delta) : {...p.position}})), knockedOff:obstruction<0?0:tower.length-obstruction };
    }
    if (![Kind.ROOK,Kind.BISHOP,Kind.QUEEN,Kind.TRISHOP].includes(piece.kind)){const obstruction=tower.findIndex(member=>obstructed(member,add(member.position,delta),true));return{members:tower.map((p,i)=>({piece:p,destination:obstruction<0||i<obstruction?add(p.position,delta):{...p.position}})),knockedOff:obstruction<0?0:tower.length-obstruction};}
    const direction = this.stepToward(piece.position,destination), distance = Math.max(Math.abs(delta.x),Math.abs(delta.y),Math.abs(delta.z));
    let active = [...tower], knockedOff = 0; const destinations = new Map();
    for (let step=1; step<=distance && active.length; step++) {
      const collision = active.findIndex(member=>obstructed(member,add(member.position,mul(direction,step)),step===distance));
      if (collision<0) continue;
      for (let i=collision;i<active.length;i++) destinations.set(active[i].id,add(active[i].position,mul(direction,step-1)));
      knockedOff += active.length-collision; active=active.slice(0,collision);
    }
    for (const member of active) destinations.set(member.id,add(member.position,delta));
    return {members:tower.map(p=>({piece:p,destination:destinations.get(p.id)})),knockedOff};
  }

  addCastlingMoves(king, result) {
    if (this.plane!==Plane.XY || king.hasMoved || king.position.x!==4) return;
    const homeRank=king.side===Side.WHITE?0:7; if(king.position.y!==homeRank)return;
    for(const direction of [-1,1]){const rookX=direction<0?0:7,rook=this.pieceAt(v(rookX,king.position.y,king.position.z));if(!rook||rook.kind!==Kind.ROOK||rook.side!==king.side||rook.hasMoved)continue;let clear=true;for(let x=king.position.x+direction;x!==rookX;x+=direction)if(!this.isEmpty(v(x,king.position.y,king.position.z))){clear=false;break;}const destination=add(king.position,v(direction*2,0,0));if(clear&&this.isEmpty(destination))result.push(destination);}
  }
  addSliding(piece,result,directions){const carriedIds=this.upwardCarriedIds(piece);for(const direction of directions){for(let distance=1;distance<16;distance++){const target=add(piece.position,mul(direction,distance));if(!TerrariumModel.isInside(target))break;if(this.isSolid(target)){if(this.canExcavate(piece,target))result.push(target);break;}const occupant=this.pieceAt(target);if(!occupant||carriedIds.has(occupant.id)){result.push(target);continue;}if(occupant.side!==piece.side)result.push(target);break;}}}
  addStepping(piece,result,offsets){const carriedIds=this.upwardCarriedIds(piece);for(const offset of offsets){const target=add(piece.position,offset);if(!TerrariumModel.isInside(target))continue;if(this.isSolid(target)){if(this.canExcavate(piece,target))result.push(target);continue;}const occupant=this.pieceAt(target);if(!occupant||carriedIds.has(occupant.id)||occupant.side!==piece.side)result.push(target);}}

  tryMove(target) {
    this.lastFalls=[];this.lastTerrainChanges=[];this.lastPieceRemovals=[]; const piece=this.selected;
    if(!piece||!this.legalMoves(piece).some(p=>eq(p,target))){this.message="That cell is not reachable on the current plane.";return false;}
    this.pushHistory(); const from={...piece.position},events=[],excavating=this.isSolid(target);let destination={...target};
    const previousPawn=this.enPassantPawnId,previousTarget=this.enPassantTarget;this.enPassantPawnId=null;this.enPassantTarget=null;
    const releases=piece.kind===Kind.PAWN&&this.plane!==Plane.XY&&target.x===from.x&&target.y===from.y&&target.z>from.z&&!excavating;
    const castling=piece.kind===Kind.KING&&this.plane===Plane.XY&&target.y===from.y&&target.z===from.z&&Math.abs(target.x-from.x)===2;
    if(excavating){this.removeTerrain(target,events,piece.id);destination=this.moveDestination(piece,target,true);events.push(`${piece.kind} excavated ${TerrariumModel.cellName(target)}.`);if(!this.pieces.includes(piece)){this.message=events.join("  ");return this.finishMove()}}
    else{const carriedIds=this.upwardCarriedIds(piece);let captured=this.pieceAt(target);if(captured&&carriedIds.has(captured.id))captured=null;if(!captured&&piece.kind===Kind.PAWN&&eq(previousTarget,target)&&previousPawn!=null)captured=this.pieces.find(p=>p.id===previousPawn);if(captured){this.destroyPiece(captured,`${piece.kind} captured ${captured.kind}.`);events.push(eq(previousTarget,target)?`En passant captured ${captured.side} ${captured.kind}.`:`Captured ${captured.side} ${captured.kind}.`);}}
    const transport=this.planTowerTransport(piece,destination),transported=transport.members.filter(m=>!eq(m.piece.position,m.destination)).length,movingIds=new Set([piece.id,...transport.members.map(m=>m.piece.id)]);
    for(const move of transport.members.filter(m=>!eq(m.piece.position,m.destination))){const collided=this.pieceAt(move.destination);if(!collided||movingIds.has(collided.id)||collided.side===move.piece.side)continue;this.destroyPiece(collided,`${move.piece.kind} in the moving tower captured ${collided.kind}.`);events.push(`Carried ${move.piece.side} ${move.piece.kind} captured ${collided.side} ${collided.kind}.`)}
    piece.position=destination;piece.hasMoved=true;
    for(const member of transport.members){const carriedFrom={...member.piece.position};member.piece.position={...member.destination};if(eq(carriedFrom,member.destination))continue;member.piece.hasMoved=true;this.lastFalls.push({pieceId:member.piece.id,side:member.piece.side,kind:member.piece.kind,from:carriedFrom,to:{...member.destination},perished:false,startsWithMove:true});}
    if(transported)events.push(`${transported+1}-piece section moved together.`);if(transport.knockedOff)events.push(`${piece.kind===Kind.KNIGHT?"An obstruction left":"An obstruction knocked"} ${transport.knockedOff} tower piece(s) behind!`);
    if(castling){const direction=Math.sign(target.x-from.x),rookFrom=v(direction<0?0:7,from.y,from.z),rook=this.pieceAt(rookFrom);if(rook){const rookTo=v(target.x-direction,from.y,from.z);rook.position=rookTo;rook.hasMoved=true;this.lastFalls.push({pieceId:rook.id,side:rook.side,kind:rook.kind,from:rookFrom,to:rookTo,perished:false,startsWithMove:true});events.push(direction>0?"Castled kingside.":"Castled queenside.");}}
    if(piece.kind===Kind.PAWN&&this.plane===Plane.XY&&from.z===target.z&&Math.abs(target.y-from.y)===2){this.enPassantPawnId=piece.id;this.enPassantTarget=v(from.x,(from.y+target.y)/2,from.z);}
    if(this.winner)return this.finishMove();
    this.resolveGravity(events,releases?piece.id:null);
    if(this.pieces.includes(piece)&&piece.kind===Kind.PAWN&&this.onEnemyBackRank(piece)){this.pendingPromotionPieceId=piece.id;events.push("Pawn reached the far rank — choose a promotion, then return it home to awaken true-3D movement.");}
    for(const candidate of this.pieces)if(candidate.kind!==Kind.PAWN&&!candidate.promoted&&(candidate.pawnOrigin?this.onOwnHomeRank(candidate):this.onEnemyBackRank(candidate))){candidate.promoted=true;events.push(candidate.pawnOrigin?`${candidate.side} ${candidate.kind} returned home and awakened its true-3D movement.`:`${candidate.side} ${candidate.kind} awakened its true-3D movement.`)}
    const moved=excavating?`${piece.side} ${piece.kind} excavated along ${this.plane}.`:`${piece.side} ${piece.kind}: ${TerrariumModel.cellName(from)} → ${TerrariumModel.cellName(target)} on ${this.plane}.`;
    this.message=events.length?`${moved}  ${events.join("  ")}`:moved;return this.finishMove();
  }
  finishMove(){this.selectedId=null;if(!this.winner&&this.pendingPromotionPieceId==null)this.turn=other(this.turn);return true;}
  onEnemyBackRank(piece){return piece.side===Side.WHITE?piece.position.y===7:piece.position.y===0}
  onOwnHomeRank(piece){return piece.side===Side.WHITE?piece.position.y===0:piece.position.y===7}
  promote(kind){if(![Kind.QUEEN,Kind.ROOK,Kind.BISHOP,Kind.KNIGHT,Kind.TRISHOP].includes(kind)||this.pendingPromotionPieceId==null)return false;const pawn=this.pieces.find(p=>p.id===this.pendingPromotionPieceId&&p.kind===Kind.PAWN);if(!pawn)return false;pawn.kind=kind;pawn.promoted=false;pawn.pawnOrigin=true;this.pendingPromotionPieceId=null;this.message=`${pawn.side} Pawn promoted to ${kind}. Return it to its own home rank to awaken true-3D movement.`;if(!this.winner)this.turn=other(this.turn);return true;}

  removeTerrain(p,events,pieceId=null,contact=p){if(!this.isSolid(p))return false;this.setSolid(p.x,p.y,p.z,false);this.lastTerrainChanges?.push({cell:{...p},solid:false,pieceId,contact:{...contact}});this.disturbedTerrain?.add(key(p));this.revealedClues?.add(key(p));if(this.mineAt(p.x,p.y,p.z))this.detonateMine(p,events,pieceId,contact);return true}
  detonateMine(p,events,sourcePieceId=null,contact=p){this.mines[this.solidIndex(p.x,p.y,p.z)]=0;let casualties=0;
    const detonated=key(p);for(const observation of this.scanObservations??[])if(observation.cells.includes(detonated))observation.mineCount=Math.max(0,observation.mineCount-1);
    for(let dx=-1;dx<=1;dx++)for(let dy=-1;dy<=1;dy++)for(let dz=-1;dz<=1;dz++){const blast=add(p,v(dx,dy,dz));this.revealedClues?.add(key(blast));for(const victim of [...this.pieces])if(eq(victim.position,blast)){this.lastPieceRemovals?.push({pieceId:victim.id,sourcePieceId,contact:{...contact}});this.destroyPiece(victim,`${victim.kind} was caught in a mine blast.`);casualties++}}
    events.push(`Mine detonated at ${TerrariumModel.cellName(p)}: ${casualties} piece(s) hit; terrain outside the dent was untouched.`)
  }

  resolveGravity(events,releasedPawnId=null){
    let environmentPieceId=null,environmentContact=null;
    for(let pass=0;pass<256;pass++){
      // A falling piece is a projectile. After every collision, finish the
      // environment cascade it caused before tracing the projectile again.
      let changed=this.resolveTerrainGravity(events,environmentPieceId,environmentContact);
      if(changed)continue;environmentPieceId=null;environmentContact=null;
      outer:for(let x=0;x<8&&!changed;x++)for(let y=0;y<8&&!changed;y++){
        const column=this.pieces.filter(p=>p.position.x===x&&p.position.y===y).sort((a,b)=>a.position.z-b.position.z);
        for(const bottom of column){if(bottom.position.z===0||this.hasSupport(bottom.position,releasedPawnId))continue;const tower=[bottom];let nextZ=bottom.position.z+1;while(true){const above=column.find(p=>p.position.z===nextZ);if(!above)break;tower.push(above);nextZ++;}
          const supportZ=this.findSupportZ(x,y,bottom.position.z-1),landingZ=supportZ+1,fall=bottom.position.z-landingZ;if(fall<=0)continue;
          if(fall===1){for(const member of tower){const from={...member.position};member.position={...member.position,z:member.position.z-1};this.lastFalls.push({pieceId:member.id,side:member.side,kind:member.kind,from,to:{...member.position},perished:false});}events.push(tower.length>1?"Tower settled safely by 1 cell.":"Piece settled safely by 1 cell.");}
          else{const impactPiece=supportZ>=0?this.pieceAt(v(x,y,supportZ)):null,pieceBrokeFall=!!impactPiece;let impactBaseZ=Math.max(0,supportZ);
            if(supportZ>=0&&this.solidAt(x,y,supportZ)){const contact=v(x,y,supportZ),mineContact=this.mineAt(contact.x,contact.y,contact.z);this.removeTerrain(contact,events,bottom.id,contact);environmentPieceId=bottom.id;environmentContact=contact;events.push(`Impact shattered cube ${TerrariumModel.cellName(contact)}!`);
              if(mineContact){tower.forEach((member,index)=>{const from={...member.position},at=v(x,y,Math.min(15,contact.z+index));this.lastFalls.push({pieceId:member.id,side:member.side,kind:member.kind,from,to:at,perished:index<=1});if(index<=1)this.destroyPiece(member,`${member.kind} was destroyed by a mine during its fall.`);else member.position=at;});events.push(`The falling projectile struck a mine at ${TerrariumModel.cellName(contact)} and stopped at the blast.`);changed=true;break outer;}
            }
            else if(impactPiece){const stationary=[];for(let scan=supportZ;scan>=0;scan--){const member=this.pieceAt(v(x,y,scan));if(!member)break;stationary.unshift(member);}const crushed=stationary.shift();this.lastPieceRemovals.push({pieceId:crushed.id,sourcePieceId:bottom.id,contact:{...impactPiece.position}});this.destroyPiece(crushed,`The bottom ${crushed.kind} was squashed beneath its tower!`);events.push(`${crushed.side} ${crushed.kind} at the tower base was squashed!`);const crater=this.excavateCraterBelow(x,y,crushed.position.z-1,events,bottom.id),settled=crater>=0?crater:crushed.position.z;if(crater>=0){environmentPieceId=bottom.id;environmentContact=v(x,y,crater);events.push(`The squashed piece left a crater at ${TerrariumModel.cellName(environmentContact)}!`)}stationary.forEach((member,i)=>{const from={...member.position};member.position=v(x,y,Math.min(15,settled+i));if(!eq(from,member.position))this.lastFalls.push({pieceId:member.id,side:member.side,kind:member.kind,from,to:{...member.position},perished:false});});impactBaseZ=settled+stationary.length;}
            let baseZ=impactBaseZ;if(!pieceBrokeFall&&(tower.length>1||fall>=4)){const casualty=tower.shift();this.lastFalls.push({pieceId:casualty.id,side:casualty.side,kind:casualty.kind,from:{...casualty.position},to:v(x,y,baseZ),perished:true});this.destroyPiece(casualty,`${casualty.kind} perished in a ${fall}-cell fall.`);events.push(tower.length?"The tower's bottom piece perished.":`The falling piece perished after ${fall} cells.`);}
            tower.forEach((member,i)=>{const from={...member.position};member.position=v(x,y,Math.min(15,baseZ+i));this.lastFalls.push({pieceId:member.id,side:member.side,kind:member.kind,from,to:{...member.position},perished:false});});
          }
          if(releasedPawnId!=null&&tower.some(m=>m.id===releasedPawnId))releasedPawnId=null;changed=true;break outer;
        }
      }
      if(!changed||this.winner)break;
    }
  }

  resolveTerrainGravity(events,pieceId=null,contact=null){for(let z=1;z<16;z++)for(let x=1;x<7;x++)for(let y=1;y<7;y++){let flat=true,supported=false;for(let dx=-1;dx<=1;dx++)for(let dy=-1;dy<=1;dy++){flat=flat&&this.solidAt(x+dx,y+dy,z);supported=supported||this.solidAt(x+dx,y+dy,z-1);}if(!flat||supported)continue;const center=v(x,y,z),eventContact=contact??center,protectedCavern=this.cavernProtected?.has(key(center)),nearDisturbance=[...(this.disturbedTerrain??[])].some(encoded=>{const[a,b,c]=encoded.split(",").map(Number);return Math.max(Math.abs(a-x),Math.abs(b-y),Math.abs(c-z))<=1});if(protectedCavern&&!nearDisturbance)continue;this.removeTerrain(center,events,pieceId,eventContact);let hitZ=-1,hitPiece=null;for(let scan=z-1;scan>=0;scan--){hitPiece=this.pieceAt(v(x,y,scan));if(hitPiece||this.solidAt(x,y,scan)){hitZ=scan;break;}}let landingZ;if(hitPiece){this.lastPieceRemovals.push({pieceId:hitPiece.id,sourcePieceId:pieceId,contact:{...eventContact}});this.destroyPiece(hitPiece,`${hitPiece.kind} was squashed by a falling cube cell.`);events.push(`Falling terrain squashed ${hitPiece.side} ${hitPiece.kind}!`);const crater=this.excavateCraterBelow(x,y,hitZ-1,events,pieceId);landingZ=crater>=0?crater:hitZ;if(crater>=0)events.push(`The crushed piece left a crater at ${TerrariumModel.cellName(v(x,y,crater))}!`);}else landingZ=Math.max(0,hitZ+1);this.setSolid(x,y,landingZ,true);this.lastTerrainChanges?.push({cell:v(x,y,landingZ),solid:true,pieceId,contact:{...eventContact}});events.push(`Unsupported 3×3 shelf collapsed: center cube fell ${z-landingZ} cell(s).`);return true;}return false;}
  excavateCraterBelow(x,y,startZ,events=[],pieceId=null){for(let z=startZ;z>=0;z--)if(this.solidAt(x,y,z)){this.removeTerrain(v(x,y,z),events,pieceId);return z;}return -1;}
  hasSupport(p,releasedPawnId=null){if(p.z<=0)return true;const below={...p,z:p.z-1},piece=this.pieceAt(p);return this.isSolid(below)||!!this.pieceAt(below)||(piece?.kind===Kind.PAWN&&piece.id!==releasedPawnId&&this.isWallLatched(p));}
  canPawnRest(p){if(!TerrariumModel.isInside(p))return false;if(p.z===0)return true;const below={...p,z:p.z-1};return this.isSolid(below)||!!this.pieceAt(below)||this.isWallLatched(p);}
  isWallLatched(p){return [v(p.x+1,p.y,p.z),v(p.x-1,p.y,p.z),v(p.x,p.y+1,p.z),v(p.x,p.y-1,p.z)].some(n=>this.isSolid(n));}
  findSupportZ(x,y,startZ,excludedId=null){for(let z=startZ;z>=0;z--){if(this.solidAt(x,y,z))return z;const occupant=this.pieceAt(v(x,y,z));if(occupant&&occupant.id!==excludedId)return z;}return -1;}
  destroyPiece(piece,reason){this.pieces=this.pieces.filter(p=>p!==piece);if(piece.kind===Kind.KING){this.winner=other(piece.side);this.message=`${reason}  ${this.winner} wins!`;}}

  pushHistory(){this.history.push({solids:this.solids.slice(),mines:this.mines?.slice(),revealedClues:[...(this.revealedClues??[])],scanObservations:(this.scanObservations??[]).map(observation=>({...observation,origin:{...observation.origin},cells:[...observation.cells]})),cavernProtected:[...(this.cavernProtected??[])],disturbedTerrain:[...(this.disturbedTerrain??[])],pieces:this.pieces.map(clonePiece),turn:this.turn,plane:this.plane,winner:this.winner,message:this.message,selectedId:this.selectedId,enPassantPawnId:this.enPassantPawnId,enPassantTarget:this.enPassantTarget&&{...this.enPassantTarget},pendingPromotionPieceId:this.pendingPromotionPieceId});}
  undo(){const s=this.history.pop();if(!s)return false;this.solids=s.solids.slice();this.mines=s.mines?.slice()??new Uint8Array(8*8*16);this.revealedClues=new Set(s.revealedClues??[]);this.scanObservations=(s.scanObservations??[]).map(observation=>({...observation,origin:{...observation.origin},cells:[...observation.cells]}));this.cavernProtected=new Set(s.cavernProtected??[]);this.disturbedTerrain=new Set(s.disturbedTerrain??[]);this.pieces=s.pieces.map(clonePiece);this.turn=s.turn;this.plane=s.plane;this.winner=s.winner;this.message="Move undone.";this.selectedId=s.selectedId;this.enPassantPawnId=s.enPassantPawnId;this.enPassantTarget=s.enPassantTarget&&{...s.enPassantTarget};this.pendingPromotionPieceId=s.pendingPromotionPieceId;this.lastFalls=[];this.lastTerrainChanges=[];this.lastPieceRemovals=[];return true;}
  surfaceZ(x,y){for(let z=15;z>=0;z--)if(this.solidAt(x,y,z))return z;return -1;}
  predictedFall(p){if(!TerrariumModel.isInside(p))return 0;const support=this.findSupportZ(p.x,p.y,p.z-1,this.selectedId);return Math.max(0,p.z-(support+1));}
  predictOutcome(target){if(this.isExcavationTarget(target))return Outcome.EXCAVATION;const moving=this.selected,releases=moving?.kind===Kind.PAWN&&this.plane!==Plane.XY&&target.x===moving.position.x&&target.y===moving.position.y&&target.z>moving.position.z;if(moving?.kind===Kind.PAWN&&!releases&&this.canPawnRest(target))return Outcome.SAFE;const support=this.findSupportZ(target.x,target.y,target.z-1,this.selectedId),fall=Math.max(0,target.z-(support+1));if(fall<2)return Outcome.SAFE;const impact=support>=0?this.pieceAt(v(target.x,target.y,support)):null,pieceBreaks=impact&&impact.id!==this.selectedId,movingTower=moving&&this.planTowerTransport(moving,target).members.some(m=>eq(m.destination,add(target,v(0,0,1))));return !pieceBreaks&&(movingTower||fall>=4)?Outcome.FATAL:Outcome.CRATER;}
  isExcavationTarget(p){return !!this.selected&&this.isSolid(p)&&this.canExcavate(this.selected,p);}
  canExcavate(piece,target){if(piece.kind!==Kind.PAWN)return target.z>=piece.position.z;return this.plane!==Plane.XY&&this.isWallLatched(piece.position)&&eq(target,add(piece.position,v(0,0,1)));}
  moveDestination(piece,target,excavating){if(!excavating)return{...target};if(piece.kind===Kind.KNIGHT||piece.kind===Kind.PAWN)return{...piece.position};if(piece.kind===Kind.KING)return{...target};return sub(target,this.stepToward(piece.position,target));}
  stepToward(from,to){return v(Math.sign(to.x-from.x),Math.sign(to.y-from.y),Math.sign(to.z-from.z));}
  planeAxes(plane){return plane===Plane.XY?[v(1,0,0),v(0,1,0)]:plane===Plane.XZ?[v(1,0,0),v(0,0,1)]:[v(0,1,0),v(0,0,1)];}
  static cellName(p){return `${String.fromCharCode(65+p.x)}${p.y+1}·${p.z}`;}
}

const parseKey=encoded=>{const[x,y,z]=encoded.split(",").map(Number);return v(x,y,z)};
const isSubset=(left,right)=>[...left].every(cell=>right.has(cell));

function visibleConstraints(position,knownSafe,knownMines){
  const constraints=[];
  for(const encoded of position.revealedClues??[]){const clueCell=parseKey(encoded),clue=position.clueAt(clueCell);if(clue==null)continue;const cells=new Set();let adjacentKnownMines=0;
    for(let dx=-1;dx<=1;dx++)for(let dy=-1;dy<=1;dy++)for(let dz=-1;dz<=1;dz++){if(!(dx||dy||dz))continue;const cell=add(clueCell,v(dx,dy,dz)),cellKey=key(cell);if(!TerrariumModel.isInside(cell)||cell.z>=7||!position.isSolid(cell))continue;if(knownMines.has(cellKey))adjacentKnownMines++;else if(!knownSafe.has(cellKey))cells.add(cellKey)}
    const remaining=clue-adjacentKnownMines;if(cells.size&&remaining>=0&&remaining<=cells.size)constraints.push({cells,remaining});
  }
  for(const observation of position.scanObservations??[]){const cells=new Set(),active=observation.cells.filter(encoded=>{const cell=parseKey(encoded);return cell.z<7&&position.isSolid(cell)}),adjacentKnownMines=active.filter(encoded=>knownMines.has(encoded)).length;for(const encoded of active)if(!knownSafe.has(encoded)&&!knownMines.has(encoded))cells.add(encoded);const remaining=observation.mineCount-adjacentKnownMines;if(cells.size&&remaining>=0&&remaining<=cells.size)constraints.push({cells,remaining})}
  return constraints;
}

function deduceMineKnowledge(position){
  position.botMineFlags=new Set([...(position.botMineFlags??[])].filter(encoded=>{const cell=parseKey(encoded);return TerrariumModel.isInside(cell)&&cell.z<7&&position.isSolid(cell)}));
  position.botSafeMarks=new Set([...(position.botSafeMarks??[])].filter(encoded=>{const cell=parseKey(encoded);return TerrariumModel.isInside(cell)&&position.isSolid(cell)}));
  const knownMines=new Set(position.botMineFlags),knownSafe=new Set(position.botSafeMarks);
  for(const encoded of position.revealedClues??[]){const cell=parseKey(encoded);if(TerrariumModel.isInside(cell))knownSafe.add(encoded)}
  let changed=true;
  while(changed){changed=false;const constraints=visibleConstraints(position,knownSafe,knownMines);
    for(const constraint of constraints){if(constraint.remaining===0){for(const cell of constraint.cells)if(!knownSafe.has(cell)){knownSafe.add(cell);changed=true}}else if(constraint.remaining===constraint.cells.size){for(const cell of constraint.cells)if(!knownMines.has(cell)){knownMines.add(cell);changed=true}}}
    if(changed)continue;
    outer:for(let i=0;i<constraints.length;i++)for(let j=0;j<constraints.length;j++){const a=constraints[i],b=constraints[j];if(i===j||a.cells.size>=b.cells.size||!isSubset(a.cells,b.cells))continue;const difference=new Set([...b.cells].filter(cell=>!a.cells.has(cell))),remaining=b.remaining-a.remaining;if(remaining===0){for(const cell of difference)if(!knownSafe.has(cell)){knownSafe.add(cell);changed=true}}else if(remaining===difference.size){for(const cell of difference)if(!knownMines.has(cell)){knownMines.add(cell);changed=true}}if(changed)break outer}
  }
  for(const cell of knownMines){knownSafe.delete(cell);position.botMineFlags.add(cell)}
  for(const encoded of knownSafe){const cell=parseKey(encoded);if(position.isSolid(cell))position.botSafeMarks.add(encoded)}
  for(const cell of position.botMineFlags)position.botSafeMarks.delete(cell);
}

export function prepareBotTurn(position){
  if(!position.minesweeperEnabled||position.winner)return;
  const originalSelection=position.selectedId,side=position.turn;let scans=0;
  for(const piece of position.pieces.filter(piece=>piece.side===side)){if(!position.select(piece.id))continue;for(const pattern of [...position.scoutPatterns(piece)])if(position.scout(pattern)!==null)scans++}
  deduceMineKnowledge(position);if(originalSelection==null||!position.select(originalSelection))position.clearSelection();
  position.message=`${side} bot used ${scans} free scans and now marks ${position.botSafeMarks.size} clear / ${position.botMineFlags.size} mined cells.`;
}

const pieceValues = {[Kind.PAWN]:100,[Kind.KNIGHT]:320,[Kind.BISHOP]:330,[Kind.TRISHOP]:360,[Kind.ROOK]:500,[Kind.QUEEN]:900,[Kind.KING]:20000};
function material(position,side){return position.pieces.filter(p=>p.side===side).reduce((sum,p)=>sum+pieceValues[p.kind],0);}
function preservationPenalty(root,position,botSide){const opponent=other(botSide),ownLoss=material(root,botSide)-material(position,botSide),opponentLoss=material(root,opponent)-material(position,opponent);return Math.max(0,ownLoss-opponentLoss)*3;}
function evaluate(position, botSide){if(position.winner===botSide)return 1e6;if(position.winner===other(botSide))return-1e6;return position.pieces.reduce((score,p)=>{const sign=p.side===botSide?1:-1,center=14-Math.abs(p.position.x*2-7)-Math.abs(p.position.y*2-7),adv=p.kind===Kind.PAWN?(p.side===Side.WHITE?p.position.y:7-p.position.y):0;return score+sign*(pieceValues[p.kind]+(p.kind===Kind.KING?center:center*2)+adv*6);},0);}
function generateMoves(position,side){const moves=[];for(const plane of planes){position.plane=plane;for(const piece of position.pieces.filter(p=>p.side===side))for(const target of position.legalMoves(piece))moves.push({pieceId:piece.id,plane,target});}return moves;}
function applyMove(position,move){const result=position.cloneForSimulation();result.plane=move.plane;if(!result.select(move.pieceId)||!result.tryMove(move.target))return null;if(result.pendingPromotionPieceId!=null)result.promote(Kind.QUEEN);return result;}

export function chooseBotMove(position, replyLimit = 72){
  if(position.winner)return null;const root=position.cloneForBotSearch(),botSide=root.turn,opponent=other(botSide),moves=generateMoves(root,botSide),analyses=[];
  for(const move of moves){const after=applyMove(root,move);if(!after)continue;if(after.winner===botSide)return move;let worst=Infinity,hasEvaluation=false,allowsMate=false;const replies=generateMoves(after,opponent),sampleStep=replies.length>replyLimit?Math.ceil(replies.length/replyLimit):1;
    // Every reply is simulated for king safety. Direct captures are also always
    // scored, so reply sampling can never hide an exposed piece or bad trade.
    for(let i=0;i<replies.length;i++){const reply=replies[i],victim=after.pieceAt(reply.target),directCapture=victim?.side===botSide||(after.enPassantTarget&&eq(after.enPassantTarget,reply.target)&&after.enPassantPawnId!=null),afterReply=applyMove(after,reply);if(!afterReply)continue;if(afterReply.winner===opponent){allowsMate=true;worst=-1e6;break;}if(directCapture||i%sampleStep===0){worst=Math.min(worst,evaluate(afterReply,botSide)-preservationPenalty(root,afterReply,botSide));hasEvaluation=true;}}
    if(!allowsMate&&!hasEvaluation)worst=evaluate(after,botSide)-preservationPenalty(root,after,botSide);analyses.push({move,score:worst,allowsMate});
  }
  if(!analyses.length)return null;const safe=analyses.filter(analysis=>!analysis.allowsMate),candidates=safe.length?safe:analyses;let best=candidates[0];for(const candidate of candidates)if(candidate.score>best.score)best=candidate;return best.move;
}
