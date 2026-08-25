import { TerrariumModel, Side, Kind, Plane } from "./engine.js";
import { WorldRenderer } from "./webgl-renderer.js";

const model=new TerrariumModel(),canvas=document.querySelector("#board"),renderer=new WorldRenderer(canvas);
const ui={turn:document.querySelector("#turn-label"),message:document.querySelector("#message"),selected:document.querySelector("#selected-piece"),planes:[...document.querySelectorAll("[data-plane]")],mode:document.querySelector("#mode-select"),role:document.querySelector("#role-note"),mine:document.querySelector("#minesweeper-toggle"),scout:document.querySelector("#scout-controls"),scoutPattern:document.querySelector("#scout-pattern"),scoutButton:document.querySelector("#scout-button"),layer:document.querySelector("#layer-toggle"),depth:document.querySelector("#depth-slider"),depthOut:document.querySelector("#depth-output"),camera:document.querySelector("#camera-readout"),help:document.querySelector("#help-dialog"),promotion:document.querySelector("#promotion-dialog"),thinking:document.querySelector("#thinking")};
let botWorker=null,botTimer=null,positionVersion=0,dirty=true,lastTime=performance.now(),pointerStart=null,pointerLast=null,dragging=false;
const MatchMode=Object.freeze({PVP:"pvp",PVBOT:"pvbot",BOTVBOT:"botvbot"});
let matchMode=MatchMode.PVBOT,falls=[],terrainBreaks=[],preImpactSolids=null,preImpactPieces=null,impactTime=0,transitionElapsed=0,pendingBot=false;
const clonePieces=pieces=>pieces.map(p=>({...p,position:{...p.position}}));
const same=(a,b)=>a&&b&&a.x===b.x&&a.y===b.y&&a.z===b.z;
const lerp=(a,b,t)=>a+(b-a)*t;
const isBotTurn=()=>matchMode===MatchMode.BOTVBOT||(matchMode===MatchMode.PVBOT&&model.turn===Side.BLACK);
const isHumanTurn=()=>!isBotTurn();
const sideName=side=>side.toUpperCase();

function startBotWorker(){
  botWorker?.terminate();botWorker=new Worker(new URL("./bot-worker.js",import.meta.url),{type:"module"});
  botWorker.addEventListener("message",event=>{
    const {version,move}=event.data;if(version!==positionVersion||!isBotTurn())return;
    if(!move){model.message=`${sideName(model.turn)} bot has no legal move.`;ui.thinking.hidden=true;syncUI();return}
    model.setPlane(move.plane);model.select(move.pieceId);const moving=model.selected,beforeSolids=model.solids.slice(),beforePieces=clonePieces(model.pieces),from={...moving.position};
    if(model.tryMove(move.target)){if(model.pendingPromotionPieceId!=null)model.promote(Kind.QUEEN);pendingBot=isBotTurn()&&!model.winner;startTransition(moving.id,from,beforeSolids,beforePieces)}
    ui.thinking.hidden=true;syncUI();
  });
  botWorker.addEventListener("error",()=>{if(isBotTurn()){model.message=`${sideName(model.turn)} bot search failed. Undo or restart to continue.`;ui.thinking.hidden=true;syncUI()}});
}
startBotWorker();

