import { TerrariumModel, Side, Kind, Plane, Outcome } from "./engine.js";

const model = new TerrariumModel();
const canvas = document.querySelector("#board"), ctx = canvas.getContext("2d");
const ui = {
  turn: document.querySelector("#turn-label"), turnChip: document.querySelector(".turn-chip"), message: document.querySelector("#message"),
  selected: document.querySelector("#selected-piece"), planes: [...document.querySelectorAll("[data-plane]")],
  layerToggle: document.querySelector("#layer-toggle"), depth: document.querySelector("#depth-slider"), depthOut: document.querySelector("#depth-output"),
  help: document.querySelector("#help-dialog"), promotion: document.querySelector("#promotion-dialog"), thinking: document.querySelector("#thinking"),
};
const glyphs = { Pawn:"♟", Knight:"♞", Bishop:"♝", Rook:"♜", Queen:"♛", King:"♚" };
const outcomeColors = { [Outcome.SAFE]:"#43d6c4", [Outcome.CRATER]:"#ffc75a", [Outcome.EXCAVATION]:"#ffc75a", [Outcome.FATAL]:"#ff5f63" };
const camera = { yaw: -Math.PI / 4, elevation: 45, height: 0, focus:false, depth:8 };
let width=0,height=0,dpr=1,scale=40,pointerStart=null,pointerLast=null,dragging=false,legalHits=[],pieceHits=[],botTimer=null,positionVersion=0,botWorker=null;

function startBotWorker(){
  botWorker?.terminate();
  botWorker=new Worker(new URL("./bot-worker.js",import.meta.url),{type:"module"});
  botWorker.addEventListener("message",event=>{
    const {version,move}=event.data;if(version!==positionVersion||model.turn!==Side.BLACK)return;
    if(!move){model.message="Black bot has no legal move.";ui.thinking.hidden=true;syncUI();return;}
    model.setPlane(move.plane);model.select(move.pieceId);model.tryMove(move.target);if(model.pendingPromotionPieceId!=null)model.promote(Kind.QUEEN);ui.thinking.hidden=true;syncUI();
  });
  botWorker.addEventListener("error",()=>{if(model.turn===Side.BLACK){model.message="Black bot search failed. Undo or restart to continue.";ui.thinking.hidden=true;syncUI();}});
}
startBotWorker();

function resize(){const rect=canvas.getBoundingClientRect();dpr=Math.min(2,window.devicePixelRatio||1);width=Math.max(1,rect.width);height=Math.max(1,rect.height);canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);ctx.setTransform(dpr,0,0,dpr,0,0);scale=Math.min(width/14.5,height/18.5);render();}
new ResizeObserver(resize).observe(canvas);

function rotatePoint(x,y){const dx=x-3.5,dy=y-3.5,c=Math.cos(camera.yaw),s=Math.sin(camera.yaw);return{x:dx*c-dy*s,y:dx*s+dy*c};}
function project(x,y,z){const r=rotatePoint(x,y),e=(camera.elevation-30)/30;return{x:width*.5+r.x*scale,y:height*.72+r.y*scale*(.36+.12*e)-z*scale*(.72+.12*e)+camera.height*scale*.42,depth:r.y-z*.28};}
function poly(points,fill,stroke="#0000",line=1,alpha=1){ctx.save();ctx.globalAlpha=alpha;ctx.beginPath();ctx.moveTo(points[0].x,points[0].y);for(let i=1;i<points.length;i++)ctx.lineTo(points[i].x,points[i].y);ctx.closePath();ctx.fillStyle=fill;ctx.fill();if(stroke!=="#0000"){ctx.strokeStyle=stroke;ctx.lineWidth=line;ctx.stroke();}ctx.restore();}
function terrainBase(x,y,z){const checker=(x+y+z)%2,light=22+z*.85+checker*3;return `hsl(${174+x*1.2} 28% ${light}%)`;}

