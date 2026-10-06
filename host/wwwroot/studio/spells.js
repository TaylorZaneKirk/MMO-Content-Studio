// One owner for Spell selection, spell mechanics and presentation, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: [], definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, mediaTarget: null, mediaAssets: {image:[],audio:[]}, mediaRevision: 0 };
const fields = new Map();
function element(tag, text, className) {
    const node = document.createElement(tag); if (text != null) node.textContent = text; if (className) node.className = className; return node;
}
function button(text, action, className) {
    const node = element('button', text, className); node.type = 'button'; node.addEventListener('click', action); return node;
}
function notice(message, error = false) { $('notice').textContent = message; $('notice').classList.toggle('error', error); }
function dirty() { return state.draft !== null && (!state.base || JSON.stringify(state.draft) !== JSON.stringify(state.base)); }
function changed() {
    state.editRevision++; state.preview = null; $('review').hidden = true; stopMedia();
    $('dirty-badge').textContent = dirty() ? 'Unsaved edits' : 'Saved'; $('dirty-badge').classList.toggle('dirty', dirty());
    $('spell-title').textContent = state.draft?.display_name || 'Untitled spell'; updateActions();
}
function busy(value) { state.pending = value; updateActions(); }
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-spell').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-spell').disabled = state.pending || !state.id;
    $('session-button').disabled = state.pending;
    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes = {
        save_draft:'Save the complete spell as Draft. Saving a Published spell removes publication. No catalog export is requested.',
        save_and_publish:'Save and publish the complete current draft, including new spells. No catalog export is requested by this service.',
        publish:'Publish the saved spell. Save or discard local changes first. No catalog export is requested.',
        disable:'Disable the saved spell. Save or discard local changes first. No catalog export is requested.',
        delete:'Permanently delete a Disabled spell after existing database guards. No catalog export is requested.'
    };
    $('operation-note').textContent = notes[$('operation').value];
}
const request = createRequest(() => state.session, () => {
    state.session.authenticated = false; state.session.can_edit = false; updateSession();
});
async function refreshSession() { state.session = await request('/session'); updateSession(); }
function updateSession() {
    $('connection').textContent = state.session.trusted_home_lan ? (state.session.read_only ? 'Home LAN · read only' : 'Home LAN · shared editor')
        : !state.session.configured ? 'Local preview · read only' : !state.session.authenticated ? 'Sign in to edit'
        : state.session.read_only ? 'Signed in · read only' : 'Connected · owner';
    $('session-button').textContent = state.session.authenticated ? 'Sign out' : 'Sign in';
    $('session-button').hidden = !state.session.configured || state.session.trusted_home_lan;
    $('access-notice').hidden = !state.session.trusted_home_lan;
    $('access-notice').textContent = state.session.read_only
        ? 'Trusted home LAN · no individual sign-in. This host currently permits viewing only.'
        : 'Trusted home LAN · anyone on the allowed network can view, upload, edit, publish and delete content. No individual sign-in.';
    updateActions();
}
async function loadOptions() {
    const revision=++state.optionsRevision;
    try { const options=await request('/spells/options');if(revision===state.optionsRevision)state.options=options; }
    catch(error){if(revision===state.optionsRevision)notice(`Spell defaults unavailable: ${error.message}. Refresh to retry.`,true);}
}
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/spells?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('spell-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} spells`;
        for (const spell of result.items) {
            const card = button('', () => loadSpell(spell.spell_id), `item-card${spell.spell_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', spell.display_name), element('small', `${spell.spell_id} · ${spell.publication_state}`));
            card.append(copy); $('spell-list').append(card);
        }
        if (!result.items.length) $('spell-list').append(element('p', 'No matching spells. Try another search or create a new spell.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('spell-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the Spell before replacing an uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved Spell changes?');
}
async function loadSpell(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/spells/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    state.definition = definition; state.id = definition.spell_id; state.version = definition.updated_at_utc;
    state.draft = clone(definition.draft); state.base = clone(definition.draft); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = definition.publication_state === 'Published' ? 'save_and_publish' : 'save_draft'; render();
}
function newSpell() {
    if (!state.options.defaults) { notice('Load spell defaults with Refresh before creating a new spell.',true);return; }
    if (!mayReplace()) return;
    state.loadRevision++; state.definition = null; state.id = ''; state.version = null; state.base = null;
    state.uncertain = false; state.remote = undefined;
    state.draft = clone(state.options.defaults);
    $('operation').value = 'save_draft'; $('reconcile').hidden = true; render(); location.hash = 'detail';
    notice('Choose a stable Spell ID and author its mechanics and presentation. Preview before saving.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { kind='text', nullable=false, min=0, max=2147483647, readonly=false, path=key, hint='', required=false }={}) {
    const wrapper=element('div',null,'field'), input=element(kind==='multiline'?'textarea':'input');
    input.id=`spell-field-${fields.size}`;fields.set(path,input);input.name=path;input.value=owner[key]??'';input.readOnly=readonly;input.required=required;
    if(kind==='multiline')input.rows=4;else {input.type='text';if(['integer','long','coordinate'].includes(kind))input.inputMode=kind==='coordinate'?'decimal':'numeric';}
    const caption=element('label',label);caption.htmlFor=input.id;wrapper.append(caption,input);
    const validate=()=>{
        const v=input.value;let valid=true;
        if(v===''&&nullable)valid=true;
        else if(kind==='integer'||kind==='long')valid=/^-?\d+$/.test(v)&&BigInt(v)>=BigInt(min)&&BigInt(v)<=BigInt(max);
        else if(kind==='coordinate')valid=v.trim()!==''&&Number.isFinite(Number(v));
        input.setCustomValidity(valid?'':kind==='coordinate'?'Enter a finite coordinate.':`Enter a whole number from ${min} through ${max}.`);return valid;
    };
    validate();input.addEventListener('input',()=>{
        const valid=validate(),v=input.value;
        owner[key]=v===''&&nullable?null:valid&&['integer','coordinate'].includes(kind)?Number(v):v;
        changed();
    });
    if(hint)wrapper.append(element('small',hint));parent.append(wrapper);return input;
}
function selectField(parent,owner,key,label,values,path=key) {
    const box=element('div',null,'field'),input=element('select');input.id=`spell-field-${fields.size}`;fields.set(path,input);
    const caption=element('label',label);caption.htmlFor=input.id;box.append(caption,input);
    for(const value of values){const option=element('option',value.display_name??value);option.value=value.id??value;input.append(option);}
    if(!values.some(value=>(value.id??value)===owner[key])){const option=element('option',`Stored value: ${owner[key]??'(empty)'}`);option.value=owner[key]??'';input.append(option);}
    input.value=owner[key]??'';input.addEventListener('change',()=>{owner[key]=input.value===''?null:input.value;changed();});parent.append(box);return input;
}
function checkField(parent,owner,key,label,path=key){
    const box=element('label',null,'dialogue-checkbox'),input=element('input');input.type='checkbox';input.checked=owner[key];fields.set(path,input);
    input.addEventListener('change',()=>{owner[key]=input.checked;changed();});box.append(input,document.createTextNode(label));parent.append(box);
}
// Small local helpers group the explicit Spell fields; the service owns all rules.
function numbers(parent,keys,{nullable=true}={}){
    const g=grid(parent);for(const [key,label] of keys)field(g,state.draft,key,label,{kind:'integer',nullable});return g;
}
function clearGroup(parent,label,keys){parent.append(button(`Clear ${label} fields`,()=>{if(confirm(`Explicitly clear every ${label} field?`)){for(const key of keys)state.draft[key]=null;render();}}));}
const forceFields=[['force','Base force'],['force_falloff_per_tile','Force falloff per tile after the first'],['max_displacement_tiles','Maximum displacement · tiles']];
const masteryFields=[['force_mastery_magic_levels_per_step','Effective Magic levels per force step'],['force_mastery_force_per_step','Force gained per step'],['force_mastery_max_force','Maximum mastered force'],['displacement_mastery_magic_levels_per_step','Base Magic levels per displacement step'],['displacement_mastery_tiles_per_step','Displacement tiles gained per step'],['displacement_mastery_max_tiles','Maximum mastered displacement']];
const matterFields=[['manifestation_base_success_percent','Base manifestation success · percent'],['manifestation_magic_levels_per_step','Magic levels per manifestation step'],['manifestation_success_percent_per_step','Success percent per step'],['matter_lifetime_milliseconds','Matter lifetime · milliseconds'],['matter_capacity_magic_levels_per_step','Base Magic levels per capacity step'],['matter_max_active','Maximum active matter'],['matter_physical_weight','Physical weight']];
const fireFields=[['ignition_base_success_percent','Base ignition success · percent'],['ignition_magic_levels_per_step','Effective Magic levels per ignition step'],['ignition_success_percent_per_step','Success percent per step'],['burning_lifetime_milliseconds','Burning lifetime · milliseconds'],['burning_capacity_magic_levels_per_step','Base Magic levels per capacity step'],['burning_max_active','Maximum active fire'],['burning_min_damage','Minimum environmental Fire damage'],['burning_max_damage','Maximum environmental Fire damage'],['burning_hazard_cooldown_milliseconds','Per-Mob hazard cooldown · milliseconds']];
const slickFields=[['slick_base_success_percent','Base slick manifestation success · percent'],['slick_magic_levels_per_step','Effective Magic levels per manifestation step'],['slick_success_percent_per_step','Success percent per step'],['slick_lifetime_milliseconds','Slick lifetime · milliseconds'],['slick_capacity_magic_levels_per_step','Base Magic levels per capacity step'],['slick_max_active','Maximum active slicks']];
const phases=[['projectile','Projectile'],['impact','Impact'],['splash','Splash'],['burning_visual','Burning hazard'],['slick_visual','Slick hazard']];
function assetField(parent,owner,key,label,kind='image',path=key,nullable=true){
    const box=element('div',null,'spell-asset');field(box,owner,key,label,{nullable,path,hint:kind==='audio'?'Canonical res://assets/ WAV, OGG or MP3.':'Canonical res://assets/ PNG. Missing references remain editable.'});
    box.append(button(`Choose ${label.toLowerCase()}`,()=>openMedia(owner,key,kind)));parent.append(box);
}
function frameRows(parent,phase,label){
    const key=phase+'_frames',frames=state.draft[key];
    parent.append(element('p',frames===null?'Frame list is unset. Adding a frame creates an ordered list.':`${frames.length} ordered frames. Reordering changes only this list.`,'muted'));
    (frames||[]).forEach((path,index)=>{
        const row=element('div',null,'repeat-row');row.append(element('h4',`${label} frame ${index+1}`));assetField(row,frames,index,'Texture','image',`${key}[${index}]`,false);
        const actions=element('div',null,'row-actions');const up=button('Move up',()=>{[frames[index-1],frames[index]]=[frames[index],frames[index-1]];render();});up.disabled=index===0;
        const down=button('Move down',()=>{[frames[index+1],frames[index]]=[frames[index],frames[index+1]];render();});down.disabled=index===frames.length-1;
        actions.append(up,down,button('Remove frame',()=>{frames.splice(index,1);render();}));row.append(actions);parent.append(row);
    });
    parent.append(button('Add frame',()=>{state.draft[key]??=[];state.draft[key].push('');render();}),button('Unset frame list',()=>{if(confirm(`Clear the entire ${label.toLowerCase()} frame list to unset?`)){state.draft[key]=null;render();}}));
}
function render(){
    const opened=new Set([...document.querySelectorAll('.section[open]')].map(n=>n.dataset.title));$('empty').hidden=true;$('editor').hidden=false;$('form-fields').replaceChildren();fields.clear();
    $('spell-state').textContent=state.definition?.publication_state?.toUpperCase()||'NEW SPELL';$('spell-meta').textContent=state.definition?`${state.id} · Updated ${state.version}`:'Unsaved spell';
    const d=state.draft;let p=section('Spell identity & casting',true,'The complete definition is saved together. Changing an effect or type retains its fields; clear incompatible groups explicitly.');let g=grid(p);
    field(g,state,'id','Spell ID',{required:true,readonly:!!state.definition,path:'definition_id'});field(g,d,'display_name','Display name',{required:true});
    selectField(g,d,'element','Element',['air','earth','fire','water']);selectField(g,d,'cast_mode','Cast mode',['selected_combat','explicit_technique']);selectField(g,d,'target_mode','Target mode',['mob','tile','physical']);
    selectField(g,d,'impact_effect','Impact effect',[{id:'',display_name:'None'},{id:'air_displacement',display_name:'Air displacement'},{id:'earth_matter',display_name:'Earth matter'},{id:'burning_terrain',display_name:'Burning terrain'},{id:'slippery_terrain',display_name:'Slippery terrain'}]);
    p=section('Requirements, damage & XP',true);g=grid(p);
    for(const [key,label,min,max] of [['tier','Tier',1,4],['required_magic_level','Required Magic level',1,99],['shard_cost','Crystal Shard cost',1,2147483647],['successful_hit_min_damage','Successful-hit minimum damage',0,2147483647],['base_max_hit','Base maximum hit',0,2147483647],['base_cast_xp_tenths','Base cast XP · integer tenths',0,2147483647]])field(g,d,key,label,{kind:'integer',min,max});
    const xp=element('p',null,'muted');xp.id='exact-xp';p.append(xp);
    p=section('Air force & displacement',d.impact_effect==='air_displacement','Blank means unset; zero remains an explicit authored value.');numbers(p,forceFields);clearGroup(p,'base force',forceFields.map(x=>x[0]));
    p=section('Air mastery',false,'Leave all six values empty for fixed force. The service validates complete mastery groups and caps.');numbers(p,masteryFields);clearGroup(p,'Air mastery',masteryFields.map(x=>x[0]));
    p=section('Earth manifestation & matter',d.impact_effect==='earth_matter');numbers(p,matterFields);g=grid(p);assetField(g,d,'matter_visual_texture_path','Matter texture');field(g,d,'matter_visual_render_scale','Matter render scale',{kind:'coordinate',nullable:true});clearGroup(p,'Earth matter',[...matterFields.map(x=>x[0]),'matter_visual_texture_path','matter_visual_render_scale']);
    p=section('Fire ignition & burning',d.impact_effect==='burning_terrain');numbers(p,fireFields);clearGroup(p,'Fire mechanics',fireFields.map(x=>x[0]));
    p=section('Water manifestation & slicks',d.impact_effect==='slippery_terrain');numbers(p,slickFields);clearGroup(p,'Water mechanics',slickFields.map(x=>x[0]));
    p=section('Icon & cast audio',true);g=grid(p);assetField(g,d,'icon_texture_path','Spellbook icon');assetField(g,d,'cast_sound_path','Cast sound','audio');
    for(const [phase,label] of phases){
        p=section(label+' presentation',false,'Order is authored. Missing frames are reported during preview, never skipped. Blank FPS/scale stays unset.');frameRows(p,phase,label);g=grid(p);
        field(g,d,phase+'_animation_fps','Animation · frames per second',{kind:'coordinate',nullable:true});field(g,d,phase+'_render_scale','Render scale',{kind:'coordinate',nullable:true});
        if(phase==='projectile'){
            selectField(g,d,'projectile_source_facing','Source artwork facing',['right','down','left','up']);checkField(p,d,'projectile_rotates_to_travel','Rotate projectile toward travel');checkField(p,d,'projectile_homing_enabled','Homing enabled');field(g,d,'projectile_homing_strength','Homing strength · greater than 0 through 1',{kind:'coordinate'});
        }
        if(phase==='impact'||phase==='splash')assetField(g,d,phase+'_sound_path',label+' sound','audio');
        clearGroup(p,label+' presentation',[phase+'_frames',phase+'_animation_fps',phase+'_render_scale']);
    }
    for(const node of document.querySelectorAll('.section'))if(opened.has(node.dataset.title))node.open=true;
    changed();
}
async function openMedia(owner,key,kind){
    state.mediaTarget={owner,key,kind,revision:state.editRevision};$('media-title').textContent=`Choose ${kind==='audio'?'sound':'PNG'}`;$('media-search').value='';$('media-dialog').showModal();$('media-results').replaceChildren(element('p','Loading available media…'));
    const target=state.mediaTarget;
    try{const result=await request(`/spells/media?kind=${kind}`);if(state.mediaTarget!==target)return;state.mediaAssets[kind]=result.assets;renderMediaPicker();}
    catch(error){if(state.mediaTarget===target)$('media-results').replaceChildren(element('p',`Media unavailable: ${error.message}. Close and choose again to retry.`));}
}
function renderMediaPicker(){
    const target=state.mediaTarget;if(!target)return;const q=$('media-search').value.toLowerCase();$('media-results').replaceChildren();
    const rows=state.mediaAssets[target.kind].filter(a=>`${a.display_name} ${a.resource_path}`.toLowerCase().includes(q));
    $('media-results').append(element('p',`${rows.length} matches. Showing up to 150; narrow the search to find more.`));
    for(const row of rows.slice(0,150)){
        const pick=button('',()=>{if(state.pending||state.uncertain||target.revision!==state.editRevision)return;target.owner[target.key]=row.resource_path;$('media-dialog').close();render();});pick.append(element('strong',row.display_name),element('small',row.resource_path));
        if(target.kind==='image'){const image=element('img');image.src=row.url;image.loading='lazy';image.alt='';image.width=48;image.height=48;pick.prepend(image);}
        $('media-results').append(pick);
    }
}
$('close-media').addEventListener('click',()=>$('media-dialog').close());$('media-search').addEventListener('input',renderMediaPicker);
$('media-dialog').addEventListener('close',()=>{state.mediaTarget=null;});

// Playback is local presentation state, never part of the saved spell or signature.
let mediaSnapshot=null,mediaFrame=0,mediaPlaying=false,mediaStart=0,mediaTick=0,drawRevision=0;
const imageCache=new Map();
function mediaUrl(path,audio=false){return `/studio/api/spells/${audio?'audio':'image'}?resource=${encodeURIComponent(path)}`;}
function stopMedia(){
    state.mediaRevision++;drawRevision++;mediaPlaying=false;cancelAnimationFrame(mediaTick);mediaSnapshot=null;imageCache.clear();
    $('spell-canvas').getContext('2d').clearRect(0,0,600,400);$('media-status').textContent='Preview current presentation to inspect the current draft. No gameplay simulation or save occurs.';$('play-media').textContent='Play';$('play-media').disabled=true;
    for(const audio of document.querySelectorAll('#sound-previews audio')){audio.pause();audio.removeAttribute('src');audio.load();}$('sound-previews').replaceChildren();
    if($('exact-xp')){const value=String(state.draft?.base_cast_xp_tenths??'');$('exact-xp').textContent=/^\d+$/.test(value)?`Exact base cast XP: ${BigInt(value)/10n}.${BigInt(value)%10n} XP. The saved value remains integer tenths.`:'Enter nonnegative integer XP tenths.';}
}
function selectedVisual(){
    const phase=$('visual-phase').value,d=mediaSnapshot;if(!d)return {frames:[]};
    if(phase==='icon')return {frames:d.icon_texture_path?[d.icon_texture_path]:[],scale:1,phase};
    if(phase==='matter')return {frames:d.matter_visual_texture_path?[d.matter_visual_texture_path]:[],scale:d.matter_visual_render_scale??1,phase};
    return {frames:d[phase+'_frames']||[],fps:d[phase+'_animation_fps'],scale:d[phase+'_render_scale']??1,phase};
}
async function drawMedia(){
    const visual=selectedVisual(),revision=state.mediaRevision,draw=++drawRevision,canvas=$('spell-canvas'),ctx=canvas.getContext('2d');ctx.clearRect(0,0,canvas.width,canvas.height);
    if(!visual.frames.length){$('media-status').textContent='This phase has no authored image or frames.';$('play-media').disabled=true;return;}
    mediaFrame=Math.max(0,Math.min(mediaFrame,visual.frames.length-1));const index=mediaFrame,path=visual.frames[index];
    $('frame-number').textContent=`Frame ${index+1} / ${visual.frames.length}`;
    try{
        if(!path)throw new Error('Frame reference is empty.');
        if(!imageCache.has(path)){
            if(imageCache.size>=32)imageCache.clear();
            imageCache.set(path,new Promise((resolve,reject)=>{const img=new Image();img.onload=()=>resolve(img);img.onerror=()=>reject(new Error('PNG is missing, invalid, oversized or outside the permitted asset folder.'));img.src=mediaUrl(path);}));
        }
        const image=await imageCache.get(path);
        if(revision!==state.mediaRevision||draw!==drawRevision)return;
        const zoom=Number($('visual-zoom').value),scale=visual.scale*zoom;ctx.imageSmoothingEnabled=false;ctx.save();ctx.translate(canvas.width/2,canvas.height/2);
        if(visual.phase==='projectile'&&mediaSnapshot.projectile_rotates_to_travel){const angles={right:0,down:90,left:180,up:270};ctx.rotate((angles[$('travel-direction').value]-angles[mediaSnapshot.projectile_source_facing])*Math.PI/180);}
        ctx.drawImage(image,-image.width*scale/2,-image.height*scale/2,image.width*scale,image.height*scale);ctx.restore();
        $('media-status').textContent=`${path} · source ${image.width} × ${image.height} · authored scale ${visual.scale} · inspection zoom ${zoom}×. Large artwork may extend outside the canvas.${visual.phase==='projectile'?` Homing ${mediaSnapshot.projectile_homing_enabled?'enabled':'disabled'}, strength ${mediaSnapshot.projectile_homing_strength}; travel direction is an inspection control, not a trajectory simulation.`:''}`;
    }catch(error){if(revision!==state.mediaRevision||selectedVisual().phase!==visual.phase)return;drawRevision++;ctx.clearRect(0,0,canvas.width,canvas.height);mediaPlaying=false;cancelAnimationFrame(mediaTick);$('play-media').textContent='Play';$('media-status').textContent=`Frame ${index+1} unavailable: ${error.message} The authored order is unchanged.`;}
}
function selectVisual(){mediaPlaying=false;cancelAnimationFrame(mediaTick);mediaFrame=0;const v=selectedVisual();$('play-media').textContent='Play';$('play-media').disabled=!mediaSnapshot||v.frames.length<2||!Number.isFinite(v.fps)||v.fps<=0;void drawMedia();}
$('refresh-media').addEventListener('click',()=>{
    if(!state.draft||!validForm())return;stopMedia();mediaSnapshot=clone(state.draft);selectVisual();
    for(const [key,label] of [['cast_sound_path','Cast'],['impact_sound_path','Impact'],['splash_sound_path','Splash']]){
        const box=element('div',null,'spell-sound');box.append(element('h4',label+' sound'));const path=mediaSnapshot[key];
        if(path){const audio=element('audio');audio.controls=true;audio.preload='none';audio.src=mediaUrl(path,true);audio.setAttribute('aria-label',label+' sound preview');const status=element('p',path,'muted');audio.addEventListener('error',()=>status.textContent=`${path} · unavailable or unsupported audio; the reference is retained.`);box.append(audio,status);}else box.append(element('p','No sound assigned.','muted'));$('sound-previews').append(box);
    }
});
$('visual-phase').addEventListener('change',selectVisual);$('visual-zoom').addEventListener('change',()=>void drawMedia());$('travel-direction').addEventListener('change',()=>void drawMedia());
for(const [id,step] of [['previous-frame',-1],['next-frame',1]])$(id).addEventListener('click',()=>{mediaPlaying=false;cancelAnimationFrame(mediaTick);$('play-media').textContent='Play';const n=selectedVisual().frames.length;if(n){mediaFrame=(mediaFrame+step+n)%n;void drawMedia();}});
$('play-media').addEventListener('click',()=>{
    const v=selectedVisual();if(!mediaSnapshot||v.frames.length<2||!Number.isFinite(v.fps)||v.fps<=0)return;
    mediaPlaying=!mediaPlaying;$('play-media').textContent=mediaPlaying?'Pause':'Play';if(!mediaPlaying){cancelAnimationFrame(mediaTick);return;}
    if(['impact','splash'].includes(v.phase)&&mediaFrame===v.frames.length-1){mediaFrame=0;void drawMedia();}
    mediaStart=performance.now()-mediaFrame*1000/v.fps;
    const tick=now=>{if(!mediaPlaying)return;const n=Math.floor((now-mediaStart)*v.fps/1000),loop=['projectile','burning_visual','slick_visual'].includes(v.phase),next=loop?n%v.frames.length:Math.min(n,v.frames.length-1);if(next!==mediaFrame){mediaFrame=next;void drawMedia();}if(!loop&&n>=v.frames.length-1){mediaPlaying=false;$('play-media').textContent='Play';}else mediaTick=requestAnimationFrame(tick);};mediaTick=requestAnimationFrame(tick);
});

function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || fields.get(message.field+'_frames[0]') || null;
        if (input) box.append(button('Go to field', () => { input.closest('details').open = true; input.focus(); input.scrollIntoView({ block: 'center' }); }));
        $('review-messages').append(box);
    }
}
function showChanges(changes) {
    $('review-changes').replaceChildren();
    for (const change of changes) {
        const box = element('div', null, 'change'); box.append(element('strong', change.field.replaceAll('_', ' ')),
            element('pre', `Before\n${JSON.stringify(change.before, null, 2)}\n\nAfter\n${JSON.stringify(change.after, null, 2)}`)); $('review-changes').append(box);
    }
    if (!changes.length) $('review-changes').append(element('p', 'No differences in this preview.', 'muted'));
}
function validForm() {
    const invalid = $('spell-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (!['save_draft', 'save_and_publish'].includes(operation) && dirty()) { notice('Save or discard local edits before applying an operation to the saved Spell.', true); return; }
    if (!/^[a-z][a-z0-9]*(_[a-z0-9]+)*$/.test(state.id)) { notice('Use a stable lowercase Spell ID, with optional underscore-separated words.', true); fields.get('definition_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { draft: clone(state.draft), expected_updated_at_utc: state.version, preview_signature: null, target_operation: operation };
    state.preview = null; busy(true);
    try {
        const result = await request(`/spells/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
        if (revision !== state.editRevision || id !== state.id) return;
        state.preview = { valid: result.applicable, signature: result.preview_signature, revision, id, operation, payload };
        messages(result.messages); showChanges(result.changes); $('review').hidden = false;
        notice(result.applicable ? 'Review the changes below, then apply this exact preview.' : 'Resolve the validation messages and preview again.', !result.applicable);
        $('review').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    finally { busy(false); }
}
async function apply() {
    const reviewed = state.preview;
    if (state.pending || state.uncertain || !reviewed?.valid || reviewed.revision !== state.editRevision || reviewed.id !== state.id) return;
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} Spell ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/spells/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: { ...reviewed.payload, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { stopMedia(); state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'Spell catalog export succeeded.', failed: 'Spell catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'Spell catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the Spell changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/spells/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(state.remote.draft, null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The Spell is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing Spell'; $('keep-local').textContent = 'Keep edits as a new draft';
        }
        $('keep-local').hidden = false; $('reconcile').hidden = false;
        notice('Compare before continuing. Stored content does not prove catalog export or live-game reload.');
        $('reconcile').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(`Could not reconcile: ${error.message} Local edits remain.`, true); }
    finally { busy(false); }
}
$('use-server').addEventListener('click', () => {
    if (state.pending || state.remote === undefined || !confirm('Discard local edits and accept the current server result?')) return;
    if (state.remote) adopt(state.remote);
    else { stopMedia(); state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.uncertain = false; state.remote = undefined;
        $('editor').hidden = true; $('empty').hidden = false; $('reconcile').hidden = true; location.hash = 'list'; }
    notice('Server result acknowledged. Export status is not inferred from stored content.'); updateActions(); void search();
});
$('keep-local').addEventListener('click', () => {
    if (state.pending || state.remote === undefined || !confirm('Keep these edits against the current server result? A fresh preview is required before writing.')) return;
    state.definition = state.remote; state.version = state.remote?.updated_at_utc ?? null; state.base = state.remote ? clone(state.remote.draft) : null;
    state.uncertain = false; state.remote = undefined; $('reconcile').hidden = true;
    $('operation').value = state.definition?.publication_state === 'Published' ? 'save_and_publish' : 'save_draft'; render();
    notice('Local edits retained against the current version. Preview again before applying.');
});
$('spells-workspace').addEventListener('click', () => location.hash = 'list');
$('new-spell').addEventListener('click', newSpell);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', changed); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-spell').addEventListener('click', reconcile); $('spell-form').addEventListener('submit', event => event.preventDefault());

$('session-button').addEventListener('click', async () => {
    if (state.session.authenticated) {
        try { await request('/logout', { method: 'POST' }); await refreshSession(); notice('Signed out. Unsaved edits remain in this tab.'); }
        catch (error) { notice(error.message, true); } return;
    }
    $('login-dialog').showModal(); $('password').focus();
});
$('cancel-login').addEventListener('click', () => $('login-dialog').close());
$('login-form').addEventListener('submit', async event => {
    event.preventDefault(); const submit = event.submitter; submit.disabled = true; $('login-error').textContent = '';
    try {
        await refreshSession(); await request('/login', { method: 'POST', body: { password: $('password').value } });
        $('password').value = ''; await refreshSession(); $('login-dialog').close(); void loadOptions(); void search(); notice('Signed in. Local edits retained.');
    } catch (error) { $('login-error').textContent = error.message; } finally { submit.disabled = false; }
});
installNavigation(() => dirty() || state.pending || state.uncertain, () => state.pending || state.uncertain, message => notice(message, true));
try {
    await refreshSession();
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the Spell catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