function startTransition(movingId,moveFrom,terrainBefore,piecesBefore){
  falls=[];terrainBreaks=[];transitionElapsed=0;preImpactSolids=terrainBefore;preImpactPieces=piecesBefore;const delays=new Map();
  const firstFall=model.lastFalls.find(f=>f.pieceId===movingId),survivor=model.pieces.find(p=>p.id===movingId),moveTo=firstFall?.from??survivor?.position??moveFrom;let moveDuration=0;
  if(!same(moveFrom,moveTo)){
    const travel=Math.abs(moveTo.x-moveFrom.x)+Math.abs(moveTo.y-moveFrom.y)+Math.abs(moveTo.z-moveFrom.z);moveDuration=.20+travel*.045;
    const before=piecesBefore.find(p=>p.id===movingId);falls.push({fall:{pieceId:movingId,side:before.side,kind:before.kind,from:{...moveFrom},to:{...moveTo},perished:false},delay:0,duration:moveDuration,elapsed:0});delays.set(movingId,moveDuration);
  }
  for(const fall of model.lastFalls){
    const distance=Math.max(1,Math.abs(fall.from.x-fall.to.x)+Math.abs(fall.from.y-fall.to.y)+Math.abs(fall.from.z-fall.to.z));
    const delay=fall.startsWithMove?(delays.get(fall.pieceId)||0):Math.max(moveDuration,delays.get(fall.pieceId)||0),duration=fall.startsWithMove ? .20+distance*.045 : .24+distance*.07;
    falls.push({fall:{...fall,from:{...fall.from},to:{...fall.to}},delay,duration,elapsed:0});delays.set(fall.pieceId,delay+duration);
  }
  impactTime=Math.max(.08,falls.length?Math.max(...falls.map(f=>f.delay+f.duration)):.08);dirty=true;
  for(const terrainBreak of model.lastTerrainBreaks??[]){const candidates=falls.filter(animation=>animation.fall.pieceId===terrainBreak.pieceId),contact=candidates.find(animation=>same(animation.fall.to,terrainBreak.cell))??candidates.find(animation=>animation.fall.to.z<=terrainBreak.cell.z+1)??candidates.at(-1),at=contact?contact.delay+contact.duration*.9:terrainBreak.pieceId===movingId?Math.max(.04,moveDuration*.9):impactTime;terrainBreaks.push({cell:{...terrainBreak.cell},at,applied:false})}
  if(terrainBreaks.length)impactTime=Math.max(impactTime,Math.max(...terrainBreaks.map(change=>change.at+.08)));
}
function transitionActive(){return preImpactSolids!==null}
function progress(a){return Math.max(0,Math.min(1,(a.elapsed-a.delay)/a.duration))}
function activeFall(pieceId){const list=falls.filter(a=>a.fall.pieceId===pieceId).sort((a,b)=>a.delay-b.delay);return [...list].reverse().find(a=>a.elapsed>=a.delay)||list[0]||null}
function animatedPosition(a){const t=progress(a),q=t*t,p={x:lerp(a.fall.from.x,a.fall.to.x,q),y:lerp(a.fall.from.y,a.fall.to.y,q),z:lerp(a.fall.from.z,a.fall.to.z,q)};if(!a.fall.perished&&t>.78)p.z+=Math.sin((t-.78)/.22*Math.PI)*.16;return p}
function renderPieces(){
  const result=[];
  for(const piece of model.pieces){const animation=activeFall(piece.id),before=preImpactPieces?.find(p=>p.id===piece.id),visual=before&&before.kind!==piece.kind?before:piece;result.push({piece:visual,position:animation?animatedPosition(animation):{...piece.position},opacity:1})}
  for(const animation of falls.filter(a=>a.fall.perished&&activeFall(a.fall.pieceId)===a)){const f=animation.fall;result.push({piece:{id:f.pieceId,side:f.side,kind:f.kind,position:{...f.to}},position:animatedPosition(animation),opacity:1-Math.pow(progress(animation),3)})}
  if(preImpactPieces){const finalIds=new Set(model.pieces.map(p=>p.id)),animatedIds=new Set(falls.map(a=>a.fall.pieceId));for(const piece of preImpactPieces)if(!finalIds.has(piece.id)&&!animatedIds.has(piece.id))result.push({piece,position:{...piece.position},opacity:1})}
  return result;
}
function finishTransition(){preImpactSolids=null;preImpactPieces=null;falls=[];terrainBreaks=[];dirty=true;syncUI();if(pendingBot&&isBotTurn()&&!model.winner&&model.pendingPromotionPieceId==null){pendingBot=false;queueBot()}}
function updateTransition(dt){if(!transitionActive())return;transitionElapsed+=dt;for(const animation of falls)animation.elapsed+=dt;for(const change of terrainBreaks)if(!change.applied&&transitionElapsed>=change.at){change.applied=true;preImpactSolids[model.solidIndex(change.cell.x,change.cell.y,change.cell.z)]=0}impactTime-=dt;if(impactTime<=0)finishTransition();dirty=true}