function buildFaces(){
  const faces=[];
  for(let x=0;x<8;x++)for(let y=0;y<8;y++)for(let z=0;z<16;z++){
    if(!model.solidAt(x,y,z)||(camera.focus&&z!==camera.depth))continue;
    const exposed=(nx,ny,nz)=>nx<0||nx>7||ny<0||ny>7||nz<0||nz>15||!model.solidAt(nx,ny,nz)||(camera.focus&&nz!==camera.depth);
    const addFace=(corners,shade)=>{const pts=corners.map(p=>project(...p)),depth=corners.reduce((n,p)=>n+project(...p).depth,0)/corners.length;faces.push({pts,depth,fill:terrainBase(x,y,z),shade});};
    if(exposed(x,y,z+1))addFace([[x,y,z+1],[x+1,y,z+1],[x+1,y+1,z+1],[x,y+1,z+1]],1.18);
    if(exposed(x-1,y,z))addFace([[x,y,z],[x,y+1,z],[x,y+1,z+1],[x,y,z+1]],.76);
    if(exposed(x+1,y,z))addFace([[x+1,y+1,z],[x+1,y,z],[x+1,y,z+1],[x+1,y+1,z+1]],.72);
    if(exposed(x,y-1,z))addFace([[x+1,y,z],[x,y,z],[x,y,z+1],[x+1,y,z+1]],.88);
    if(exposed(x,y+1,z))addFace([[x,y+1,z],[x+1,y+1,z],[x+1,y+1,z+1],[x,y+1,z+1]],.82);
  }
  return faces.sort((a,b)=>b.depth-a.depth);
}

function shadeColor(color,amount){const m=color.match(/hsl\(([^ ]+) ([^ ]+) ([^)]+)%\)/);if(!m)return color;return `hsl(${m[1]} ${m[2]} ${Math.max(4,Math.min(80,parseFloat(m[3])*amount))}%)`;}
function drawTerrain(){for(const face of buildFaces())poly(face.pts,shadeColor(face.fill,face.shade),"#24444a",.65,camera.focus?.88:1);if(camera.focus){const z=camera.depth+.015,corners=[[0,0,z],[8,0,z],[8,8,z],[0,8,z]].map(p=>project(...p));ctx.save();ctx.setLineDash([5,6]);poly(corners,"#43d6c407","#43d6c477",1);ctx.restore();}}

function drawHint(target){
  const outcome=model.predictOutcome(target),color=outcomeColors[outcome],center=project(target.x+.5,target.y+.5,target.z+.07),r=Math.max(8,scale*.24);
  ctx.save();ctx.globalCompositeOperation="screen";const glow=ctx.createRadialGradient(center.x,center.y,0,center.x,center.y,r*2.2);glow.addColorStop(0,color+"aa");glow.addColorStop(.35,color+"45");glow.addColorStop(1,color+"00");ctx.fillStyle=glow;ctx.beginPath();ctx.arc(center.x,center.y,r*2.2,0,Math.PI*2);ctx.fill();ctx.globalCompositeOperation="source-over";ctx.strokeStyle=color;ctx.lineWidth=2;ctx.beginPath();ctx.arc(center.x,center.y,r,0,Math.PI*2);ctx.stroke();ctx.fillStyle=color+"45";ctx.fill();
  if(outcome===Outcome.EXCAVATION){ctx.beginPath();ctx.moveTo(center.x-r*.45,center.y-r*.45);ctx.lineTo(center.x+r*.45,center.y+r*.45);ctx.moveTo(center.x+r*.45,center.y-r*.45);ctx.lineTo(center.x-r*.45,center.y+r*.45);ctx.stroke();}
  ctx.restore();legalHits.push({target,center,r:Math.max(17,r*1.5)});
}

function drawPiece(piece){
  const p=project(piece.position.x+.5,piece.position.y+.5,piece.position.z+.05),size=Math.max(18,scale*.66),selected=piece.id===model.selectedId;
  const white=piece.side===Side.WHITE,body=white?"#e8e8df":"#18262b",edge=white?"#ffffff":"#759097";
  ctx.save();if(selected){ctx.shadowColor="#43d6c4";ctx.shadowBlur=22;ctx.fillStyle="#43d6c433";ctx.beginPath();ctx.ellipse(p.x,p.y+2,size*.67,size*.27,0,0,Math.PI*2);ctx.fill();}
  ctx.shadowBlur=white?8:5;ctx.shadowColor="#000b";ctx.fillStyle=body;ctx.strokeStyle=selected?"#43d6c4":edge;ctx.lineWidth=selected?2:1;
  ctx.beginPath();ctx.ellipse(p.x,p.y+3,size*.53,size*.2,0,0,Math.PI*2);ctx.fill();ctx.stroke();
  ctx.beginPath();ctx.moveTo(p.x-size*.35,p.y+1);ctx.quadraticCurveTo(p.x-size*.25,p.y-size*.6,p.x,p.y-size*.72);ctx.quadraticCurveTo(p.x+size*.25,p.y-size*.6,p.x+size*.35,p.y+1);ctx.closePath();ctx.fill();ctx.stroke();
  ctx.shadowBlur=0;ctx.font=`${size*.72}px Georgia,serif`;ctx.textAlign="center";ctx.textBaseline="middle";ctx.fillStyle=white?"#0d2025":"#c9d5d1";ctx.fillText(glyphs[piece.kind],p.x,p.y-size*.34);
  if(selected){ctx.font=`500 ${Math.max(8,scale*.2)}px 'DM Mono',monospace`;ctx.fillStyle="#8affef";ctx.fillText(TerrariumModel.cellName(piece.position),p.x,p.y+size*.52);}
  ctx.restore();pieceHits.push({piece,center:{x:p.x,y:p.y-size*.2},r:Math.max(20,size*.62),depth:project(piece.position.x+.5,piece.position.y+.5,piece.position.z).depth});
}

