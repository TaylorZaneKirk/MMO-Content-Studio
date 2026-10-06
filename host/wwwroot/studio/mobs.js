// One owner for Mob fields, appearance, diagnostics, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
import { ActorCalibrationEditor, drawActorPreview } from './actor-appearance.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: {}, definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, assets: [], direction: 'S', frame: 1, appearanceRevision: 0 };
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
    $('mob-title').textContent = state.draft?.display_name || 'Untitled mob';
    $('actor-preview').getContext('2d').clearRect(0,0,600,450);
    $('appearance-status').textContent='Appearance may be stale. Preview current appearance to refresh it.';
    staleDiagnostics();updateActions();

}
function busy(value) { state.pending=value;updateActions();calibration.refreshPermission(); }
function updateActions() {
    const locked = state.pending || state.uncertain || calibration.isBusy() || calibration.isDirty() || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain || calibration.isBusy();
    $('preview-direction').disabled=state.pending||state.uncertain;
    $('preview-frame').disabled=state.pending||state.uncertain;
    $('new-mob').disabled = state.pending || state.uncertain || calibration.isBusy();
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-mob').disabled = state.pending || calibration.isBusy() || calibration.isDirty() || !state.id;
    $('session-button').disabled = state.pending || calibration.isBusy();
    $('refresh-appearance').disabled=state.pending||state.uncertain||calibration.isBusy()||!state.draft;
    $('open-calibration').disabled=state.pending||state.uncertain||calibration.isBusy()||!state.draft;

    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes={save_draft:'Save the complete Mob as Draft. Saving a Published Mob removes publication. No catalog export is requested.',publish:'Publish the saved Mob and export its catalog. Save or discard local changes first.',disable:'Disable the saved Mob after reference validation. No catalog export is requested.',delete:'Permanently delete a Draft or Disabled Mob after existing reference guards. No catalog export is requested.'};
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
const draftKeys=['display_name', 'visual_texture_path', 'source_width', 'source_height', 'visual_anchor_offset_x', 'visual_anchor_offset_y', 'visual_render_scale', 'footprint_width_tiles', 'footprint_height_tiles', 'max_health', 'movement_speed_tiles_per_second', 'movement_behavior', 'wander_radius_tiles', 'aggression_mode', 'aggression_radius_tiles', 'leash_radius_tiles', 'return_home_behavior', 'combat_faction_id', 'can_proactively_target_hostile_mobs', 'mob_detection_radius_tiles', 'mob_target_scan_interval_ms', 'mob_target_scan_candidate_limit', 'primary_combat_profile', 'combat_bonuses', 'guaranteed_drops', 'visual_mode', 'composite_visual', 'root_loot_table_id'];
function toDraft(definition){return Object.fromEntries(draftKeys.map(key=>[key,key==='guaranteed_drops'?definition[key].map(({drop_order,item_id,stack_count})=>({drop_order,item_id,stack_count})):clone(definition[key])]));}
async function loadOptions(){
    const revision=++state.optionsRevision;
    try{const [options,assets]=await Promise.all([request('/mobs/options'),request('/actor-appearance/assets')]);if(revision!==state.optionsRevision)return;state.options=options;state.assets=assets.assets||[];}
    catch(error){notice(`Mob choices unavailable: ${error.message}. Refresh to retry; local values remain.`,true);}
}
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/mobs?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('mob-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} ${result.items.length === 1 ? 'mob' : 'mobs'}`;
        for (const mob of result.items) {
            const card = button('', () => loadMob(mob.mob_definition_id), `item-card${mob.mob_definition_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', mob.display_name), element('small', `${mob.mob_definition_id} · ${mob.publication_state}`));
            card.append(copy); $('mob-list').append(card);
        }
        if (!result.items.length) $('mob-list').append(element('p', 'No matching mobs. Try another search or create a new mob.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('mob-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending || calibration.isBusy()) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the Mob before replacing an uncertain write.', true); return false; }
    return (!dirty() && !calibration.isDirty()) || confirm('Discard your unsaved Mob and shared calibration changes?');
}
async function loadMob(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/mobs/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    calibration.clear();
    state.definition = definition; state.id = definition.mob_definition_id; state.version = definition.updated_at_utc;
    state.draft = toDraft(definition); state.base = toDraft(definition); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = 'save_draft'; render();showAppearance(definition.rigged_sprite_preview);showDiagnostics(definition,true);$('diagnostic-status').textContent='Diagnostics for the loaded saved version.';
}
function newMob(){
    if(!mayReplace())return;calibration.clear();state.loadRevision++;state.definition=null;state.id='';state.version=null;state.base=null;state.uncertain=false;state.remote=undefined;
    const defaults=state.options.defaults||{};
    state.draft={display_name:'',visual_texture_path:'',source_width:32,source_height:32,visual_anchor_offset_x:0,visual_anchor_offset_y:0,visual_render_scale:.25,footprint_width_tiles:1,footprint_height_tiles:1,max_health:10,movement_speed_tiles_per_second:1.25,movement_behavior:'static',wander_radius_tiles:0,aggression_mode:'retaliatory',aggression_radius_tiles:0,leash_radius_tiles:6,return_home_behavior:'return_to_spawn',combat_faction_id:null,can_proactively_target_hostile_mobs:false,mob_detection_radius_tiles:0,mob_target_scan_interval_ms:0,mob_target_scan_candidate_limit:0,primary_combat_profile:null,combat_bonuses:zeroBonuses(),guaranteed_drops:[],visual_mode:'flat_sprite',composite_visual:null,root_loot_table_id:null};
    for(const key of draftKeys)if(key in defaults)state.draft[key]=clone(defaults[key]);
    $('operation').value='save_draft';$('reconcile').hidden=true;render();location.hash='detail';notice('Choose a stable Mob ID and author its complete draft. Calibration has a separate save action.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { kind='text', nullable=false, min=0, max=2147483647, readonly=false, path=key, hint='', required=false }={}) {
    const wrapper=element('div',null,'field'), input=element(kind==='multiline'?'textarea':'input');
    input.id=`mob-field-${fields.size}`;fields.set(path,input);input.name=path;input.value=owner[key]??'';input.readOnly=readonly;input.required=required;
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
    const box=element('div',null,'field'),input=element('select');input.id=`mob-field-${fields.size}`;fields.set(path,input);
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
    field(parent,owner,key,label,{nullable:type!=='cosmetic'&&key!=='visual_texture_path'&&key!=='item_id',hint:'Choose an available reference or type its stable ID. Existing missing values are preserved.'});
    parent.append(button(`Choose ${label.toLowerCase()}`,()=>{referenceTarget={owner,key,type,values};$('reference-title').textContent=`Choose ${label.toLowerCase()}`;$('reference-search').value='';renderReferences();$('reference-dialog').showModal();$('reference-search').focus();}));
}
function renderReferences(){
    const t=referenceTarget;if(!t)return;const q=$('reference-search').value.toLowerCase();
    const rows=t.values??(t.type==='asset'?state.assets.map(a=>({id:a.resource_path,display_name:a.display_name,url:a.url})):[]);
    $('reference-hint').textContent='Select a listed reference or leave this dialog to type a missing/unpublished ID. Asset selection does not upload or change files.';$('reference-results').replaceChildren();
    for(const row of rows.filter(r=>`${r.id} ${r.display_name}`.toLowerCase().includes(q)).slice(0,150)){
        const pick=button('',()=>{t.owner[t.key]=row.id;$('reference-dialog').close();render();});pick.append(element('strong',row.display_name),element('small',row.id));if(row.url){const image=element('img');image.src=row.url;image.loading='lazy';image.alt='';image.width=48;image.height=48;pick.prepend(image);}$('reference-results').append(pick);
    }
    if(!$('reference-results').children.length)$('reference-results').append(element('p','No matching references.'));
}
function render(){
    const opened=new Set([...document.querySelectorAll('.section[open]')].map(n=>n.dataset.title));$('empty').hidden=true;$('editor').hidden=false;$('form-fields').replaceChildren();fields.clear();
    $('mob-state').textContent=state.definition?.publication_state?.toUpperCase()||'NEW Mob';$('mob-meta').textContent=state.definition?`${state.id} · Updated ${state.version}`:'Unsaved Mob';
    const d=state.draft;let p=section('Mob details',true,'The complete Mob definition is saved together. Shared calibration is saved separately.');let g=grid(p);
    field(g,state,'id','Mob ID',{required:true,readonly:!!state.definition,path:'mob_definition_id'});field(g,d,'display_name','Display name',{required:true});field(g,d,'max_health','Maximum Health',{kind:'integer',min:1});
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
    p=section('Movement & aggression',false,'Mode changes retain values. Explicitly clear inactive wander, aggression or target-scan values before saving.');g=grid(p);
    field(g,d,'footprint_width_tiles','Footprint width · tiles',{kind:'integer',min:1});field(g,d,'footprint_height_tiles','Footprint height · tiles',{kind:'integer',min:1});
    field(g,d,'movement_speed_tiles_per_second','Movement speed · tiles per second',{kind:'coordinate'});
    selectField(g,d,'movement_behavior','Movement',options('movement_behaviors',['static','random_wander']));
    field(g,d,'wander_radius_tiles','Wander radius · tiles',{kind:'integer'});
    selectField(g,d,'aggression_mode','Aggression',options('aggression_modes',['passive','retaliatory','proactive']));
    field(g,d,'aggression_radius_tiles','Aggression radius · tiles',{kind:'integer'});field(g,d,'leash_radius_tiles','Leash radius · tiles',{kind:'integer'});
    selectField(g,d,'return_home_behavior','Return home',options('return_home_behaviors',['none','return_to_spawn']));
    reference(g,d,'combat_faction_id','Combat faction','faction',(state.options.factions||[]).map(f=>({id:f.faction_id,display_name:f.display_name})));
    checkField(p,d,'can_proactively_target_hostile_mobs','Proactively target hostile mobs');g=grid(p);
    field(g,d,'mob_detection_radius_tiles','Mob detection radius · tiles',{kind:'integer'});field(g,d,'mob_target_scan_interval_ms','Target scan interval · milliseconds',{kind:'integer'});field(g,d,'mob_target_scan_candidate_limit','Target scan candidate limit',{kind:'integer'});
    p=section('Combat profile',true,'The existing service supports melee attacks. Levels and bonuses feed the derived diagnostics; Magic level and physical weight are preserved as authored.');
    if(!d.primary_combat_profile)p.append(element('p','No primary combat profile.'),button('Add combat profile',()=>{const v=state.options.defaults||{};d.primary_combat_profile={attack_type:v.attack_type||'melee',accuracy_style:v.accuracy_style||'crush',minimum_range_tiles:v.minimum_range_tiles??1,maximum_range_tiles:v.maximum_range_tiles??1,attack_speed_units:v.attack_speed_units??4,attack_level:1,strength_level:1,defence_level:1,magic_level:0,physical_weight:100};render();}));
    else {
        const v=d.primary_combat_profile;g=grid(p);
        selectField(g,v,'attack_type','Attack type',options('attack_types',['melee']),'primary_combat_profile.attack_type');
        selectField(g,v,'accuracy_style','Accuracy style',options('accuracy_styles',['thrust','slash','crush']),'primary_combat_profile.accuracy_style');
        for(const [key,label] of [['attack_level','Attack level'],['strength_level','Strength level'],['defence_level','Defence level'],['magic_level','Magic level'],['physical_weight','Physical weight · percent'],['minimum_range_tiles','Minimum range · tiles'],['maximum_range_tiles','Maximum range · tiles'],['attack_speed_units','Attack speed · units']])field(g,v,key,label,{kind:'integer',path:`primary_combat_profile.${key}`});
        const timing=element('p',null,'muted');timing.id='combat-timing';p.append(timing);
        p.append(button('Remove combat profile',()=>{if(confirm('Remove the complete primary combat profile? Bonuses and drops remain.')){d.primary_combat_profile=null;render();}}));
    }
    p=section('Combat bonuses',false,'Signed bonuses are stored independently from the primary profile.');g=grid(p);
    if(d.combat_bonuses)for(const option of bonusFields())field(g,d.combat_bonuses,option.id,option.display_name,{kind:'integer',min:-1000000,max:1000000,path:`combat_bonuses.${option.id}`});
    else p.append(button('Add zero bonuses',()=>{d.combat_bonuses=zeroBonuses();render();}));
    p=section('Guaranteed drops & root loot',false,'Order and quantities are explicit. Missing references remain editable. Root loot rolls are separate from guaranteed drops.');g=grid(p);
    reference(g,d,'root_loot_table_id','Root Loot Table','loot',(state.options.loot_tables||[]).map(t=>({id:t.loot_table_id,display_name:`${t.display_name} · ${t.publication_state}`})));
    d.guaranteed_drops.forEach((drop,index)=>{
        const row=element('div',null,'repeat-row');row.append(element('h4',`Guaranteed drop ${index+1}`));const f=grid(row);
        field(f,drop,'drop_order','Order',{kind:'integer',max:255,path:`guaranteed_drops[${index}].drop_order`});
        reference(f,drop,'item_id','Item','item',(state.options.published_drop_items||[]).map(i=>({id:i.item_id,display_name:i.display_name})));
        field(f,drop,'stack_count','Quantity',{kind:'integer',min:1,max:1000000,path:`guaranteed_drops[${index}].stack_count`});
        row.append(button('Remove drop',()=>{d.guaranteed_drops.splice(index,1);render();}));p.append(row);
    });
    p.append(button('Add guaranteed drop',()=>{d.guaranteed_drops.push({drop_order:Math.max(-1,...d.guaranteed_drops.map(r=>Number(r.drop_order)))+1,item_id:'',stack_count:1});render();}));
    for(const node of document.querySelectorAll('.section'))if(opened.has(node.dataset.title))node.open=true;changed();
}
async function showAppearance(preview){
    const revision=++state.appearanceRevision,d=clone(state.draft);$('appearance-status').textContent=preview?'Resolved composite preview. Item grip/pose editing remains in desktop Items.':'Flat source preview. Use Preview current appearance for the current pose and composite layers.';
    try{await drawActorPreview($('actor-preview'),preview||{base_url:`/studio/api/actor-appearance/image?resource=${encodeURIComponent(d.visual_texture_path)}`,source_width:d.source_width,source_height:d.source_height,cosmetics:[],foreground_overlays:[]},()=>revision===state.appearanceRevision);}
    catch(error){if(revision===state.appearanceRevision)$('appearance-status').textContent=`Appearance unavailable: ${error.message}`;}
}
function bonusFields(){return state.options.combat_bonus_fields||['attack_thrust','attack_slash','attack_crush','attack_ranged','attack_magic','strength_melee','strength_ranged','defence_thrust','defence_slash','defence_crush','defence_ranged_light','defence_ranged_standard','defence_ranged_heavy','defence_magic'].map(id=>({id,display_name:id.replaceAll('_',' ')}));}
function zeroBonuses(){return Object.fromEntries(bonusFields().map(f=>[f.id,0]));}
function showDiagnostics(value, saved=false){
    const panel=$('mob-diagnostics');panel.replaceChildren(element('h3',saved?'Saved diagnostics':'Current preview diagnostics'));
    panel.append(element('p',`Derived combat level: ${value.derived_combat_level??'unavailable without a valid profile'}`));
    const diag=value.combat_level_diagnostics;
    if(diag){const rows=element('dl');for(const [key,v] of Object.entries(diag))rows.append(element('dt',key.replaceAll('_',' ')),element('dd',String(v)));panel.append(rows);}
    if(saved&&value.root_loot_table_expected_value){const report=value.root_loot_table_expected_value;panel.append(element('h4','Saved root loot expected value'),element('p',`Reference value: ${report.total_reference_value.display} · No-drop probability: ${report.no_drop_probability.display}`));const detail=element('details');detail.append(element('summary','Complete exact loot report'),element('pre',JSON.stringify(report,null,2),'readonly'));panel.append(detail);}
    panel.append(element('p','Derived values are calculated by the existing host. EnemySpawn reference checks remain deferred in the Mob service; publication/delete previews report its warnings.','muted'));
}
function staleDiagnostics(){
    $('diagnostic-status').textContent='Diagnostics may be stale. Preview the current draft to refresh combat diagnostics; root loot reporting describes the loaded saved definition.';
    if($('combat-timing'))$('combat-timing').textContent=`Attack interval: ${Number(state.draft.primary_combat_profile.attack_speed_units)*(state.options.attack_speed_unit_milliseconds||600)} ms (${state.options.attack_speed_unit_milliseconds||600} ms per unit).`;
}
async function previewAppearance(){
    if(state.pending||state.uncertain||!state.draft||!validForm())return;
    if(!state.session.can_edit){showAppearance(state.definition?.rigged_sprite_preview);notice('Read-only view shows the saved appearance. Exact source frames remain available in calibration.');return;}
    const revision=state.editRevision;busy(true);
    try{const result=await request(`/mobs/${encodeURIComponent(state.id||'appearance_preview')}/preview`,{method:'POST',body:{...clone(state.draft),expected_updated_at_utc:state.version,target_operation:'save_draft',preview_direction:state.direction,preview_frame:state.frame}});if(revision===state.editRevision){showAppearance(result.preview.rigged_sprite_preview);showDiagnostics(result.preview);$('diagnostic-status').textContent='Current draft combat diagnostics. Root loot report refreshes when the saved Mob is loaded.';notice('Appearance preview only. No Mob or calibration changes saved.');}}
    catch(error){notice(error.message,true);}finally{busy(false);}
}
$('preview-direction').addEventListener('change',event=>{state.direction=event.target.value;changed();});$('preview-frame').addEventListener('change',event=>{state.frame=Number(event.target.value);changed();});$('refresh-appearance').addEventListener('click',previewAppearance);
$('open-calibration').addEventListener('click',async()=>{
    const d=state.draft,v=d?.composite_visual;if(!v){notice('Add a composite descriptor and select its rig before opening calibration.',true);return;}
    const rig=state.options.actor_appearance?.rigs.find(r=>r.rig_id===v.rig_id);if(!rig){notice('Select an available rig before calibrating.',true);return;}
    const resource=d.visual_texture_path,rigId=v.rig_id;
    await calibration.open({actorKind:'mob',resource,rig,calibrationId:v.calibration_id||'',onUse:id=>{
        // The form can change while this independent file editor remains open.
        if(state.draft!==d||d.composite_visual!==v||d.visual_texture_path!==resource||v.rig_id!==rigId){
            notice('Appearance context changed. Reopen shared calibration before applying its reference.',true);return false;
        }
        v.calibration_id=id;render();return true;
    }});
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
    const invalid = $('mob-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || calibration.isBusy() || calibration.isDirty() || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (operation !== 'save_draft' && dirty()) { notice('Save or discard local edits before applying an operation to the saved Mob.', true); return; }
    if (!/^[a-z][a-z0-9]*(_[a-z0-9]+)*$/.test(state.id)) { notice('Use a stable lowercase Mob ID, with optional underscore-separated words.', true); fields.get('mob_definition_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { ...clone(state.draft), expected_updated_at_utc: state.version, target_operation: operation, preview_direction:state.direction,preview_frame:state.frame };
    state.preview = null; busy(true);
    try {
        const response = await request(`/mobs/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
        const result = response.preview;
        if (revision !== state.editRevision || id !== state.id) return;
        const applicable = (operation === 'publish' ? result.valid_for_publication : result.valid_for_draft) ;
        state.preview = { valid: applicable, signature: result.preview_signature, revision, id, operation, payload };
        messages(result.messages); showChanges(result.changes); showPayloadChanges(response.normalized_draft, operation); showAppearance(result.rigged_sprite_preview); showDiagnostics(result);$('diagnostic-status').textContent='Exact lifecycle preview diagnostics.'; $('review').hidden = false;
        notice(applicable ? 'Review the changes below, then apply this exact preview.' : 'Resolve the validation messages and preview again.', !applicable);
        $('review').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    finally { busy(false); }
}
async function apply() {
    const reviewed = state.preview;
    if (state.pending || state.uncertain || calibration.isBusy() || calibration.isDirty() || !reviewed?.valid || reviewed.revision !== state.editRevision || reviewed.id !== state.id) return;
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} Mob ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/mobs/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: reviewed.operation === 'save_draft'
                ? { ...Object.fromEntries(draftKeys.map(key=>[key,reviewed.payload[key]])), expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
                : { expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'Mob catalog export succeeded.', failed: 'Mob catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'Mob catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the Mob changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || calibration.isBusy() || calibration.isDirty() || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/mobs/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(toDraft(state.remote), null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The Mob is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing Mob'; $('keep-local').textContent = 'Keep edits as a new draft';
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
$('mobs-workspace').addEventListener('click', () => location.hash = 'list');
$('new-mob').addEventListener('click', newMob);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', changed); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-mob').addEventListener('click', reconcile); $('mob-form').addEventListener('submit', event => event.preventDefault());

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
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the Mob catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