function syncUI(){
  const botThinking=!ui.thinking.hidden;ui.turn.textContent=model.winner?`${sideName(model.winner)} WINS`:botThinking?`${sideName(model.turn)} BOT THINKING`:`${sideName(model.turn)} TO MOVE`;ui.turn.classList.toggle("black",model.turn===Side.BLACK&&!model.winner);
  ui.message.textContent=model.message;const piece=model.selected;ui.selected.textContent=piece?`${piece.kind}${piece.promoted?" ✦":""}  ·  ${TerrariumModel.cellName(piece.position)}`:"No piece selected";
  ui.thinking.innerHTML=`<i></i> ${sideName(model.turn)} BOT THINKING`;
  ui.role.textContent=matchMode===MatchMode.PVP?"Same-device PvP · pass control each turn":matchMode===MatchMode.BOTVBOT?"Bot vs Bot · spectator mode":"You are White · Black is the bot";
  for(const button of ui.planes)button.setAttribute("aria-checked",String(button.dataset.plane===model.plane));
  const patterns=model.minesweeperEnabled&&piece?model.scoutPatterns(piece):[],previous=ui.scoutPattern.value;ui.scout.hidden=!patterns.length;ui.scoutPattern.replaceChildren(...patterns.map(pattern=>{const option=document.createElement("option");option.value=pattern;option.textContent=pattern.replaceAll("-"," ").toUpperCase();return option}));if(patterns.includes(previous))ui.scoutPattern.value=previous;
  ui.camera.textContent=`TRUE 3D / ${Math.round(renderer.elevation).toString().padStart(2,"0")}° / CAM Z ${renderer.camera.z.toFixed(1).padStart(4,"0")}`;
  if(model.pendingPromotionPieceId!=null&&isHumanTurn()&&!transitionActive()&&!ui.promotion.open)ui.promotion.showModal();dirty=true;
}
function draw(){
  renderer.render(model,preImpactSolids??model.solids,renderPieces(),transitionActive()?[]:model.selected?model.legalMoves():[]);dirty=false;
}
function frame(now){const dt=Math.min(.05,(now-lastTime)/1000);lastTime=now;updateTransition(dt);if(dirty)draw();requestAnimationFrame(frame)}
requestAnimationFrame(frame);new ResizeObserver(()=>{dirty=true}).observe(canvas);