function drawCoordinateLabels(){ctx.save();ctx.font=`500 ${Math.max(8,scale*.21)}px 'DM Mono',monospace`;ctx.fillStyle="#71888b";ctx.textAlign="center";for(let x=0;x<8;x++){const p=project(x+.5,-.25,8.03);ctx.fillText(String.fromCharCode(65+x),p.x,p.y);}for(let y=0;y<8;y++){const p=project(-.25,y+.5,8.03);ctx.fillText(y+1,p.x,p.y);}ctx.restore();}
function render(){if(!width||!height)return;ctx.clearRect(0,0,width,height);legalHits=[];pieceHits=[];drawTerrain();drawCoordinateLabels();if(model.selected)for(const target of model.legalMoves())drawHint(target);const pieces=[...model.pieces].sort((a,b)=>project(b.position.x,b.position.y,b.position.z).depth-project(a.position.x,a.position.y,a.position.z).depth);for(const piece of pieces)drawPiece(piece);}

function syncUI(){
  ui.turn.textContent=model.winner?`${model.winner} wins`:model.pendingPromotionPieceId!=null?"Promotion":`${model.turn} to move`;ui.turnChip.classList.toggle("black",model.turn===Side.BLACK);ui.message.textContent=model.message;
  for(const button of ui.planes)button.setAttribute("aria-checked",String(button.dataset.plane===model.plane));
  const piece=model.selected;if(piece)ui.selected.innerHTML=`<span class="piece-token">${glyphs[piece.kind]}</span><div><b>${piece.side} ${piece.kind}</b><small>${TerrariumModel.cellName(piece.position)} · ${model.legalMoves(piece).length} options</small></div>`;else ui.selected.innerHTML='<span class="piece-token">—</span><div><b>None</b><small>Choose one of your pieces</small></div>';
  if(model.pendingPromotionPieceId!=null&&!ui.promotion.open)ui.promotion.showModal();render();
}

function setPlane(plane){if(model.turn!==Side.WHITE||model.winner||model.pendingPromotionPieceId!=null)return;model.setPlane(plane);syncUI();}
function cyclePiece(backward=false){if(model.turn!==Side.WHITE)return;const pieces=model.pieces.filter(p=>p.side===Side.WHITE).sort((a,b)=>a.id-b.id);if(!pieces.length)return;let index=pieces.findIndex(p=>p.id===model.selectedId);index=(index+(backward?-1:1)+pieces.length)%pieces.length;model.select(pieces[index].id);syncUI();}
function reset(){clearTimeout(botTimer);model.reset();positionVersion++;startBotWorker();ui.thinking.hidden=true;if(ui.promotion.open)ui.promotion.close();syncUI();}
function undo(){clearTimeout(botTimer);ui.thinking.hidden=true;if(!model.undo())return;if(model.turn===Side.BLACK&&!model.winner)model.undo();positionVersion++;startBotWorker();syncUI();}

function queueBot(){
  if(model.turn!==Side.BLACK||model.winner||model.pendingPromotionPieceId!=null)return;const version=++positionVersion;ui.thinking.hidden=false;render();
  botTimer=setTimeout(()=>{if(version!==positionVersion||model.turn!==Side.BLACK)return;const state=model.cloneForSimulation();botWorker.postMessage({version,state:{solids:[...state.solids],pieces:state.pieces,turn:state.turn,plane:state.plane,winner:state.winner,message:state.message,selectedId:state.selectedId,enPassantPawnId:state.enPassantPawnId,enPassantTarget:state.enPassantTarget,pendingPromotionPieceId:state.pendingPromotionPieceId,nextId:state.nextId}});},460);
}
function executeMove(target){if(model.tryMove(target)){positionVersion++;syncUI();if(model.pendingPromotionPieceId==null)queueBot();}else syncUI();}
function clickAt(point){
  if(model.turn!==Side.WHITE||model.winner||model.pendingPromotionPieceId!=null)return;
  let hit=[...legalHits].reverse().find(h=>Math.hypot(point.x-h.center.x,point.y-h.center.y)<=h.r);if(hit){executeMove(hit.target);return;}
  const candidates=pieceHits.filter(h=>Math.hypot(point.x-h.center.x,point.y-h.center.y)<=h.r).sort((a,b)=>a.depth-b.depth);if(candidates.length){model.select(candidates[0].piece.id);syncUI();return;}model.clearSelection();syncUI();
}

