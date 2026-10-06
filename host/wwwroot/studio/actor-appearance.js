// Pixel rendering is shared by actor workspaces. Calibration owns its own draft,
// whole-catalog hash, review and uncertain-write recovery, independent of actor DB saves.
const clone=value=>structuredClone(value);
function el(tag,text,cls){const node=document.createElement(tag);if(text!=null)node.textContent=text;if(cls)node.className=cls;return node;}
function btn(text,action){const b=el('button',text);b.type='button';b.addEventListener('click',action);return b;}
const images=new Map();
async function image(url){
    if(!url)throw new Error('Exact image is unavailable.');
    if(!images.has(url))images.set(url,new Promise((resolve,reject)=>{const img=new Image();img.onload=()=>resolve(img);img.onerror=()=>{images.delete(url);reject(new Error('Could not load a confined actor image.'));};img.src=url;}));
    return images.get(url);
}
export async function drawActorPreview(canvas,preview,current=()=>true){
    const base=await image(preview.base_url),cosmetics=await Promise.all((preview.cosmetics||[]).map(async c=>({...c,image:await image(c.url)})));
    if(!current())return;
    const ctx=canvas.getContext('2d');ctx.clearRect(0,0,canvas.width,canvas.height);ctx.imageSmoothingEnabled=false;
    const minX=Math.min(0,...cosmetics.map(c=>c.x)),minY=Math.min(0,...cosmetics.map(c=>c.y)),maxX=Math.max(base.width,...cosmetics.map(c=>c.x+c.image.width)),maxY=Math.max(base.height,...cosmetics.map(c=>c.y+c.image.height));
    const scale=Math.min((canvas.width-48)/(maxX-minX),(canvas.height-48)/(maxY-minY),8);ctx.save();ctx.translate((canvas.width-(maxX-minX)*scale)/2-minX*scale,(canvas.height-(maxY-minY)*scale)/2-minY*scale);ctx.scale(scale,scale);
    const layers=[{z:0,draw:()=>ctx.drawImage(base,0,0)},...cosmetics.map(c=>({z:c.z_index,draw:()=>{ctx.save();ctx.translate(c.x,c.y);if(c.flip_x){ctx.translate(c.image.width,0);ctx.scale(-1,1);}ctx.drawImage(c.image,0,0);ctx.restore();}})),...(preview.foreground_overlays||[]).map(o=>({z:o.z_index,draw:()=>{const r=o.source_rect;ctx.drawImage(base,r.x,r.y,r.width,r.height,o.x,o.y,r.width,r.height);}}))];
    for(const layer of layers.sort((a,b)=>a.z-b.z))layer.draw();ctx.restore();
}
export class ActorCalibrationEditor {
    constructor(container,request,canEdit,onState,canInteract=()=>true){this.canInteract=canInteract;this.root=container;this.request=request;this.canEdit=canEdit;this.onState=onState;this.pending=false;this.uncertain=false;this.context=null;this.draft=null;this.base=null;this.revision=0;this.direction='S';this.frame='1';this.mode='socket';this.selection='';this.zoom=2;this.pointerTool='pan';this.targetDirection='S';this.targetFrame='1';this.reviewed=null;this.frames=[];this.remote=undefined;this.numericDraft=null;this.numericDirty=false;}
    isDirty(){return this.numericDirty||this.draft!==null&&JSON.stringify(this.draft)!==JSON.stringify(this.base);}
    isBusy(){return this.pending||this.uncertain;}
    clear(){this.revision++;this.context=null;this.draft=null;this.base=null;this.frames=[];this.mode='socket';this.uncertain=false;this.pending=false;this.reviewed=null;this.numericDraft=null;this.numericDirty=false;this.root.replaceChildren();this.root.hidden=true;}
    refreshPermission(){if(!this.context)return;this.render();}
    async open(context){
        if(this.isBusy())return;
        if(this.isDirty()&&!confirm('Discard unsaved shared calibration edits before changing context?'))return;
        this.clear();this.context=context;this.id=context.calibrationId;this.selection=context.rig.sockets[0]?.socket_id||'';this.root.hidden=false;this.render();this.status('Choose or enter a shared calibration ID, then load it.');
        const revision=this.revision;this.pending=true;this.render();this.onState();
        try{const result=await this.request(`/actor-appearance/frames?actorKind=${encodeURIComponent(context.actorKind)}&resource=${encodeURIComponent(context.resource)}`);if(revision!==this.revision)return;this.frames=result.frames;}
        catch(error){if(revision===this.revision)this.status(`Exact frames unavailable: ${error.message}`,true);}
        finally{if(revision===this.revision){this.pending=false;this.render();this.onState();}}
        if(revision===this.revision&&this.id)await this.load();
        this.root.scrollIntoView({block:'start'});
    }
    status(text,error=false){const n=this.root.querySelector('[data-status]');if(n){n.textContent=text;n.classList.toggle('error',error);}}
    payloadFrom(response){const c=response.calibration||{};return{socket_overrides:clone(c.sockets||{}),foreground_overlay_overrides:Object.fromEntries(Object.entries(c.foreground_overlays||{}).filter(([key,v])=>v.source_rect_by_direction&&this.context.rig.foreground_overlays.some(o=>o.overlay_id===key)).map(([key,v])=>[key,clone(v.source_rect_by_direction)]))};}
    adopt(response){this.loadedId=this.id;this.hash=response.catalog_hash;this.metadata=response.calibration;this.exists=response.exists;this.draft=this.payloadFrom(response);this.base=clone(this.draft);this.uncertain=false;this.remote=undefined;this.reviewed=null;this.numericDraft=null;this.numericDirty=false;this.revision++;}
    async load(compare=false){
        if(this.pending||!this.canInteract()||!this.id)return;
        if(!compare&&this.isDirty()&&!confirm('Discard local calibration edits and load the selected ID?'))return;
        const requestedId=this.id;this.pending=true;this.render();this.onState();
        try{const response=await this.request(`/actor-appearance/calibrations/${encodeURIComponent(requestedId)}`);
            if(response.calibration&&response.calibration.rig_id!==this.context.rig.rig_id)throw new Error('This calibration belongs to another rig. Select a compatible ID.');
            if(compare){this.remote=response;this.uncertain=true;}else this.adopt(response);this.render();this.status(compare?'Compare local and server calibration before continuing.':response.exists?'Shared calibration loaded. Edits here are not NPC draft edits.':'New calibration ID loaded against the current catalog hash.');
        }catch(error){this.status(`Calibration load failed: ${error.message}. Existing local edits remain.`,true);}
        finally{this.pending=false;this.render();this.onState();}
    }
    changed(){this.revision++;this.reviewed=null;this.status('Unsaved shared calibration edits. Review and save this file separately.');this.render();this.onState();}
    poseFrame(direction=this.direction,frame=this.frame){return this.frames.find(f=>f.direction===direction&&String(f.frame)===String(frame));}
    entries(){return this.mode==='socket'?this.context.rig.sockets.map(s=>({id:s.socket_id,positions:s.positions})):this.context.rig.foreground_overlays.map(o=>({id:o.overlay_id,positions:o.source_rect_by_direction}));}
    map(){return this.draft?.[this.mode==='socket'?'socket_overrides':'foreground_overlay_overrides'];}
    effective(){return this.map()?.[this.selection]?.[this.direction]?.[this.frame]??this.entries().find(e=>e.id===this.selection)?.positions?.[this.direction]?.[this.frame]??null;}
    editable(){return!!this.draft&&!this.isBusy()&&this.canInteract()&&this.loadedId===this.id&&!!this.poseFrame()?.available&&!!this.poseFrame()?.url;}
    set(value,direction=this.direction,frame=this.frame,numeric=false){
        if(this.numericDirty&&!numeric){this.status('Set numeric value or discard pending numeric edits first.',true);return;}
        if(!this.editable())return;
        const target=this.poseFrame(direction,frame);if(!target?.available||!target.url){this.status('An exact target frame is required.',true);return;}
        if(!Object.values(value).every(Number.isInteger)){this.status('Coordinates and dimensions must be whole source pixels.',true);return;}
        if(this.mode==='socket'&&Object.values(value).some(v=>v < -4096||v>4096)){this.status('Socket coordinates must be within −4096 through 4096.',true);return;}
        if(this.mode==='overlay'&&(value.x<0||value.y<0||value.width<1||value.height<1||value.x+value.width>target.source_width||value.y+value.height>target.source_height)){this.status('The overlay rectangle must fit the exact actor frame.',true);return;}
        const map=this.map();map[this.selection]??={};map[this.selection][direction]??={};map[this.selection][direction][frame]=clone(value);this.numericDraft=null;this.numericDirty=false;this.changed();
    }
    reset(){if(!this.editable())return;this.numericDraft=null;this.numericDirty=false;const map=this.map(),dirs=map[this.selection],frames=dirs?.[this.direction];if(!frames)return;delete frames[this.frame];if(!Object.keys(frames).length)delete dirs[this.direction];if(!Object.keys(dirs).length)delete map[this.selection];this.changed();}
    input(parent,label,value,change,{number=false,readonly=false}={}){const wrap=el('label',label,'field'),input=el('input');input.type='text';input.value=value??'';input.readOnly=readonly;if(number)input.inputMode='numeric';input.addEventListener(number?'input':'change',()=>{if(number&&!this.canInteract())return;change(number&&/^-?\d+$/.test(input.value)?Number(input.value):input.value);});wrap.append(input);parent.append(wrap);return input;}
    select(parent,label,value,values,change){const wrap=el('label',label,'field'),select=el('select');for(const v of values){const option=el('option',v.label??v);option.value=v.id??v;select.append(option);}select.value=value;select.addEventListener('change',()=>{if(this.numericDirty){select.value=value;this.status('Set numeric value or discard calibration edits before changing pose controls.',true);return;}change(select.value);});wrap.append(select);parent.append(wrap);return select;}
    render(){
        if(!this.context)return;const message=this.root.querySelector('[data-status]')?.textContent||'';this.root.replaceChildren();this.root.hidden=false;
        this.root.append(el('p','SHARED ACTOR CALIBRATION · FILE SAVE','eyebrow'),el('h3','Sockets & foreground overlays'),el('p',`Rig ${this.context.rig.rig_id} · ${this.context.resource}`,'muted'),el('p','This edits a shared calibration catalog used by every actor referencing its ID. It does not save or publish the NPC. Item grip/pose editing remains in desktop Items.'));
        const status=el('p',message);status.dataset.status='';status.setAttribute('role','status');this.root.append(status);
        const identity=el('div',null,'fields'),idInput=this.input(identity,'Calibration ID',this.id,value=>{if(this.isDirty()){this.status('Discard or save the loaded calibration before changing its ID.',true);idInput.value=this.id;return;}this.id=value;this.reviewed=null;this.render();});idInput.disabled=this.isBusy()||!this.canInteract();this.root.append(identity);
        const actions=el('div',null,'row-actions'),load=btn('Load calibration',()=>this.load()),reload=btn('Reload / compare calibration',()=>this.load(true));load.disabled=this.isBusy()||!this.canInteract();reload.disabled=this.pending||!this.canInteract()||!this.id;actions.append(load,reload);this.root.append(actions);
        if(!this.draft){this.root.append(el('p','Load an existing or new ID to edit against an exact catalog hash.'));return;}
        this.root.append(el('p',`${this.isDirty()?'Unsaved calibration':'Calibration unchanged'} · ${this.exists?'Existing':'New'} · catalog ${this.hash.slice(0,12)}…`,'badge'));
        const controls=el('fieldset');controls.disabled=this.pending||this.uncertain||!this.canInteract();const g=el('div',null,'fields');controls.append(g);this.root.append(controls);
        this.select(g,'Edit mode',this.mode,[{id:'socket',label:'Actor socket'},{id:'overlay',label:'Foreground overlay rectangle'}],value=>{this.mode=value;this.selection=this.entries()[0]?.id||'';this.render();});
        this.select(g,'Socket / overlay',this.selection,this.entries().map(e=>({id:e.id,label:e.id})),value=>{this.selection=value;this.render();});
        this.select(g,'Direction',this.direction,['N','E','S','W'],value=>{this.direction=value;this.render();});this.select(g,'Frame',this.frame,['1','2','3','4'],value=>{this.frame=value;this.render();});
        this.select(g,'Touch tool',this.pointerTool,[{id:'pan',label:'Pan / scroll image'},{id:'edit',label:'Edit selected socket / rectangle'}],value=>{this.pointerTool=value;this.render();});
        this.select(g,'Zoom',String(this.zoom),['1','2','4','6','8','12'],value=>{this.zoom=Number(value);this.render();});
        const viewport=el('div',null,'calibration-viewport'),canvas=el('canvas');canvas.className='calibration-canvas';canvas.style.touchAction=this.pointerTool==='pan'?'auto':'none';canvas.width=600;canvas.height=420;viewport.append(canvas);controls.append(viewport);
        const effective=this.effective(),exact=this.poseFrame();controls.append(el('p',!exact?.available||!exact.url?'Exact pose image unavailable. This pose cannot be edited.':effective?`${this.map()?.[this.selection]?.[this.direction]?.[this.frame]?'Override':'Inherited'} source-pixel ${this.mode==='socket'?'point':'rectangle'}. Tap/drag on the canvas or use numeric fields.`:'No effective value. Use Create override to author one.','muted'));
        const inputs=el('fieldset');inputs.disabled=!this.editable();const values=el('div',null,'fields');inputs.append(values);controls.append(inputs);
        if(effective){const poseKey=`${this.mode}/${this.selection}/${this.direction}/${this.frame}`;const edit=this.numericDraft?.key===poseKey?this.numericDraft.value:clone(effective);for(const key of this.mode==='socket'?['x','y']:['x','y','width','height'])this.input(values,key.toUpperCase(),edit[key],value=>{edit[key]=value;this.numericDraft={key:poseKey,value:edit};this.numericDirty=true;this.reviewed=null;for(const b of this.root.querySelectorAll('button'))if(['Review calibration changes','Save shared calibration file'].includes(b.textContent))b.disabled=true;this.status('Numeric edits are pending. Set numeric value before switching poses, reviewing or saving.');this.onState();},{number:true});inputs.append(btn('Set numeric value',()=>this.set(edit,this.direction,this.frame,true)));}
        else inputs.append(btn('Create override',()=>this.set(this.mode==='socket'?{x:0,y:0}:{x:0,y:0,width:1,height:1})));
        const nudges=el('div',null,'row-actions');for(const [label,x,y]of[['← 1',-1,0],['→ 1',1,0],['↑ 1',0,-1],['↓ 1',0,1]]){const b=btn(label,()=>{const v=this.effective();if(v)this.set({...v,x:v.x+x,y:v.y+y});});b.disabled=!effective;nudges.append(b);}nudges.append(btn('Reset pose to inherited',()=>this.reset()));inputs.append(nudges);
        if(this.mode==='socket'){
            this.select(values,'Copy/mirror target direction',this.targetDirection,['N','E','S','W'],v=>{this.targetDirection=v;});this.select(values,'Copy/mirror target frame',this.targetFrame,['1','2','3','4'],v=>{this.targetFrame=v;});
            inputs.append(btn('Copy current to target',()=>{const v=this.effective();if(v)this.set(v,this.targetDirection,this.targetFrame);}),btn('Mirror current to target',()=>{const v=this.effective(),target=this.poseFrame(this.targetDirection,this.targetFrame);if(v&&target?.source_width)this.set({x:target.source_width-1-v.x,y:v.y},this.targetDirection,this.targetFrame);}));
        }
        const review=btn('Review calibration changes',()=>{if(this.numericDirty){this.status('Set numeric value before reviewing.',true);return;}this.reviewed={revision:this.revision,payload:clone(this.draft)};this.render();});review.disabled=!this.isDirty()||this.numericDirty||this.isBusy()||!this.canInteract();controls.append(review);
        if(this.reviewed){controls.append(el('pre',JSON.stringify({before:this.base,after:this.reviewed.payload},null,2),'readonly'));const save=btn('Save shared calibration file',()=>this.save());save.disabled=this.numericDirty||this.reviewed.revision!==this.revision||!this.canEdit();controls.append(save);}
        controls.append(btn('Discard calibration edits',()=>{if(confirm('Discard local calibration changes?')){this.draft=clone(this.base);this.numericDirty=false;this.numericDraft=null;this.changed();}}));
        const use=btn('Use this calibration in NPC draft',()=>{if(this.loadedId===this.id){this.context.onUse(this.id);this.status('Calibration reference set in NPC draft. Save the NPC separately.');}});use.disabled=this.isBusy()||!this.exists||this.loadedId!==this.id||this.isDirty();controls.append(use);
        const metadata=el('details');metadata.append(el('summary','Stored calibration metadata · preserved'),el('pre',JSON.stringify(this.metadata,null,2),'readonly'));this.root.append(metadata);
        if(this.remote!==undefined)this.renderComparison();
        void this.draw(canvas,exact,effective);
    }
    async draw(canvas,frame,value){
        const revision=this.revision,direction=this.direction,pose=this.frame,mode=this.mode,selection=this.selection,zoom=this.zoom;
        const ctx=canvas.getContext('2d');ctx.clearRect(0,0,canvas.width,canvas.height);if(!frame?.available||!frame.url)return;
        try{const img=await image(frame.url);if(revision!==this.revision||direction!==this.direction||pose!==this.frame||mode!==this.mode||selection!==this.selection||!canvas.isConnected)return;
            canvas.width=Math.max(300,(img.width+32)*zoom);canvas.height=Math.max(260,(img.height+32)*zoom);canvas.style.width=`${canvas.width}px`;canvas.style.height=`${canvas.height}px`;ctx.imageSmoothingEnabled=false;const x=16*zoom,y=16*zoom;ctx.drawImage(img,x,y,img.width*zoom,img.height*zoom);
            if(value){ctx.strokeStyle='#ffcb45';ctx.lineWidth=2;if(mode==='socket'){const px=x+value.x*zoom,py=y+value.y*zoom;ctx.beginPath();ctx.moveTo(px-12,py);ctx.lineTo(px+12,py);ctx.moveTo(px,py-12);ctx.lineTo(px,py+12);ctx.stroke();}else{ctx.fillStyle='#ffcb4533';ctx.fillRect(x+value.x*zoom,y+value.y*zoom,value.width*zoom,value.height*zoom);ctx.strokeRect(x+value.x*zoom,y+value.y*zoom,value.width*zoom,value.height*zoom);}}
            let start=null;const point=e=>{const r=canvas.getBoundingClientRect();return{x:Math.round((e.clientX-r.left-x)/zoom),y:Math.round((e.clientY-r.top-y)/zoom)};};
            canvas.onpointerdown=e=>{if(!this.editable()||this.pointerTool!=='edit')return;start=point(e);canvas.setPointerCapture(e.pointerId);};
            canvas.onpointerup=e=>{if(!start||!this.editable())return;const end=point(e),origin=start;start=null;if(mode==='socket')this.set(end);else this.set({x:Math.min(origin.x,end.x),y:Math.min(origin.y,end.y),width:Math.max(1,Math.abs(end.x-origin.x)+1),height:Math.max(1,Math.abs(end.y-origin.y)+1)});};canvas.onpointercancel=()=>start=null;
        }catch(error){if(canvas.isConnected)this.status(error.message,true);}
    }
    renderComparison(){const box=el('div',null,'calibration-compare');box.append(el('h4','Reconcile shared calibration'),el('pre',JSON.stringify({local:this.draft,server:this.remote},null,2),'readonly'));
        box.append(btn('Use server calibration',()=>{if(confirm('Discard local calibration edits and accept the server version?')){this.adopt(this.remote);this.render();this.onState();}}),btn('Keep edits on current catalog',()=>{if(confirm('Keep local edits against the current whole-catalog hash? Review again before saving.')){this.base=this.payloadFrom(this.remote);this.hash=this.remote.catalog_hash;this.metadata=this.remote.calibration;this.exists=this.remote.exists;this.uncertain=false;this.remote=undefined;this.changed();}}));this.root.append(box);}
    async save(){
        if(!this.canEdit()||this.numericDirty||this.isBusy()||!this.reviewed||this.reviewed.revision!==this.revision||this.loadedId!==this.id)return;
        if(!confirm(`Save shared calibration ${this.id}? Every actor using this ID shares these socket/overlay settings. The NPC draft is not saved.`))return;
        const payload={...clone(this.reviewed.payload),expected_catalog_hash:this.hash,rig_id:this.context.rig.rig_id,actor_kind:this.context.actorKind,visual_texture_path:this.context.resource};this.pending=true;this.render();this.onState();
        try{const response=await this.request(`/actor-appearance/calibrations/${encodeURIComponent(this.id)}`,{method:'PUT',body:payload});if(response?.catalog_file_written!==true||!response.calibration?.catalog_hash)throw new Error('The response did not confirm the catalog-file result.');this.adopt(response.calibration);this.status('Shared calibration file saved. NPC database draft and live game were not changed.');}
        catch(error){this.reviewed=null;if(!error.status||error.status>=500||error.status===409){this.uncertain=true;this.remote=undefined;this.status(`${error.message} Reload / compare before another save. Local edits remain.`,true);}else this.status(error.message,true);}
        finally{this.pending=false;this.render();this.onState();}
    }
}
