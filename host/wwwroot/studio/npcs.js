// One owner for NPC fields, appearance, diagnostics, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
import { ActorCalibrationEditor, drawActorPreview } from './actor-appearance.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: {}, definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, diagnosticRevision: 0, shops: [], assets: [], direction: 'S', frame: 1, appearanceRevision: 0 };
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
    state.editRevision++; state.appearanceRevision++; state.preview = null; $('review').hidden = true;
    $('dirty-badge').textContent = dirty() ? 'Unsaved edits' : 'Saved'; $('dirty-badge').classList.toggle('dirty', dirty());
    $('npc-title').textContent = state.draft?.display_name || 'Untitled npc';
    $('actor-preview').getContext('2d').clearRect(0,0,600,450);
    $('appearance-status').textContent='Appearance may be stale. Preview current appearance to refresh it.';
    updateActions();

}
function busy(value) { state.pending=value;updateActions();calibration.refreshPermission(); }
function updateActions() {
    const locked = state.pending || state.uncertain || calibration.isBusy() || calibration.isDirty() || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-npc').disabled = state.pending || state.uncertain || calibration.isBusy();
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-npc').disabled = state.pending || calibration.isBusy() || calibration.isDirty() || !state.id;
    $('session-button').disabled = state.pending || calibration.isBusy();
    $('refresh-appearance').disabled=state.pending||state.uncertain||calibration.isBusy()||!state.draft;
    $('open-calibration').disabled=state.pending||state.uncertain||calibration.isBusy()||!state.draft;

    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes={save_draft:'Save the complete NPC as Draft. Saving a Published NPC removes publication. No catalog export is requested.',publish:'Publish the saved NPC and export its catalog. Save or discard local changes first.',disable:'Disable the saved NPC after reference validation. No catalog export is requested.',delete:'Permanently delete a Disabled NPC with no blocking references. No catalog export is requested.'};
    $('operation-note').textContent = notes[$('operation').value];
}
const request = createRequest(() => state.session, () => {
    state.session.authenticated = false; state.session.can_edit = false; updateSession();
});
const calibration=new ActorCalibrationEditor($('calibration'),request,()=>state.session.can_edit&&!state.pending&&!state.uncertain,()=>changed(),()=>!state.pending&&!state.uncertain);
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
    updateActions();calibration.refreshPermission();
}
const draftKeys=['display_name','visual_texture_path','source_width','source_height','visual_anchor_offset_x','visual_anchor_offset_y','visual_render_scale','footprint_width_tiles','footprint_height_tiles','movement_behavior','wander_radius_tiles','tick_interval_ms','idle_chance','interaction_enabled','interaction_range_tiles','default_interaction','default_dialogue_id','notes','visual_mode','composite_visual','shop_definition_id'];
function toDraft(definition){return Object.fromEntries(draftKeys.map(key=>[key,clone(definition[key])]));}
async function loadOptions(){
    const revision=++state.optionsRevision;
    try{const [options,shops,assets]=await Promise.all([request('/npcs/options'),request('/shops'),request('/actor-appearance/assets')]);if(revision!==state.optionsRevision)return;state.options=options;state.shops=shops.items||[];state.assets=assets.assets||[];}
    catch(error){notice(`NPC choices unavailable: ${error.message}. Refresh to retry; local values remain.`,true);}
}
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/npcs?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('npc-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} ${result.items.length === 1 ? 'npc' : 'npcs'}`;
        for (const npc of result.items) {
            const card = button('', () => loadNpc(npc.npc_definition_id), `item-card${npc.npc_definition_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', npc.display_name), element('small', `${npc.npc_definition_id} · ${npc.publication_state}`));
            card.append(copy); $('npc-list').append(card);
        }
        if (!result.items.length) $('npc-list').append(element('p', 'No matching npcs. Try another search or create a new npc.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('npc-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending || calibration.isBusy()) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the NPC before replacing an uncertain write.', true); return false; }
    return (!dirty() && !calibration.isDirty()) || confirm('Discard your unsaved NPC changes?');
}
async function loadNpc(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/npcs/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    calibration.clear();
    state.definition = definition; state.id = definition.npc_definition_id; state.version = definition.updated_at_utc;
    state.draft = toDraft(definition); state.base = toDraft(definition); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = 'save_draft'; render();showAppearance(definition.rigged_sprite_preview);
}
function newNpc(){
    if(!mayReplace())return;calibration.clear();state.loadRevision++;state.definition=null;state.id='';state.version=null;state.base=null;state.uncertain=false;state.remote=undefined;
    state.draft={display_name:'',visual_texture_path:'',source_width:32,source_height:32,visual_anchor_offset_x:0,visual_anchor_offset_y:0,visual_render_scale:1,footprint_width_tiles:1,footprint_height_tiles:1,movement_behavior:'static',wander_radius_tiles:0,tick_interval_ms:1000,idle_chance:.5,interaction_enabled:true,interaction_range_tiles:1,default_interaction:'talk',default_dialogue_id:null,notes:null,visual_mode:'flat_sprite',composite_visual:null,shop_definition_id:null,...clone(state.options.defaults||{})};
    $('operation').value='save_draft';$('reconcile').hidden=true;render();location.hash='detail';notice('Choose a stable NPC ID and author its complete draft. Calibration has a separate save action.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { kind='text', nullable=false, min=0, max=2147483647, readonly=false, path=key, hint='', required=false }={}) {
    const wrapper=element('div',null,'field'), input=element(kind==='multiline'?'textarea':'input');
    input.id=`npc-field-${fields.size}`;fields.set(path,input);input.name=path;input.value=owner[key]??'';input.readOnly=readonly;input.required=required;
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
    const box=element('div',null,'field'),input=element('select');input.id=`npc-field-${fields.size}`;fields.set(path,input);
    const caption=element('label',label);caption.htmlFor=input.id;box.append(caption,input);
    for(const value of values){const option=element('option',value.display_name??value);option.value=value.id??value;input.append(option);}
    if(!values.some(value=>(value.id??value)===owner[key])){const option=element('option',`Stored value: ${owner[key]??'(empty)'}`);option.value=owner[key]??'';input.append(option);}
    input.value=owner[key]??'';input.addEventListener('change',()=>{owner[key]=input.value;changed();});parent.append(box);return input;
}
function checkField(parent,owner,key,label,path=key){
    const box=element('label',null,'dialogue-checkbox'),input=element('input');input.type='checkbox';input.checked=owner[key];fields.set(path,input);
    input.addEventListener('change',()=>{owner[key]=input.checked;changed();});box.append(input,document.createTextNode(label));parent.append(box);
}
function options(key,fallback=[]){return state.options[key]||fallback.map(id=>({id,display_name:id.replaceAll('_',' ')}));}
let referenceTarget=null;
function reference(parent,owner,key,label,type,values=null){
    field(parent,owner,key,label,{nullable:type!=='cosmetic'&&key!=='visual_texture_path',hint:'Choose an available reference or type its stable ID. Existing missing values are preserved.'});
    parent.append(button(`Choose ${label.toLowerCase()}`,()=>{referenceTarget={owner,key,type,values};$('reference-title').textContent=`Choose ${label.toLowerCase()}`;$('reference-search').value='';renderReferences();$('reference-dialog').showModal();$('reference-search').focus();}));
}
function renderReferences(){
    const t=referenceTarget;if(!t)return;const q=$('reference-search').value.toLowerCase();
    const rows=t.values??(t.type==='asset'?state.assets.map(a=>({id:a.resource_path,display_name:a.display_name,url:a.url})):t.type==='dialogue'?state.options.dialogue_references||[]:state.shops.map(s=>({id:s.shop_definition_id,display_name:`${s.display_name} · ${s.publication_state}`})));
    $('reference-hint').textContent='Select a listed reference or leave this dialog to type a missing/unpublished ID. Asset selection does not upload or change files.';$('reference-results').replaceChildren();
    for(const row of rows.filter(r=>`${r.id} ${r.display_name}`.toLowerCase().includes(q)).slice(0,150)){
        const pick=button('',()=>{t.owner[t.key]=row.id;$('reference-dialog').close();render();});pick.append(element('strong',row.display_name),element('small',row.id));if(row.url){const image=element('img');image.src=row.url;image.loading='lazy';image.alt='';image.width=48;image.height=48;pick.prepend(image);}$('reference-results').append(pick);
    }
    if(!$('reference-results').children.length)$('reference-results').append(element('p','No matching references.'));
}
function render(){
    const opened=new Set([...document.querySelectorAll('.section[open]')].map(n=>n.dataset.title));$('empty').hidden=true;$('editor').hidden=false;$('form-fields').replaceChildren();fields.clear();
    $('npc-state').textContent=state.definition?.publication_state?.toUpperCase()||'NEW NPC';$('npc-meta').textContent=state.definition?`${state.id} · Updated ${state.version}`:'Unsaved NPC';
    const d=state.draft;let p=section('NPC details',true,'The complete NPC definition is saved together. Shared calibration is saved separately.');let g=grid(p);
    field(g,state,'id','NPC ID',{required:true,readonly:!!state.definition,path:'npc_definition_id'});field(g,d,'display_name','Display name',{required:true});field(g,d,'notes','Author notes',{kind:'multiline',nullable:true});
    p=section('Appearance',true,'Flat sprites and composite actors share the source texture. Changing mode retains authored data until you explicitly remove incompatible fields.');g=grid(p);
    selectField(g,d,'visual_mode','Visual mode',[{id:'flat_sprite',display_name:'Flat sprite'},{id:'composite_rig',display_name:'Composite rig'}]);reference(g,d,'visual_texture_path','Source PNG','asset');
    field(g,d,'source_width','Source width',{kind:'integer',min:1});field(g,d,'source_height','Source height',{kind:'integer',min:1});field(g,d,'visual_anchor_offset_x','Anchor X',{kind:'coordinate'});field(g,d,'visual_anchor_offset_y','Anchor Y',{kind:'coordinate'});field(g,d,'visual_render_scale','Render scale',{kind:'coordinate'});
    if(!d.composite_visual)p.append(button('Add composite descriptor',()=>{d.composite_visual={schema_version:1,rig_id:'',calibration_id:null,pose_policy:'actor_pose',fixed_direction:null,fixed_frame:null,cosmetic_item_ids:{}};render();}));
    else{
        const v=d.composite_visual,appearance=state.options.actor_appearance||{},rig=appearance.rigs?.find(r=>r.rig_id===v.rig_id);g=grid(p);
        field(g,v,'schema_version','Composite schema version',{kind:'integer',min:1,path:'composite_visual.schema_version'});
        selectField(g,v,'rig_id','Rig',(appearance.rigs||[]).map(r=>({id:r.rig_id,display_name:r.rig_id})),'composite_visual.rig_id').addEventListener('change',render);
        reference(g,v,'calibration_id','Calibration','calibration',(appearance.calibrations||[]).map(c=>({id:c.calibration_id,display_name:`${c.calibration_id} · ${c.rig_id}`})));
        selectField(g,v,'pose_policy','Pose policy',['actor_pose','fixed'],'composite_visual.pose_policy');selectField(g,v,'fixed_direction','Fixed direction',['','N','E','S','W'],'composite_visual.fixed_direction').addEventListener('change',event=>{if(!event.target.value){v.fixed_direction=null;changed();}});
        field(g,v,'fixed_frame','Fixed frame',{kind:'integer',nullable:true,min:1,max:4,path:'composite_visual.fixed_frame',hint:'Clear fixed direction/frame when following movement.'});
        p.append(element('h4','Cosmetic equipment'),element('p','Only socket-bound published visuals are offered. Selection changes neither item definitions nor grip/pose calibration.','muted'));
        const slots=[...new Set([...(appearance.equipped_visuals||[]).filter(v=>v.rig_id===rig?.rig_id).map(v=>v.render_layer_id),...Object.keys(v.cosmetic_item_ids)])];
        for(const slot of slots){const row=element('div',null,'repeat-row');reference(row,v.cosmetic_item_ids,slot,`Cosmetic · ${slot}`,'cosmetic',(appearance.equipped_visuals||[]).filter(e=>e.rig_id===v.rig_id&&e.render_layer_id===slot).map(e=>({id:e.item_id,display_name:e.item_id})));row.append(button('Remove this cosmetic',()=>{delete v.cosmetic_item_ids[slot];render();}));p.append(row);}
        const unknown=Object.fromEntries(Object.entries(v).filter(([key])=>!['schema_version','rig_id','calibration_id','pose_policy','fixed_direction','fixed_frame','cosmetic_item_ids'].includes(key)));
        if(Object.keys(unknown).length)p.append(element('p','Additional descriptor metadata is preserved. The browser blocks normalization that would remove it.','message Warning'),element('pre',JSON.stringify(unknown,null,2),'readonly'));
        p.append(button('Remove composite descriptor',()=>{if(confirm('Explicitly remove the complete composite descriptor and cosmetic selections?')){d.composite_visual=null;render();}}));
    }
    p=section('Footprint & movement',false,'Static NPCs require zero wander radius. Changing movement never clears the radius automatically.');g=grid(p);
    field(g,d,'footprint_width_tiles','Footprint width · tiles',{kind:'integer',min:1});field(g,d,'footprint_height_tiles','Footprint height · tiles',{kind:'integer',min:1});selectField(g,d,'movement_behavior','Movement',options('movement_behaviors',['static','wander']));
    field(g,d,'wander_radius_tiles','Wander radius · tiles',{kind:'integer'});field(g,d,'tick_interval_ms','Movement tick · milliseconds',{kind:'integer',min:1});field(g,d,'idle_chance','Idle probability · 0 to 1',{kind:'coordinate'});
    p=section('Interactions',true,'This contract has one default interaction. Disabling interactions retains the dialogue reference until explicitly cleared.');g=grid(p);checkField(g,d,'interaction_enabled','Interactions enabled');
    field(g,d,'interaction_range_tiles','Interaction range · tiles',{kind:'integer',min:1});selectField(g,d,'default_interaction','Default interaction',options('interaction_types',['talk','trade']));reference(g,d,'default_dialogue_id','Dialogue','dialogue');reference(g,d,'shop_definition_id','Shop','shop');
    for(const node of document.querySelectorAll('.section'))if(opened.has(node.dataset.title))node.open=true;changed();
}
async function showAppearance(preview){
    const revision=++state.appearanceRevision,d=clone(state.draft);$('appearance-status').textContent=preview?'Resolved composite preview. Item grip/pose editing remains in desktop Items.':'Flat source preview. Use Preview current appearance for the current pose and composite layers.';
    try{await drawActorPreview($('actor-preview'),preview||{base_url:`/studio/api/actor-appearance/image?resource=${encodeURIComponent(d.visual_texture_path)}`,source_width:d.source_width,source_height:d.source_height,cosmetics:[],foreground_overlays:[]},()=>revision===state.appearanceRevision);}
    catch(error){if(revision===state.appearanceRevision)$('appearance-status').textContent=`Appearance unavailable: ${error.message}`;}
}
function showReferences(refs){$('npc-references').textContent=refs?`${refs.known_reference_count} known references · check ${refs.reference_check_complete?'complete':'incomplete'} · ${refs.reference_sources.join(', ')}`:'';}
async function previewAppearance(){
    if(state.pending||state.uncertain||!state.draft||!validForm())return;
    if(!state.session.can_edit){showAppearance(state.definition?.rigged_sprite_preview);notice('Read-only view shows the saved appearance. Exact source frames remain available in calibration.');return;}
    const revision=state.editRevision;busy(true);
    try{const result=await request(`/npcs/${encodeURIComponent(state.id||'appearance_preview')}/preview`,{method:'POST',body:{...clone(state.draft),expected_updated_at_utc:state.version,target_operation:'save_draft',preview_direction:state.direction,preview_frame:state.frame}});if(revision===state.editRevision){showAppearance(result.preview.rigged_sprite_preview);showReferences(result.preview.reference_summary);notice('Appearance preview only. No NPC or calibration changes saved.');}}
    catch(error){notice(error.message,true);}finally{busy(false);}
}
$('preview-direction').addEventListener('change',event=>{state.direction=event.target.value;});$('preview-frame').addEventListener('change',event=>{state.frame=Number(event.target.value);});$('refresh-appearance').addEventListener('click',previewAppearance);
$('open-calibration').addEventListener('click',async()=>{
    const d=state.draft,v=d?.composite_visual;if(!v){notice('Add a composite descriptor and select its rig before opening calibration.',true);return;}
    const rig=state.options.actor_appearance?.rigs.find(r=>r.rig_id===v.rig_id);if(!rig){notice('Select an available rig before calibrating.',true);return;}
    await calibration.open({actorKind:'npc',resource:d.visual_texture_path,rig,calibrationId:v.calibration_id||'',onUse:id=>{v.calibration_id=id;render();}});
});
$('close-reference').addEventListener('click',()=>$('reference-dialog').close());$('reference-search').addEventListener('input',renderReferences);

function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || null;
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
function showPayloadChanges(normalized, operation) {
    if (operation !== 'save_draft' || !normalized) return;
    const before = state.base || {};
    for (const key of draftKeys) {
        if (JSON.stringify(before[key]) === JSON.stringify(normalized[key])) continue;
        const box = element('details', null, 'change'); box.open = true;
        box.append(element('summary', `Complete ${key.replaceAll('_',' ')} change`), element('pre', `Before\n${JSON.stringify(before[key] ?? null,null,2)}\n\nAfter normalization\n${JSON.stringify(normalized[key],null,2)}`)); $('review-changes').append(box);
    }
    if (JSON.stringify(state.draft) !== JSON.stringify(normalized)) $('review-changes').prepend(element('p','The host normalizes ID casing, whitespace and row order. The complete normalized changes below are what this signature authorizes.','message Warning'));
}
function validForm() {
    const invalid = $('npc-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || calibration.isBusy() || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (operation !== 'save_draft' && dirty()) { notice('Save or discard local edits before applying an operation to the saved NPC.', true); return; }
    if (!/^[a-z][a-z0-9]*(_[a-z0-9]+)*$/.test(state.id)) { notice('Use a stable lowercase NPC ID, with optional underscore-separated words.', true); fields.get('npc_definition_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { ...clone(state.draft), expected_updated_at_utc: state.version, target_operation: operation, preview_direction:state.direction,preview_frame:state.frame };
    state.preview = null; busy(true);
    try {
        const response = await request(`/npcs/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
        const result = response.preview;
        if (revision !== state.editRevision || id !== state.id) return;
        const applicable = (operation === 'publish' ? result.valid_for_publication : result.valid_for_draft) && !result.messages.some(message=>String(message.severity).toLowerCase()==='error');
        state.preview = { valid: applicable, signature: result.preview_signature, revision, id, operation, payload };
        messages(result.messages); showChanges(result.changes); showPayloadChanges(response.normalized_draft, operation); showAppearance(result.rigged_sprite_preview); showReferences(result.reference_summary); $('review').hidden = false;
        notice(applicable ? 'Review the changes below, then apply this exact preview.' : 'Resolve the validation messages and preview again.', !applicable);
        $('review').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    finally { busy(false); }
}
async function apply() {
    const reviewed = state.preview;
    if (state.pending || state.uncertain || calibration.isBusy() || calibration.isDirty() || !reviewed?.valid || reviewed.revision !== state.editRevision || reviewed.id !== state.id) return;
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} NPC ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/npcs/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: reviewed.operation === 'save_draft'
                ? { ...Object.fromEntries(draftKeys.map(key=>[key,reviewed.payload[key]])), expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
                : { expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'NPC catalog export succeeded.', failed: 'NPC catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'NPC catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the NPC changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || calibration.isBusy() || calibration.isDirty() || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/npcs/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(toDraft(state.remote), null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The NPC is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing NPC'; $('keep-local').textContent = 'Keep edits as a new draft';
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
    else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.uncertain = false; state.remote = undefined;
        $('editor').hidden = true; $('empty').hidden = false; $('reconcile').hidden = true; location.hash = 'list'; }
    notice('Server result acknowledged. Export status is not inferred from stored content.'); updateActions(); void search();
});
$('keep-local').addEventListener('click', () => {
    if (state.pending || state.remote === undefined || !confirm('Keep these edits against the current server result? A fresh preview is required before writing.')) return;
    state.definition = state.remote; state.version = state.remote?.updated_at_utc ?? null; state.base = state.remote ? toDraft(state.remote) : null;
    state.uncertain = false; state.remote = undefined; $('reconcile').hidden = true;
    $('operation').value = 'save_draft'; render();
    notice('Local edits retained against the current version. Preview again before applying.');
});
$('npcs-workspace').addEventListener('click', () => location.hash = 'list');
$('new-npc').addEventListener('click', newNpc);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', changed); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-npc').addEventListener('click', reconcile); $('npc-form').addEventListener('submit', event => event.preventDefault());

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
installNavigation(() => dirty() || state.pending || state.uncertain || calibration.isDirty() || calibration.isBusy(), () => state.pending || state.uncertain || calibration.isBusy(), message => notice(message, true));
try {
    await refreshSession();
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the NPC catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