canvas.addEventListener("pointerdown",e=>{canvas.setPointerCapture(e.pointerId);pointerStart=pointerLast={x:e.offsetX,y:e.offsetY};dragging=false;});
canvas.addEventListener("pointermove",e=>{if(!pointerStart)return;const here={x:e.offsetX,y:e.offsetY},total=Math.hypot(here.x-pointerStart.x,here.y-pointerStart.y);if(total>5)dragging=true;if(dragging){camera.yaw-=(here.x-pointerLast.x)*.009;camera.elevation=Math.max(30,Math.min(60,camera.elevation+(here.y-pointerLast.y)*.18));render();}pointerLast=here;});
canvas.addEventListener("pointerup",e=>{if(pointerStart&&!dragging)clickAt({x:e.offsetX,y:e.offsetY});pointerStart=pointerLast=null;dragging=false;});
canvas.addEventListener("pointercancel",()=>{pointerStart=pointerLast=null;dragging=false;});
canvas.addEventListener("contextmenu",e=>{e.preventDefault();model.clearSelection();syncUI();});
canvas.addEventListener("wheel",e=>{if(!camera.focus)return;e.preventDefault();camera.depth=Math.max(0,Math.min(15,camera.depth-Math.sign(e.deltaY)));ui.depth.value=camera.depth;ui.depthOut.value=`Z${camera.depth}`;render();},{passive:false});

for(const button of ui.planes)button.addEventListener("click",()=>setPlane(button.dataset.plane));
document.querySelector("#undo-button").addEventListener("click",undo);document.querySelector("#restart-button").addEventListener("click",reset);
document.querySelector("#help-button").addEventListener("click",()=>{document.querySelector("#help-button").setAttribute("aria-expanded","true");ui.help.showModal();});
ui.help.addEventListener("close",()=>document.querySelector("#help-button").setAttribute("aria-expanded","false"));
ui.layerToggle.addEventListener("click",()=>{camera.focus=!camera.focus;ui.layerToggle.setAttribute("aria-pressed",camera.focus);ui.layerToggle.querySelector("b").textContent=camera.focus?"Single layer":"All layers";ui.depth.disabled=!camera.focus;render();});
ui.depth.addEventListener("input",()=>{camera.depth=Number(ui.depth.value);ui.depthOut.value=`Z${camera.depth}`;render();});
document.querySelector("#rotate-left").addEventListener("click",()=>{camera.yaw-=Math.PI/2;render();});document.querySelector("#rotate-right").addEventListener("click",()=>{camera.yaw+=Math.PI/2;render();});
for(const button of document.querySelectorAll("[data-kind]"))button.addEventListener("click",()=>{if(model.promote(button.dataset.kind)){ui.promotion.close();positionVersion++;syncUI();queueBot();}});

window.addEventListener("keydown",e=>{
  if(ui.help.open){if(e.key.toLowerCase()==="h")ui.help.close();return;}if(ui.promotion.open)return;
  const key=e.key.toLowerCase();if(["1","2","3"," ","tab","q","e","u","r","h","m","arrowup","arrowdown"].includes(key))e.preventDefault();
  if(key==="1")setPlane(Plane.XY);else if(key==="2")setPlane(Plane.XZ);else if(key==="3")setPlane(Plane.YZ);else if(key===" "){const ps=[Plane.XY,Plane.XZ,Plane.YZ];setPlane(ps[(ps.indexOf(model.plane)+1)%3]);}
  else if(key==="tab")cyclePiece(e.shiftKey);else if(key==="u")undo();else if(key==="r")reset();else if(key==="h")ui.help.showModal();else if(key==="m")ui.layerToggle.click();else if(key==="q"){camera.yaw-=Math.PI/2;render();}else if(key==="e"){camera.yaw+=Math.PI/2;render();}else if(key==="arrowup"){camera.height=Math.max(-7,camera.height-1);render();}else if(key==="arrowdown"){camera.height=Math.min(7,camera.height+1);render();}else if(key==="escape"){model.clearSelection();syncUI();}
});

syncUI();
