import { Side, Kind, Plane, Outcome } from "./engine.js";

const TAU = Math.PI * 2;
const vec = (x=0,y=0,z=0)=>({x,y,z});
const add=(a,b)=>vec(a.x+b.x,a.y+b.y,a.z+b.z), sub=(a,b)=>vec(a.x-b.x,a.y-b.y,a.z-b.z);
const mul=(a,n)=>vec(a.x*n,a.y*n,a.z*n), dot=(a,b)=>a.x*b.x+a.y*b.y+a.z*b.z;
const cross=(a,b)=>vec(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
const norm=a=>{const n=Math.hypot(a.x,a.y,a.z)||1;return mul(a,1/n)};
const color=(r,g,b,a=255)=>[r/255,g/255,b/255,a/255];
const shade=(c,n)=>[Math.min(1,c[0]*n),Math.min(1,c[1]*n),Math.min(1,c[2]*n),c[3]];
const alpha=(c,n)=>[c[0],c[1],c[2],c[3]*n];
const rotateX=(p,a)=>vec(p.x,p.y*Math.cos(a)-p.z*Math.sin(a),p.y*Math.sin(a)+p.z*Math.cos(a));

class MeshBuilder {
  constructor(){this.opaque=[];this.transparent=[]}
  tri(a,b,c,col,transparent=false){
    const triangle={points:[a,b,c],color:col,center:mul(add(add(a,b),c),1/3)};
    (transparent?this.transparent:this.opaque).push(triangle);
  }
  quad(a,b,c,d,col,transparent=false,doubleSided=false){
    this.tri(a,b,c,col,transparent);this.tri(a,c,d,col,transparent);
    if(doubleSided){this.tri(c,b,a,col,transparent);this.tri(d,c,a,col,transparent)}
  }
  cylinder(center,radius,height,segments,col,transparent=false){
    const top=center.z+height;
    for(let i=0;i<segments;i++){
      const a=i*TAU/segments,b=(i+1)*TAU/segments;
      const p0=add(center,vec(Math.cos(a)*radius,Math.sin(a)*radius,0));
      const p1=add(center,vec(Math.cos(b)*radius,Math.sin(b)*radius,0));
      const p2=vec(p1.x,p1.y,top),p3=vec(p0.x,p0.y,top),face=shade(col,.68+.25*Math.max(0,Math.sin(a)));
      this.quad(p0,p1,p2,p3,face,transparent);
      this.tri(vec(center.x,center.y,top),p3,p2,col,transparent);
      this.tri(center,p1,p0,shade(col,.55),transparent);
    }
  }
  cone(center,radius,height,segments,col,transparent=false,rotation=0){
    const transform=p=>add(center,rotation?rotateX(p,rotation):p),tip=transform(vec(0,0,height));
    for(let i=0;i<segments;i++){
      const a=i*TAU/segments,b=(i+1)*TAU/segments,p0=transform(vec(Math.cos(a)*radius,Math.sin(a)*radius,0)),p1=transform(vec(Math.cos(b)*radius,Math.sin(b)*radius,0));
      this.tri(p0,p1,tip,shade(col,.72+.24*Math.max(0,Math.sin(a))),transparent);
      this.tri(transform(vec(0,0,0)),p1,p0,shade(col,.55),transparent);
    }
  }
  sphere(center,radius,longitude,latitude,col,transparent=false){
    const point=(p,a)=>add(center,mul(vec(Math.cos(p)*Math.cos(a),Math.cos(p)*Math.sin(a),Math.sin(p)),radius));
    for(let lat=0;lat<latitude;lat++){
      const p0=-Math.PI/2+lat*Math.PI/latitude,p1=-Math.PI/2+(lat+1)*Math.PI/latitude;
      for(let lon=0;lon<longitude;lon++){
        const a0=lon*TAU/longitude,a1=(lon+1)*TAU/longitude;
        this.quad(point(p0,a0),point(p0,a1),point(p1,a1),point(p1,a0),shade(col,.82+.18*Math.sin(p1)),transparent);
      }
    }
  }
  box(center,size,col,transparent=false){
    const mn=sub(center,mul(size,.5)),mx=add(center,mul(size,.5));
    this.quad(vec(mn.x,mn.y,mx.z),vec(mx.x,mn.y,mx.z),vec(mx.x,mx.y,mx.z),vec(mn.x,mx.y,mx.z),col,transparent);
    this.quad(vec(mn.x,mx.y,mn.z),vec(mx.x,mx.y,mn.z),vec(mx.x,mn.y,mn.z),vec(mn.x,mn.y,mn.z),shade(col,.48),transparent);
    this.quad(vec(mx.x,mn.y,mn.z),vec(mx.x,mx.y,mn.z),vec(mx.x,mx.y,mx.z),vec(mx.x,mn.y,mx.z),shade(col,.72),transparent);
    this.quad(vec(mn.x,mx.y,mn.z),vec(mn.x,mn.y,mn.z),vec(mn.x,mn.y,mx.z),vec(mn.x,mx.y,mx.z),shade(col,.58),transparent);
    this.quad(vec(mn.x,mn.y,mn.z),vec(mx.x,mn.y,mn.z),vec(mx.x,mn.y,mx.z),vec(mn.x,mn.y,mx.z),shade(col,.57),transparent);
    this.quad(vec(mx.x,mx.y,mn.z),vec(mn.x,mx.y,mn.z),vec(mn.x,mx.y,mx.z),vec(mx.x,mx.y,mx.z),shade(col,.68),transparent);
  }
  planeBox(center,u,v,size,col){
    const hu=mul(u,size.x/2),hv=mul(v,size.y/2),hw=vec(0,0,size.z/2),b00=sub(sub(sub(center,hu),hv),hw),b10=add(sub(sub(center,hv),hw),hu),b11=add(add(sub(center,hw),hu),hv),b01=add(sub(sub(center,hu),hw),hv),t00=add(b00,mul(hw,2)),t10=add(b10,mul(hw,2)),t11=add(b11,mul(hw,2)),t01=add(b01,mul(hw,2));
    this.quad(t00,t10,t11,t01,col);this.quad(b01,b11,b10,b00,shade(col,.48));this.quad(b10,b11,t11,t10,shade(col,.72));this.quad(b01,b00,t00,t01,shade(col,.58));this.quad(b00,b10,t10,t00,shade(col,.57));this.quad(b11,b01,t01,t11,shade(col,.68));
  }
  disc(center,radius,segments,col){for(let i=0;i<segments;i++){const a=i*TAU/segments,b=(i+1)*TAU/segments;this.tri(center,add(center,vec(Math.cos(a)*radius,Math.sin(a)*radius,0)),add(center,vec(Math.cos(b)*radius,Math.sin(b)*radius,0)),col,true)}}
  ring(center,radius,width,segments,col){for(let i=0;i<segments;i++){const a=i*TAU/segments,b=(i+1)*TAU/segments,p=(t,r)=>add(center,vec(Math.cos(t)*r,Math.sin(t)*r,0));this.quad(p(a,radius),p(b,radius),p(b,radius-width),p(a,radius-width),col,true)}}
  planeFrame(center,u,v,size,col,width,normal=cross(u,v)){
    const h=size/2,q=(a,b,c,d)=>dot(cross(sub(b,a),sub(c,a)),normal)>=0?this.quad(a,b,c,d,col,true):this.quad(d,c,b,a,col,true);
    q(sub(sub(center,mul(u,h)),mul(v,h)),add(sub(center,mul(v,h)),mul(u,h)),add(sub(center,mul(v,h-width)),mul(u,h)),sub(sub(center,mul(v,h-width)),mul(u,h)));
    q(add(sub(center,mul(u,h)),mul(v,h-width)),add(add(center,mul(u,h)),mul(v,h-width)),add(add(center,mul(u,h)),mul(v,h)),add(sub(center,mul(u,h)),mul(v,h)));
    q(sub(sub(center,mul(u,h)),mul(v,h)),sub(add(center,mul(v,h)),mul(u,h)),sub(add(center,mul(v,h)),mul(u,h-width)),sub(sub(center,mul(v,h)),mul(u,h-width)));
    q(sub(add(center,mul(u,h-width)),mul(v,h)),sub(add(center,mul(u,h)),mul(v,h)),add(add(center,mul(u,h)),mul(v,h)),add(add(center,mul(u,h-width)),mul(v,h)));
  }
}

function compile(gl,type,source){const s=gl.createShader(type);gl.shaderSource(s,source);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw new Error(gl.getShaderInfoLog(s));return s}
function program(gl){
  const vs=compile(gl,gl.VERTEX_SHADER,`#version 300 es
    in vec3 a_position; in vec4 a_color; out vec4 v_color;
    uniform vec3 u_camera; uniform vec3 u_right; uniform vec3 u_up; uniform vec3 u_forward;
    uniform vec2 u_halfSize; uniform vec2 u_depth;
    void main(){vec3 d=a_position-u_camera;float depth=dot(d,u_forward);gl_Position=vec4(dot(d,u_right)/u_halfSize.x,dot(d,u_up)/u_halfSize.y,((depth-u_depth.x)/(u_depth.y-u_depth.x))*2.0-1.0,1.0);v_color=a_color;}`);
  const fs=compile(gl,gl.FRAGMENT_SHADER,`#version 300 es
    precision mediump float; in vec4 v_color; out vec4 outColor; void main(){outColor=v_color;}`);
  const p=gl.createProgram();gl.attachShader(p,vs);gl.attachShader(p,fs);gl.linkProgram(p);if(!gl.getProgramParameter(p,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(p));return p;
}

export class WorldRenderer {
  constructor(canvas){
    this.canvas=canvas;this.gl=canvas.getContext("webgl2",{alpha:false,antialias:true,premultipliedAlpha:false});
    if(!this.gl)throw new Error("WebGL 2 is required to render the 3D arena.");
    const gl=this.gl;this.program=program(gl);this.buffer=gl.createBuffer();
    this.locations={position:gl.getAttribLocation(this.program,"a_position"),color:gl.getAttribLocation(this.program,"a_color"),camera:gl.getUniformLocation(this.program,"u_camera"),right:gl.getUniformLocation(this.program,"u_right"),up:gl.getUniformLocation(this.program,"u_up"),forward:gl.getUniformLocation(this.program,"u_forward"),halfSize:gl.getUniformLocation(this.program,"u_halfSize"),depth:gl.getUniformLocation(this.program,"u_depth")};
    this.yaw=0;this.elevation=45;this.heightOffset=0;this.layerFocus=false;this.focusLayer=8;this.width=1;this.height=1;this.pixelRatio=1;this.updateCamera();
    gl.useProgram(this.program);gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer);gl.enableVertexAttribArray(this.locations.position);gl.vertexAttribPointer(this.locations.position,3,gl.FLOAT,false,28,0);gl.enableVertexAttribArray(this.locations.color);gl.vertexAttribPointer(this.locations.color,4,gl.FLOAT,false,28,12);
    gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.enable(gl.CULL_FACE);gl.cullFace(gl.BACK);gl.frontFace(gl.CCW);
  }
  resize(){
    const r=this.canvas.getBoundingClientRect();this.pixelRatio=Math.min(2,window.devicePixelRatio||1);this.width=Math.max(1,r.width);this.height=Math.max(1,r.height);
    const w=Math.round(this.width*this.pixelRatio),h=Math.round(this.height*this.pixelRatio);if(this.canvas.width!==w||this.canvas.height!==h){this.canvas.width=w;this.canvas.height=h}this.gl.viewport(0,0,w,h);this.updateCamera();
  }
  updateCamera(){
    this.target=vec(3.5,3.5,7.4+this.heightOffset);const d=13,e=d*Math.tan(this.elevation*Math.PI/180);
    this.camera=add(this.target,vec(Math.sin(this.yaw)*d,-Math.cos(this.yaw)*d,e));this.forward=norm(sub(this.target,this.camera));this.right=norm(cross(this.forward,vec(0,0,1)));this.up=norm(cross(this.right,this.forward));
    this.orthoHeight=17.6;this.orthoWidth=this.orthoHeight*(this.width/this.height);
  }
  project(p){const d=sub(p,this.camera),x=dot(d,this.right)/(this.orthoWidth/2),y=dot(d,this.up)/(this.orthoHeight/2);return{x:(x*.5+.5)*this.width,y:(.5-y*.5)*this.height,depth:dot(d,this.forward)}}
  targetPoint(model,target){return this.moveGeometry(model,target,model.isExcavationTarget(target)).center}
  moveGeometry(model,target,excavation){
    if(model.plane===Plane.XZ){const n=this.camera.y>=target.y?vec(0,1,0):vec(0,-1,0),off=excavation?.505:.012;return{center:add(vec(target.x,target.y,target.z+.49),mul(n,off)),u:vec(1,0,0),v:vec(0,0,1),normal:n}}
    if(model.plane===Plane.YZ){const n=this.camera.x>=target.x?vec(1,0,0):vec(-1,0,0),off=excavation?.505:.012;return{center:add(vec(target.x,target.y,target.z+.49),mul(n,off)),u:vec(0,1,0),v:vec(0,0,1),normal:n}}
    return{center:vec(target.x,target.y,excavation?target.z+1.025:target.z+.025),u:vec(1,0,0),v:vec(0,1,0),normal:vec(0,0,1)};
  }
  solid(solids,x,y,z){return x>=0&&x<8&&y>=0&&y<8&&z>=0&&z<16&&solids[x*128+y*16+z]===1}
  terrainColor(x,y,z,factor=1,opacity=1){const c=(x+y+z)%2===0?color(237,214,176):color(184,135,98);return alpha(shade(c,factor),opacity)}
  cellFace(mesh,x,y,z,face,col,transparent=false){
    const mn=vec(x-.49,y-.49,z),mx=vec(x+.49,y+.49,z+.98),q=(a,b,c,d)=>mesh.quad(a,b,c,d,col,transparent);
    if(face==="top")q(vec(mn.x,mn.y,mx.z),vec(mx.x,mn.y,mx.z),vec(mx.x,mx.y,mx.z),vec(mn.x,mx.y,mx.z));
    else if(face==="bottom")q(vec(mn.x,mx.y,mn.z),vec(mx.x,mx.y,mn.z),vec(mx.x,mn.y,mn.z),vec(mn.x,mn.y,mn.z));
    else if(face==="east")q(vec(mx.x,mn.y,mn.z),vec(mx.x,mx.y,mn.z),vec(mx.x,mx.y,mx.z),vec(mx.x,mn.y,mx.z));
    else if(face==="west")q(vec(mn.x,mx.y,mn.z),vec(mn.x,mn.y,mn.z),vec(mn.x,mn.y,mx.z),vec(mn.x,mx.y,mx.z));
    else if(face==="north")q(vec(mn.x,mn.y,mn.z),vec(mx.x,mn.y,mn.z),vec(mx.x,mn.y,mx.z),vec(mn.x,mn.y,mx.z));
    else q(vec(mx.x,mx.y,mn.z),vec(mn.x,mx.y,mn.z),vec(mn.x,mx.y,mx.z),vec(mx.x,mx.y,mx.z));
  }
  buildTerrain(mesh,solids){
    for(let x=0;x<8;x++)for(let y=0;y<8;y++)for(let z=0;z<16;z++){
      if(!this.solid(solids,x,y,z)||this.layerFocus&&z!==this.focusLayer)continue;
      if(this.layerFocus){const top=this.terrainColor(x,y,z,1.18),side=this.terrainColor(x,y,z,.78);this.cellFace(mesh,x,y,z,"top",top);this.cellFace(mesh,x,y,z,"bottom",side);if(!this.solid(solids,x+1,y,z))this.cellFace(mesh,x,y,z,"east",side);if(!this.solid(solids,x-1,y,z))this.cellFace(mesh,x,y,z,"west",side);if(!this.solid(solids,x,y-1,z))this.cellFace(mesh,x,y,z,"north",side);if(!this.solid(solids,x,y+1,z))this.cellFace(mesh,x,y,z,"south",side);continue}
      const top=this.terrainColor(x,y,z,1),a=this.terrainColor(x,y,z,.68),b=this.terrainColor(x,y,z,.51);
      if(!this.solid(solids,x,y,z+1))this.cellFace(mesh,x,y,z,"top",top);if(!this.solid(solids,x,y,z-1))this.cellFace(mesh,x,y,z,"bottom",b);if(!this.solid(solids,x+1,y,z))this.cellFace(mesh,x,y,z,"east",a);if(!this.solid(solids,x-1,y,z))this.cellFace(mesh,x,y,z,"west",b);if(!this.solid(solids,x,y+1,z))this.cellFace(mesh,x,y,z,"south",a);if(!this.solid(solids,x,y-1,z))this.cellFace(mesh,x,y,z,"north",b);
    }
    if(this.layerFocus){const z=this.focusLayer+.012,c=vec(3.5,3.5,z);mesh.quad(vec(-.55,-.55,z),vec(7.55,-.55,z),vec(7.55,7.55,z),vec(-.55,7.55,z),color(65,235,213,22),true);mesh.planeFrame(c,vec(1,0,0),vec(0,1,0),8.18,color(93,255,229,155),.035,vec(0,0,1))}
  }
  buildHints(mesh,model,moves){
    for(const target of moves){const out=model.predictOutcome(target),col=out===Outcome.FATAL?color(255,76,86,215):(out===Outcome.EXCAVATION||out===Outcome.CRATER)?color(255,185,72,210):color(72,245,209,190),g=this.moveGeometry(model,target,model.isExcavationTarget(target)),h=.39;
      const a=sub(sub(g.center,mul(g.u,h)),mul(g.v,h)),b=add(sub(g.center,mul(g.v,h)),mul(g.u,h)),c=add(add(g.center,mul(g.u,h)),mul(g.v,h)),d=add(sub(g.center,mul(g.u,h)),mul(g.v,h));
      if(dot(cross(sub(b,a),sub(c,a)),g.normal)>=0)mesh.quad(a,b,c,d,col,true);else mesh.quad(d,c,b,a,col,true);mesh.planeFrame(add(g.center,mul(g.normal,.006)),g.u,g.v,.78,shade(col,1.3),.025,g.normal);
    }
  }
  buildClues(mesh,model,solids){
    const glyphs={0:"abcdef",1:"bc",2:"abdeg",3:"abcdg",4:"bcfg",5:"acdfg",6:"acdefg",7:"abc",8:"abcdefg",9:"abcdfg"};
    const segments={a:[0,.25,1],b:[.16,.13,0],c:[.16,-.13,0],d:[0,-.25,1],e:[-.16,-.13,0],f:[-.16,.13,0],g:[0,0,1]};
    for(const encoded of model.revealedClues??[]){const[x,y,z]=encoded.split(",").map(Number),clue=model.clueAt({x,y,z});if(clue==null||(!this.layerFocus&&clue===0)||(this.layerFocus&&z!==this.focusLayer)||this.solid(solids,x,y,z))continue;
      const digits=String(clue),scale=digits.length>1?.68:1,col=clue>=4?color(255,102,122):clue>=2?color(255,209,102):clue===0?color(54,115,119):color(127,255,240),away=norm(vec(x-this.camera.x,y-this.camera.y,0)),right=vec(away.y,-away.x,0);
      for(let index=0;index<digits.length;index++){const offset=(index-(digits.length-1)/2)*.38*scale;for(const id of glyphs[digits[index]]??[]){const[sx,sy,horizontal]=segments[id],center=add(vec(x,y,z+.055),add(mul(right,offset+sx*scale),mul(away,sy*scale))),size=horizontal?vec(.30*scale,.065*scale,.07):vec(.065*scale,.23*scale,.07);mesh.planeBox(center,right,away,size,col)}}
    }
  }
  buildPiece(mesh,rendered,selected){
    const p=rendered.piece,o=rendered.opacity,transparent=o<.98,body=alpha(p.side===Side.WHITE?color(235,225,199):color(35,41,55),o),trim=alpha(p.side===Side.WHITE?color(73,218,200):color(231,83,109),o),dark=alpha(p.side===Side.WHITE?color(154,158,146):color(12,16,24),o),at=rendered.position;
    if(selected){mesh.disc(add(at,vec(0,0,.015)),.48,24,alpha(color(78,255,224,115),o));mesh.ring(add(at,vec(0,0,.021)),.49,.035,28,alpha(color(119,255,233),o))}
    mesh.cylinder(at,.34,.11,20,trim,transparent);mesh.cylinder(add(at,vec(0,0,.09)),.28,.10,20,body,transparent);mesh.cone(add(at,vec(0,0,.18)),.22,.42,20,dark,transparent);const crown=add(at,vec(0,0,.57));
    if(p.kind===Kind.PAWN)mesh.sphere(crown,.16,10,8,body,transparent);
    else if(p.kind===Kind.ROOK){mesh.cylinder(add(crown,vec(0,0,-.03)),.22,.26,12,body,transparent);for(let i=0;i<4;i++){const a=i*Math.PI/2;mesh.box(add(crown,vec(Math.cos(a)*.15,Math.sin(a)*.15,.17)),vec(.14,.14,.16),trim,transparent)}}
    else if(p.kind===Kind.KNIGHT){mesh.sphere(add(crown,vec(0,0,.02)),.19,9,7,body,transparent);mesh.cone(add(crown,vec(0,-.04,.05)),.18,.42,12,body,transparent,-.72);mesh.sphere(add(crown,vec(0,-.24,.31)),.12,9,7,body,transparent)}
    else if(p.kind===Kind.BISHOP){mesh.sphere(crown,.19,12,8,body,transparent);mesh.cone(add(crown,vec(0,0,.16)),.12,.30,12,trim,transparent)}
    else if(p.kind===Kind.TRISHOP){mesh.sphere(crown,.18,12,8,body,transparent);for(let i=0;i<3;i++){const a=i*TAU/3;mesh.cone(add(crown,vec(Math.cos(a)*.12,Math.sin(a)*.12,.10)),.08,.30,8,trim,transparent)}}
    else if(p.kind===Kind.QUEEN){mesh.cone(add(crown,vec(0,0,-.02)),.25,.28,16,body,transparent);for(let i=0;i<5;i++){const a=i*TAU/5;mesh.sphere(add(crown,vec(Math.cos(a)*.20,Math.sin(a)*.20,.27)),.065,7,5,trim,transparent)}}
    else if(p.kind===Kind.KING){mesh.sphere(crown,.18,12,8,body,transparent);mesh.box(add(crown,vec(0,0,.28)),vec(.07,.07,.33),trim,transparent);mesh.box(add(crown,vec(0,0,.34)),vec(.28,.07,.07),trim,transparent)}
  }
  vertices(triangles){const data=new Float32Array(triangles.length*21);let i=0;for(const t of triangles)for(const p of t.points){data[i++]=p.x;data[i++]=p.y;data[i++]=p.z;data[i++]=t.color[0];data[i++]=t.color[1];data[i++]=t.color[2];data[i++]=t.color[3]}return data}
  drawTriangles(triangles,transparent=false){if(!triangles.length)return;const gl=this.gl,data=this.vertices(triangles);gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer);gl.bufferData(gl.ARRAY_BUFFER,data,gl.DYNAMIC_DRAW);if(transparent){gl.enable(gl.BLEND);gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);gl.depthMask(false)}else{gl.disable(gl.BLEND);gl.depthMask(true)}gl.drawArrays(gl.TRIANGLES,0,data.length/7);if(transparent){gl.depthMask(true);gl.disable(gl.BLEND)}}
  render(model,solids,pieces,moves){
    this.resize();const mesh=new MeshBuilder();this.buildTerrain(mesh,solids);this.buildClues(mesh,model,solids);this.buildHints(mesh,model,moves);for(const p of [...pieces].sort((a,b)=>dot(sub(b.position,this.camera),sub(b.position,this.camera))-dot(sub(a.position,this.camera),sub(a.position,this.camera))))this.buildPiece(mesh,p,p.piece.id===model.selectedId);
    mesh.transparent.sort((a,b)=>dot(sub(b.center,this.camera),sub(b.center,this.camera))-dot(sub(a.center,this.camera),sub(a.center,this.camera)));
    const gl=this.gl,l=this.locations;gl.clearColor(7/255,11/255,20/255,1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.useProgram(this.program);gl.uniform3f(l.camera,this.camera.x,this.camera.y,this.camera.z);gl.uniform3f(l.right,this.right.x,this.right.y,this.right.z);gl.uniform3f(l.up,this.up.x,this.up.y,this.up.z);gl.uniform3f(l.forward,this.forward.x,this.forward.y,this.forward.z);gl.uniform2f(l.halfSize,this.orthoWidth/2,this.orthoHeight/2);gl.uniform2f(l.depth,.1,80);this.drawTriangles(mesh.opaque);this.drawTriangles(mesh.transparent,true);
  }
}
