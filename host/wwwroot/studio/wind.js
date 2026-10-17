// This page owns family selection, painted drafts, exact preview and source save.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const state = { session: {}, draft: null, rgba: null, mask: null, catalog: null, shader: '', dirty: false, busy: false, signature: null, undo: [] };
const notice = (text, error = false) => { $('notice').textContent = text; $('notice').classList.toggle('error', error); };
const request = createRequest(() => state.session, () => { state.session.can_edit = false; controls(); });
const decode = text => Uint8Array.from(atob(text), c => c.charCodeAt(0));
const encode = bytes => { let text = ''; for (const byte of bytes) text += String.fromCharCode(byte); return btoa(text); };
function controls() {
    for (const id of ['load', 'create', 'login']) $(id).disabled = state.busy;
    for (const id of ['enabled', 'profile', 'brush', 'size', 'undo', 'preview']) $(id).disabled = state.busy || !state.session.can_edit;
    $('save').disabled = state.busy || !state.session.can_edit || !state.signature;
    $('connection').textContent = state.session.can_edit ? 'Owner access · source assets' : 'Read only';
    $('login').hidden = !state.session.configured || state.session.authenticated;
}
async function work(action) { if (state.busy) return; state.busy = true; controls(); try { await action(); } catch (e) { state.signature = null; notice(e.message, true); } finally { state.busy = false; controls(); } }
function changed() { state.dirty = true; state.signature = null; $('review').textContent = ''; controls(); drawMask(); renderWind(); }
function mayReplace() { return !state.dirty || confirm('Discard this unsaved wind draft?'); }
function setDraft(draft) {
    state.draft = draft; state.rgba = decode(draft.rgba); state.mask = decode(draft.mask); state.undo = []; state.signature = null; state.dirty = false;
    $('editor').hidden = false; $('title').textContent = draft.id; $('enabled').checked = draft.enabled; $('profile').value = draft.profile;
    const canvas = $('mask'); canvas.width = draft.width; canvas.height = draft.height; canvas.style.width = `${draft.width * 3}px`; canvas.style.height = `${draft.height * 3}px`;
    $('review').textContent = ''; drawMask(); renderWind(); controls();
}
function drawMask() {
    if (!state.draft) return;
    const pixels = new Uint8ClampedArray(state.rgba);
    if ($('overlay').checked) for (let i = 0; i < state.mask.length; i++) if (state.mask[i]) {
        pixels[i*4] = Math.round(pixels[i*4]*.55 + (state.mask[i] === 127 ? 115 : 0));
        pixels[i*4+1] = Math.round(pixels[i*4+1]*.55 + (state.mask[i] === 255 ? 115 : 0)); pixels[i*4+2] *= .55;
    }
    $('mask').getContext('2d').putImageData(new ImageData(pixels, state.draft.width, state.draft.height), 0, 0);
}
let painting = false, last = null;
function paint(event) {
    if (!painting) return;
    const rect = $('mask').getBoundingClientRect(), d = state.draft;
    const point = [Math.floor((event.clientX-rect.left)*d.width/rect.width), Math.floor((event.clientY-rect.top)*d.height/rect.height)];
    const from = last || point, steps = Math.max(Math.abs(point[0]-from[0]), Math.abs(point[1]-from[1]), 1), radius = Math.max(1, Math.min(24, Number($('size').value)||1))/2;
    for (let step=0; step<=steps; step++) {
        const cx = Math.round(from[0]+(point[0]-from[0])*step/steps), cy = Math.round(from[1]+(point[1]-from[1])*step/steps);
        for (let y=Math.max(0,Math.floor(cy-radius)); y<Math.min(d.height,cy+radius); y++) for (let x=Math.max(0,Math.floor(cx-radius)); x<Math.min(d.width,cx+radius); x++) {
            const i=y*d.width+x; if (state.rgba[i*4+3]) state.mask[i]=Number($('brush').value);
        }
    }
    last=point; changed();
}
$('mask').onpointerdown = e => { if (!state.draft || state.busy || !state.session.can_edit) return; painting=true; last=null; state.undo.push(state.mask.slice()); if (state.undo.length>30) state.undo.shift(); $('mask').setPointerCapture(e.pointerId); paint(e); };
$('mask').onpointermove = paint;
$('mask').onpointerup = $('mask').onpointercancel = () => { painting=false; last=null; };
$('undo').onclick = () => { if (state.undo.length) { state.mask=state.undo.pop(); changed(); } };
$('overlay').onchange = drawMask;
$('enabled').onchange = $('profile').onchange = changed;
// Adapt only shader declarations/entry points. The movement algorithm is served
// verbatim from the game's resource, so authoring has no second wind implementation.
let gl, program, textures;
function setupShader(source) {
    gl = $('wind').getContext('webgl2', { alpha:true, premultipliedAlpha:false, preserveDrawingBuffer:true });
    if (!gl) throw new Error('WebGL 2 is required for the game shader preview.');
    const compile = (type, text) => { const shader=gl.createShader(type); gl.shaderSource(shader,text); gl.compileShader(shader); if (!gl.getShaderParameter(shader,gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader)); return shader; };
    const fragment = source.replace(/shader_type canvas_item;/,'').replace(/varying vec4 tint;/,'').replace(/void vertex\(\) \{[^}]*\}/,'')
        .replace(/ : filter_nearest, repeat_disable/g,'').replace(/(uniform \w+ \w+) = [^;]+;/g,'$1;').replace('void fragment()', 'void main()').replace(/\bCOLOR\b/g,'output_color').replace(/\btint\b/g,'vec4(1.0)');
    program=gl.createProgram();
    gl.attachShader(program,compile(gl.VERTEX_SHADER,'#version 300 es\nin vec2 position; out vec2 UV; void main(){UV=vec2((position.x+1.0)*.5,(1.0-position.y)*.5);gl_Position=vec4(position,0,1);}'));
    gl.attachShader(program,compile(gl.FRAGMENT_SHADER,'#version 300 es\nprecision highp float; precision highp int; in vec2 UV; out vec4 output_color; uniform sampler2D TEXTURE; uniform float TIME;\n'+fragment));
    gl.linkProgram(program); if (!gl.getProgramParameter(program,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program)); gl.useProgram(program);
    const buffer=gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER,buffer); gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,1,1]),gl.STATIC_DRAW);
    const position=gl.getAttribLocation(program,'position'); gl.enableVertexAttribArray(position); gl.vertexAttribPointer(position,2,gl.FLOAT,false,0,0);
    textures=[gl.createTexture(),gl.createTexture(),gl.createTexture()];
}
function renderWind() {
    if (!gl || !state.draft) return;
    const d=state.draft,w=d.width+6,h=d.height+6,art=new Uint8Array(w*h*4),mask=new Uint8Array(w*h*4),coordinates=new Uint8Array(w*h*4);
    for(let y=0;y<h;y++) for(let x=0;x<w;x++) {
        const p=(y*w+x)*4; coordinates.set([0,x,y,255],p); mask.set([0,x,y,255],p);
        if(x>=3 && x<w-3 && y>=3 && y<h-3) { const i=(y-3)*d.width+x-3; art.set(state.rgba.subarray(i*4,i*4+4),p); mask[p]=$('enabled').checked ? state.mask[i] : (state.rgba[i*4+3] ? 255 : 0); }
    }
    $('wind').width=w; $('wind').height=h; $('wind').style.width=`${w*3}px`; $('wind').style.height=`${h*3}px`; gl.viewport(0,0,w,h); gl.useProgram(program);
    [coordinates,art,mask].forEach((bytes,i)=>{ gl.activeTexture(gl.TEXTURE0+i); gl.bindTexture(gl.TEXTURE_2D,textures[i]); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE); gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,w,h,0,gl.RGBA,gl.UNSIGNED_BYTE,bytes); gl.uniform1i(gl.getUniformLocation(program,['TEXTURE','canopy_texture','foliage_mask'][i]),i); });
    const profile=state.catalog.profiles[$('profile').value];
    const vec=(name,value)=>gl.uniform2fv(gl.getUniformLocation(program,name),value);
    vec('tile_origin',[0,0]); vec('art_size',[d.width,d.height]); vec('plant_size',d.family.plant_size); vec('motion',profile.motion);
    gl.uniform1i(gl.getUniformLocation(program,'stem_motion'),profile.stem?1:0); gl.uniform1f(gl.getUniformLocation(program,'phase_delay'),profile.phase);
    drawFrame();
}
function drawFrame() { if (!gl || !state.draft) return; gl.uniform1i(gl.getUniformLocation(program,'frozen_frame'),Number($('frame').value)); gl.drawArrays(gl.TRIANGLE_STRIP,0,4); }
setInterval(()=>{ if ($('playing').checked && state.draft) { $('frame').value=(Number($('frame').value)+1)%8; drawFrame(); } },250);
$('frame').oninput=()=>{ $('playing').checked=false; drawFrame(); };
function body() { return { id:state.draft.id, expectedHash:state.draft.hash, enabled:$('enabled').checked, profile:$('profile').value, mask:encode(state.mask), source:state.draft.source, previewSignature:state.signature }; }
$('load').onclick=()=>{ if(mayReplace()) work(async()=>{ setDraft(await request('/wind/family?id='+encodeURIComponent($('family').value))); notice('Family loaded.'); }); };
$('create').onclick=()=>{ if(mayReplace()) work(async()=>{ const query=new URLSearchParams(Object.fromEntries(['id','file','tile','columns','rows'].map(id=>[id,$(id).value]))); setDraft(await request('/wind/source?'+query)); notice('New draft. All opaque pixels start fixed.'); }); };
$('preview').onclick=()=>work(async()=>{ const result=await request('/wind/preview',{method:'POST',body:body()}); state.signature=result.preview_signature; $('review').textContent=result.scope; notice('Validated. Save applies these exact settings.'); });
$('save').onclick=()=>work(async()=>{ const result=await request('/wind/save',{method:'PUT',body:body()}); setDraft(result.draft); await catalog(); $('family').value=result.draft.id; notice(result.message); });
async function catalog() { state.catalog=await request('/wind'); $('family').replaceChildren(...state.catalog.families.map(f=>new Option(f.id+(f.enabled?'':' · disabled'),f.id))); $('profile').replaceChildren(...Object.keys(state.catalog.profiles).map(p=>new Option(p,p))); if(state.draft) $('profile').value=state.draft.profile; }
$('login').onclick=()=>$('signin').showModal(); $('cancel').onclick=()=>$('signin').close();
$('signin-form').onsubmit=e=>{ e.preventDefault(); work(async()=>{ await request('/login',{method:'POST',body:{password:$('password').value}}); $('password').value=''; $('signin').close(); state.session=await request('/session'); await catalog(); if (!gl) setupShader((await request('/wind/shader')).source); notice('Signed in.'); }); };
installNavigation(()=>state.dirty,()=>state.busy,notice);
work(async()=>{ state.session=await request('/session'); await catalog(); setupShader((await request('/wind/shader')).source); const query=new URLSearchParams(location.search); if(query.has('file')) { $('file').value=query.get('file'); $('tile').value=query.get('tile')||'0'; $('new').open=true; } notice('Choose an existing family or open a native tile rectangle.'); });