function setPlane(plane){if(!isHumanTurn()||model.winner||model.pendingPromotionPieceId!=null||transitionActive())return;model.setPlane(plane);syncUI()}
function cyclePiece(backward=false){if(!isHumanTurn()||transitionActive())return;const pieces=model.pieces.filter(p=>p.side===model.turn).sort((a,b)=>a.id-b.id);if(!pieces.length)return;let i=pieces.findIndex(p=>p.id===model.selectedId);i=(i+(backward?-1:1)+pieces.length)%pieces.length;model.select(pieces[i].id);syncUI()}
function reset(){clearTimeout(botTimer);pendingBot=false;preImpactSolids=null;preImpactPieces=null;falls=[];terrainBreaks=[];matchMode=MatchMode.PVBOT;ui.mode.value=matchMode;model.reset();positionVersion++;startBotWorker();ui.thinking.hidden=true;if(ui.promotion.open)ui.promotion.close();syncUI();if(isBotTurn())queueBot()}
function setMatchMode(mode){
  if(matchMode===mode)return;clearTimeout(botTimer);pendingBot=false;matchMode=mode;positionVersion++;startBotWorker();ui.thinking.hidden=true;
  if(model.pendingPromotionPieceId!=null&&isBotTurn()){model.promote(Kind.QUEEN);if(ui.promotion.open)ui.promotion.close();positionVersion++;}
  syncUI();if(transitionActive())pendingBot=isBotTurn()&&!model.winner;else if(isBotTurn())queueBot();
}
function undo(){if(transitionActive())return;clearTimeout(botTimer);pendingBot=false;ui.thinking.hidden=true;if(!model.undo())return;if(matchMode===MatchMode.PVBOT&&model.turn===Side.BLACK&&!model.winner)model.undo();positionVersion++;startBotWorker();syncUI();if(isBotTurn())queueBot()}
function queueBot(){
  if(!isBotTurn()||model.winner||model.pendingPromotionPieceId!=null||transitionActive())return;const version=++positionVersion;ui.thinking.hidden=false;syncUI();
  botTimer=setTimeout(()=>{if(version!==positionVersion||!isBotTurn())return;const state=model.cloneForSimulation();botWorker.postMessage({version,state:{solids:[...state.solids],mines:[...state.mines],revealedClues:[...state.revealedClues],cavernProtected:[...state.cavernProtected],disturbedTerrain:[...state.disturbedTerrain],minesweeperEnabled:state.minesweeperEnabled,pieces:state.pieces,turn:state.turn,plane:state.plane,winner:state.winner,message:state.message,selectedId:state.selectedId,enPassantPawnId:state.enPassantPawnId,enPassantTarget:state.enPassantTarget,pendingPromotionPieceId:state.pendingPromotionPieceId,nextId:state.nextId}})},460);
}
function executeMove(target){
  const moving=model.selected;if(!moving)return;const beforeSolids=model.solids.slice(),beforePieces=clonePieces(model.pieces),from={...moving.position};
  if(model.tryMove(target)){positionVersion++;pendingBot=model.pendingPromotionPieceId==null&&isBotTurn();startTransition(moving.id,from,beforeSolids,beforePieces)}syncUI();
}
function hitAt(point){
  if(!isHumanTurn()||model.winner||model.pendingPromotionPieceId!=null||transitionActive())return;
  const radius=Math.max(18,32*renderer.width/1050),moves=model.selected?model.legalMoves():[];
  const moveHits=moves.map(target=>({target,screen:renderer.project(renderer.targetPoint(model,target))})).filter(h=>Math.hypot(point.x-h.screen.x,point.y-h.screen.y)<=radius).sort((a,b)=>a.screen.depth-b.screen.depth);
  if(moveHits.length){executeMove(moveHits[0].target);return}
  const pieceHits=model.pieces.map(piece=>({piece,screen:renderer.project({x:piece.position.x,y:piece.position.y,z:piece.position.z+.48})})).filter(h=>Math.hypot(point.x-h.screen.x,point.y-h.screen.y)<=radius).sort((a,b)=>a.screen.depth-b.screen.depth);
  if(pieceHits.length)model.select(pieceHits[0].piece.id);else model.clearSelection();syncUI();
}
function point(e){const r=canvas.getBoundingClientRect();return{x:e.clientX-r.left,y:e.clientY-r.top}}
canvas.addEventListener("pointerdown",e=>{if(e.button===1){e.preventDefault();toggleLayer();return}if(e.button!==0)return;canvas.setPointerCapture(e.pointerId);pointerStart=pointerLast=point(e);dragging=false});
canvas.addEventListener("pointermove",e=>{if(!pointerStart)return;const here=point(e);if(Math.hypot(here.x-pointerStart.x,here.y-pointerStart.y)>5)dragging=true;if(dragging){renderer.yaw-=(here.x-pointerLast.x)*.009;renderer.elevation=Math.max(30,Math.min(60,renderer.elevation+(here.y-pointerLast.y)*.18));renderer.updateCamera();syncUI()}pointerLast=here});
canvas.addEventListener("pointerup",e=>{if(pointerStart&&!dragging)hitAt(point(e));pointerStart=pointerLast=null;dragging=false});canvas.addEventListener("pointercancel",()=>{pointerStart=pointerLast=null;dragging=false});canvas.addEventListener("contextmenu",e=>{e.preventDefault();if(!transitionActive()){model.clearSelection();syncUI()}});
canvas.addEventListener("wheel",e=>{if(!renderer.layerFocus)return;e.preventDefault();renderer.focusLayer=Math.max(0,Math.min(15,renderer.focusLayer-Math.sign(e.deltaY)));ui.depth.value=renderer.focusLayer;ui.depthOut.value=`Z${String(renderer.focusLayer).padStart(2,"0")}`;dirty=true},{passive:false});

function toggleLayer(){renderer.layerFocus=!renderer.layerFocus;ui.layer.setAttribute("aria-pressed",String(renderer.layerFocus));ui.layer.querySelector("b").textContent=`MB  LAYER FOCUS: ${renderer.layerFocus?`Z${String(renderer.focusLayer).padStart(2,"0")}`:"OFF"}`;ui.depth.disabled=!renderer.layerFocus;dirty=true}
for(const button of ui.planes)button.addEventListener("click",()=>setPlane(button.dataset.plane));ui.layer.addEventListener("click",toggleLayer);ui.depth.addEventListener("input",()=>{renderer.focusLayer=Number(ui.depth.value);ui.depthOut.value=`Z${String(renderer.focusLayer).padStart(2,"0")}`;ui.layer.querySelector("b").textContent=`MB  LAYER FOCUS: Z${String(renderer.focusLayer).padStart(2,"0")}`;dirty=true});
document.querySelector("#undo-button").addEventListener("click",undo);document.querySelector("#restart-button").addEventListener("click",reset);document.querySelector("#help-button").addEventListener("click",()=>ui.help.showModal());
ui.mode.addEventListener("change",()=>setMatchMode(ui.mode.value));
ui.mine.addEventListener("change",()=>{model.minesweeperEnabled=ui.mine.checked;reset()});
ui.scoutButton.addEventListener("click",()=>{if(!isHumanTurn()||transitionActive()||!model.scout(ui.scoutPattern.value))return;positionVersion++;syncUI()});
for(const button of document.querySelectorAll("[data-kind]"))button.addEventListener("click",()=>{if(model.promote(button.dataset.kind)){ui.promotion.close();positionVersion++;pendingBot=false;syncUI();if(isBotTurn())queueBot()}});
window.addEventListener("keydown",e=>{
  if(ui.help.open){if(["h","Escape"].includes(e.key))ui.help.close();return}const key=e.key.toLowerCase();if(ui.promotion.open){const promotion={q:Kind.QUEEN,r:Kind.ROOK,b:Kind.BISHOP,n:Kind.KNIGHT,t:Kind.TRISHOP}[key];if(promotion)document.querySelector(`[data-kind="${promotion}"]`).click();return}
  if(["1","2","3"," ","tab","q","e","u","r","h","m","arrowup","arrowdown"].includes(key))e.preventDefault();
  if(key==="1")setPlane(Plane.XY);else if(key==="2")setPlane(Plane.XZ);else if(key==="3")setPlane(Plane.YZ);else if(key===" "){const p=[Plane.XY,Plane.XZ,Plane.YZ];setPlane(p[(p.indexOf(model.plane)+1)%3])}else if(key==="tab")cyclePiece(e.shiftKey);else if(key==="u")undo();else if(key==="r")reset();else if(key==="h")ui.help.showModal();else if(key==="m")toggleLayer();else if(key==="q"||key==="e"){renderer.yaw+=(key==="e"?1:-1)*Math.PI/2;renderer.updateCamera();syncUI()}else if(key==="arrowup"||key==="arrowdown"){renderer.heightOffset=Math.max(-7,Math.min(7,renderer.heightOffset+(key==="arrowdown"?1:-1)));renderer.updateCamera();syncUI()}else if(key==="escape"){model.clearSelection();syncUI()}
});

syncUI();
