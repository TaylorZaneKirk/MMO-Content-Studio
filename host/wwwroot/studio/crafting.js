// This page owns settings edits, preview/apply, and explicit draft reconciliation.
import {
    createRequest, installNavigation
}
from './studio-common.js';
const fields=[["saw_item_id", "Saw item id"], ["hammer_item_id", "Hammer item id"], ["log_item_id", "Arrow shaft log item id"], ["nails_item_id", "Nails item id"], ["station_definition_id", "Station definition id"]];
const defaults={"saw_item_id": "inventory_175_saw", "hammer_item_id": "blacksmithing_hammer", "log_item_id": "inventory_174_logs", "nails_item_id": "bronze_nails", "station_definition_id": "home_workbench", "furniture": [{"recipe_id": "craft_chair", "display_name": "Crude wooden chair", "item_id": "crude_wooden_chair", "level": 1, "xp_tenths": 580, "duration_ms": 3000, "inputs": [{"item_id": "normal_plank", "quantity": 2}, {"item_id": "bronze_nails", "quantity": 2}], "required_tool_ids": ["blacksmithing_hammer", "inventory_175_saw"], "placement_layer": "furniture", "south_texture_path": "", "placement_level": 1, "footprint_width_tiles": 1, "footprint_height_tiles": 1, "occupies_furniture_space": true, "blocks_movement": false, "east_texture_path": "res://assets/maps/objects/world_objects/MapGFX_83_C4.png", "west_texture_path": "res://assets/maps/objects/world_objects/MapGFX_84_D4.png"}], "sawing": [{"recipe_id": "saw_plank", "display_name": "Saw plank", "log_item_id": "inventory_174_logs", "plank_item_id": "normal_plank", "level": 1, "xp_tenths": 25, "duration_ms": 3000}]};
const arrowDefaults={"knife_item_id": "crafting_knife", "shaft_item_id": "arrow_shafts", "headless_item_id": "headless_arrows", "feather_item_id": "feather", "bronze_head_item_id": "bronze_arrowheads", "iron_head_item_id": "iron_arrowheads", "bronze_arrow_item_id": "bronze_arrows", "iron_arrow_item_id": "iron_arrows", "shafts": {"level": 1, "xp_tenths": 50, "duration_ms": 3000}, "headless": {"level": 1, "xp_tenths": 10, "duration_ms": 3000}, "bronze": {"level": 1, "xp_tenths": 13, "duration_ms": 3000}, "iron": {"level": 15, "xp_tenths": 25, "duration_ms": 3000}};
const arrowFields=[["knife_item_id", "Knife Item Id"], ["shaft_item_id", "Shaft Item Id"], ["headless_item_id", "Headless Item Id"], ["feather_item_id", "Feather Item Id"], ["bronze_head_item_id", "Bronze Head Item Id"], ["iron_head_item_id", "Iron Head Item Id"], ["bronze_arrow_item_id", "Bronze Arrow Item Id"], ["iron_arrow_item_id", "Iron Arrow Item Id"], ["shafts.level", "Shafts level"], ["shafts.xp_tenths", "Shafts xp tenths"], ["shafts.duration_ms", "Shafts duration ms"], ["headless.level", "Headless level"], ["headless.xp_tenths", "Headless xp tenths"], ["headless.duration_ms", "Headless duration ms"], ["bronze.level", "Bronze level"], ["bronze.xp_tenths", "Bronze xp tenths"], ["bronze.duration_ms", "Bronze duration ms"], ["iron.level", "Iron level"], ["iron.xp_tenths", "Iron xp tenths"], ["iron.duration_ms", "Iron duration ms"]];
let session={
}, current=null, prepared=null, busy=false, uncertain=false, remote=undefined, base=null, retainedEdits=false;
const $=id=>document.getElementById(id);
const request=createRequest(()=>session,()=>{
    $('status').textContent='Sign in from the Studio home page, then reload.';
});
for(const [key,label] of fields){
    const row=document.createElement('label');
    row.textContent=label;
    row.style.display='block';
    row.style.margin='12px 0';
    const input=document.createElement('input');
    input.id=key;
    input.type=(["saw_item_id", "hammer_item_id", "log_item_id", "plank_item_id", "nails_item_id", "chair_item_id", "station_definition_id", "east_texture_path", "west_texture_path", "occupies_furniture_space", "blocks_movement"]).includes(key)?'text':'number';
    input.step=key.endsWith('_percent')?'any':'1';
    input.addEventListener('input',invalidate);
    row.append(input);
    $('fields').append(row);
}
// Sawing rows feed the same preview/save/publish document as furniture.
const sawingFields=['recipe_id','display_name','log_item_id','plank_item_id','level','xp_tenths','duration_ms'];
const sawingRows=document.createElement('div');$('fields').append(sawingRows);
const addSawing=document.createElement('button');addSawing.type='button';addSawing.textContent='Add sawing recipe';
$('fields').append(addSawing);
addSawing.onclick=()=>{appendSawing({...defaults.sawing[0],recipe_id:'',display_name:'',log_item_id:'',plank_item_id:''});invalidate();};
function appendSawing(facts){
    const row=document.createElement('fieldset');
    const title=document.createElement('legend');title.textContent='Sawing: one log to one plank';row.append(title);
    for(const key of sawingFields){
        const label=document.createElement('label');label.textContent=key.replaceAll('_',' ');label.style.display='block';
        const input=document.createElement('input');input.dataset.field=key;input.value=String(facts[key]??'');
        input.type=['level','xp_tenths','duration_ms'].includes(key)?'number':'text';input.step='1';
        input.addEventListener('input',invalidate);label.append(input);row.append(label);
    }
    const remove=document.createElement('button');remove.type='button';remove.textContent='Remove sawing recipe';
    remove.onclick=()=>{row.remove();invalidate();};row.append(remove);sawingRows.append(row);
}
function sawingInputs(){
    return [...sawingRows.children].map(row=>Object.fromEntries([...row.querySelectorAll('input')].map(input=>[input.dataset.field,input.value])));
}
function sawingDraft(){
    return sawingInputs().map(row=>Object.fromEntries(Object.entries(row).map(([key,value])=>{
        if(['level','xp_tenths','duration_ms'].includes(key)){
            if(!/^\d+$/.test(value))throw new Error('Enter a whole number for sawing '+key);
            return [key,Number(value)];
        }
        return [key,value.trim()];
    })));
}
const furnitureFields=["recipe_id", "display_name", "item_id", "level", "xp_tenths", "duration_ms", "inputs", "required_tool_ids", "placement_layer", "south_texture_path", "placement_level", "footprint_width_tiles", "footprint_height_tiles", "occupies_furniture_space", "blocks_movement", "east_texture_path", "west_texture_path"];
const furnitureRows=document.createElement('div');$('fields').append(furnitureRows);
const addFurniture=document.createElement('button');addFurniture.type='button';addFurniture.textContent='Add furniture recipe';
$('fields').append(addFurniture);
addFurniture.onclick=()=>{appendFurniture({...defaults.furniture[0],recipe_id:'',display_name:'',item_id:''});invalidate();};
function appendFurniture(facts){
    const row=document.createElement('fieldset');
    for(const key of furnitureFields){
        const label=document.createElement('label');label.textContent=key.replaceAll('_',' ');label.style.display='block';
        const input=document.createElement('input');input.dataset.field=key;input.value=Array.isArray(facts[key]) ? (key==='inputs' ? facts[key].map(i=>i.item_id+':'+i.quantity).join(', ') : facts[key].join(', ')) : String(facts[key]??'');
        if(key==='inputs')label.textContent='Materials (item_id:quantity, item_id:quantity)';
        if(key==='required_tool_ids')label.textContent='Retained tools (item_id, item_id; empty for none)';
        input.addEventListener('input',invalidate);label.append(input);row.append(label);
    }
    const remove=document.createElement('button');remove.type='button';remove.textContent='Remove recipe';
    remove.onclick=()=>{row.remove();invalidate();};row.append(remove);furnitureRows.append(row);
}
function furnitureInputs(){
    return [...furnitureRows.children].map(row=>Object.fromEntries([...row.querySelectorAll('input')].map(input=>[input.dataset.field,input.value])));
}
function furnitureDraft(){
    return furnitureInputs().map(row=>Object.fromEntries(Object.entries(row).map(([key,value])=>{
        if(key==='inputs')return [key,value.split(',').filter(v=>v.trim()).map(v=>{const [item_id,n]=v.trim().split(':');if(!item_id||!/^\d+$/.test(n||''))throw new Error('Use item_id:quantity for materials');return {item_id,quantity:Number(n)};})];
        if(key==='required_tool_ids')return [key,value.split(',').map(v=>v.trim()).filter(Boolean)];
        if(['occupies_furniture_space','blocks_movement'].includes(key)){
            if(!['true','false'].includes(value.toLowerCase()))throw new Error('Enter true or false for '+key);
            return [key,value.toLowerCase()==='true'];
        }
        if(['level','xp_tenths','duration_ms','planks','nails','placement_level','footprint_width_tiles','footprint_height_tiles'].includes(key)){
            if(!/^\d+$/.test(value))throw new Error('Enter a whole number for '+key);
            return [key,Number(value)];
        }
        return [key,value.trim()];
    })));
}
const arrowToggle=document.createElement('input');
arrowToggle.type='checkbox';arrowToggle.id='arrows-enabled';
const arrowLabel=document.createElement('label');arrowLabel.textContent='Enable inventory arrow recipes';
arrowLabel.append(arrowToggle);$('fields').append(arrowLabel);arrowToggle.addEventListener('change',invalidate);
for(const [key,label] of arrowFields){
    const row=document.createElement('label');row.textContent=label;row.style.display='block';
    const input=document.createElement('input');input.id='arrows.'+key;
    input.type=key.endsWith('_item_id')?'text':'number';input.step='1';
    input.addEventListener('input',invalidate);row.append(input);$('fields').append(row);
}
const spinningDefaults={"wool_item_id": "raw_wool", "yarn_item_id": "wool_yarn", "station_definition_id": "spinning_wheel", "level": 1, "xp_tenths": 50, "duration_ms": 3000};
const spinningToggle=document.createElement('input');spinningToggle.type='checkbox';
const spinningLabel=document.createElement('label');spinningLabel.textContent='Enable wool spinning';
spinningLabel.append(spinningToggle);$('fields').append(spinningLabel);spinningToggle.addEventListener('change',invalidate);
for(const key of Object.keys(spinningDefaults)){
    const label=document.createElement('label');label.textContent='Spinning '+key.replaceAll('_',' ');label.style.display='block';
    const input=document.createElement('input');input.id='spinning.'+key;input.type=key.endsWith('_id')?'text':'number';
    input.addEventListener('input',invalidate);label.append(input);$('fields').append(label);
}
function arrowValue(settings,key){return key.split('.').reduce((value,part)=>value?.[part],settings);}
function invalidate(){
    prepared=null;
    $('apply').disabled=true;
}
// Compare raw inputs, not parsed numbers: even an incomplete edit must be retained.
function localInputs(){
    const local=Object.fromEntries(fields.map(([key])=>[key,$(key).value]));
    local.sawing=sawingInputs();
    local.furniture=furnitureInputs();
    local.spinning_enabled=spinningToggle.checked;
    for(const key of Object.keys(spinningDefaults))local['spinning.'+key]=$('spinning.'+key).value;
    local.arrows_enabled=arrowToggle.checked;
    for(const [key] of arrowFields)local["arrows."+key]=$("arrows."+key).value;
    return local;
}
function dirty(){ return retainedEdits || (base!==null && JSON.stringify(localInputs())!==base); }
function renderDefinition(definition){
    for(const [key] of fields)$(key).value=definition.draft[key];
    sawingRows.replaceChildren();
    for(const saw of definition.draft.sawing||[])appendSawing(saw);
    furnitureRows.replaceChildren();
    for(const furniture of definition.draft.furniture||[])appendFurniture(furniture);
    spinningToggle.checked=definition.draft.spinning!=null;
    for(const key of Object.keys(spinningDefaults))$('spinning.'+key).value=(definition.draft.spinning||spinningDefaults)[key];
    arrowToggle.checked=definition.draft.arrows!=null;
    for(const [key] of arrowFields)$('arrows.'+key).value=arrowValue(definition.draft.arrows||arrowDefaults,key);
}
function adopt(definition){
    current=definition;
    if(definition)renderDefinition(definition);
    base=definition?JSON.stringify(localInputs()):null;
    retainedEdits=false;
    uncertain=false;
    remote=undefined;
    $('reconcile').hidden=true;
    invalidate();
}
function lock(value){
    busy=value;
    for(const node of document.querySelectorAll('input,textarea,select,button'))
        node.disabled=value||uncertain||!current;
    $('reload').disabled=value;
    $('use-server').disabled=value||remote===undefined;
    $('keep-local').disabled=value||remote===undefined||remote===null;
    $('apply').disabled=value||uncertain||!prepared||!session.can_edit;
    $('preview').disabled=value||uncertain||!current||!session.can_edit;
}
async function readDefinition(){
    try { return await request('/crafting-definitions/v1'); }
    catch(e) { if(e.status!==404)throw e; return {draft:structuredClone(defaults),updated_at_utc:null,publication_state:'Missing — new draft'}; }
}
async function load(){
    if(busy)return;
    const compare=dirty()||uncertain;
    invalidate();
    remote=undefined;
    $('reconcile').hidden=true;
    lock(true);
    try{
        session=await request('/session');
        const loaded=await readDefinition();
        $('connection').textContent=session.can_edit?'Editor connected':'Read only';
        if(compare){
            // A GET never replaces local inputs until the author chooses a side.
            uncertain=true;
            remote=loaded;
            $('local-copy').textContent=JSON.stringify(localInputs(),null,2);
            $('server-copy').textContent=loaded
                ? loaded.publication_state+' · '+loaded.updated_at_utc+'\n'+JSON.stringify(loaded.draft,null,2)
                : 'Settings are missing on the server. Local inputs remain available to copy.';
            $('reconcile').hidden=false;
            $('status').textContent='Compare the saved settings with your retained inputs. Stored content does not prove export or live-game reload.';
        }else{
            adopt(loaded);
            $('status').textContent=loaded?'Loaded '+loaded.publication_state:'Settings are missing on the server.';
        }
    }
    catch(e){ $('status').textContent=e.message+' Local inputs have not been replaced.'; }
    finally{ lock(false); }
}
$('reload').onclick=load;
$('use-server').onclick=()=>{
    if(busy||remote===undefined||!confirm('Discard local edits and use the server version?'))return;
    adopt(remote);
    $('status').textContent='Server result acknowledged. Export status is not inferred from stored settings.';
    lock(false);
};
$('keep-local').onclick=()=>{
    if(busy||!remote)return;
    // Rebase only the version. Keep every raw input.
    current=remote;
    base=null;
    uncertain=false;
    remote=undefined;
    $('reconcile').hidden=true;
    $('operation').value='save_draft';
    invalidate();
    // Retained inputs count as unsaved even if their parsed values match the server.
    retainedEdits=true;
    $('status').textContent='Local inputs retained against the loaded version. Preview again before applying.';
    lock(false);
};
installNavigation(()=>dirty()||busy||uncertain,()=>busy||uncertain,message=>{$('status').textContent=message;});
// Mobile browsers do not reliably show beforeunload. Guard ordinary workspace links too.
document.addEventListener('click',event=>{
    const link=event.target.closest('a[href]');
    if(!link||link.target==='_blank'||event.ctrlKey||event.metaKey||event.defaultPrevented)return;
    if(link.pathname===location.pathname&&link.origin===location.origin)return;
    if(busy||uncertain){event.preventDefault();$('status').textContent='Reload / compare the pending result before leaving.';}
    else if(dirty()&&!confirm('Leave this page and discard unsaved settings?'))event.preventDefault();
});
$('operation').onchange=invalidate;
$('fields').onsubmit=e=>e.preventDefault();
$('preview').onclick=async()=>{
    if(busy||uncertain||!current||!session.can_edit)return;
    lock(true);
    try{
        const draft={
        };
        for(const [key]of fields){
            const value=$(key).value;
            if (["saw_item_id", "hammer_item_id", "log_item_id", "plank_item_id", "nails_item_id", "chair_item_id", "station_definition_id", "east_texture_path", "west_texture_path"].includes(key)) draft[key]=value.trim();
            else if (["occupies_furniture_space", "blocks_movement"].includes(key)) { if(!["true","false"].includes(value.toLowerCase()))throw new Error("Enter true or false for "+key); draft[key]=value.toLowerCase()==="true"; }
            else { if(!/^\d+$/.test(value))throw new Error("Enter a whole number for "+key);draft[key]=Number(value); }
        }
        draft.sawing=sawingDraft();
        draft.furniture=furnitureDraft();
        draft.arrows=null;
        if(arrowToggle.checked){
            draft.arrows=structuredClone(arrowDefaults);
            for(const [key] of arrowFields){
                const value=$('arrows.'+key).value.trim();
                if(!key.endsWith('_item_id')&&!/^\d+$/.test(value))throw new Error('Enter a whole number for '+key);
                const parts=key.split('.');const target=parts.length===2?draft.arrows[parts[0]]:draft.arrows;
                target[parts.at(-1)]=key.endsWith('_item_id')?value:Number(value);
            }
        }
        draft.spinning=null;
        if(spinningToggle.checked){
            draft.spinning={};
            for(const key of Object.keys(spinningDefaults)){
                const value=$('spinning.'+key).value.trim();
                if(!key.endsWith('_id')&&!/^\d+$/.test(value))throw new Error('Enter a whole number for '+key);
                draft.spinning[key]=key.endsWith('_id')?value:Number(value);
            }
        }
        const body={
            draft,expected_updated_at_utc:current.updated_at_utc,target_operation:$('operation').value
        };
        const result=await request('/crafting-definitions/v1/preview',{
            method:'POST',body
        });
        $('changes').replaceChildren();
        for(const change of result.changes){
            const li=document.createElement('li');
            li.textContent=change.field+': '+JSON.stringify(change.before)+' → '+JSON.stringify(change.after);
            $('changes').append(li);
        }
        $('status').textContent=result.messages.map(m=>m.message).join(' ')||'Review the changes before applying.';
        prepared=result.applicable?{
            ...body,preview_signature:result.preview_signature
        }
        :null;
    }
    catch(e){
        invalidate();
        $('status').textContent=e.message;
    }
    finally{
        lock(false);
    }
};
$('apply').onclick=async()=>{
    if(busy||uncertain||!prepared||!session.can_edit)return;
    lock(true);
    const body=prepared;
    invalidate();
    try{
        const draft=body.target_operation==='save_draft';
        const result=await request('/crafting-definitions/v1/'+(draft?'draft':body.target_operation),{
            method:draft?'PUT':'POST',body
        });
        if(result?.database_committed!==true || (!result.definition && body.target_operation!=='delete'))
            throw new Error('The host did not confirm the complete write outcome.');
        adopt(result.definition||{draft:structuredClone(defaults),updated_at_utc:null,publication_state:'Missing — new draft'});
        const exportNote={succeeded:'Catalog export succeeded.',failed:'Catalog export failed; inspect host logs without repeating this mutation.',
            skipped:'Catalog export was skipped; inspect host configuration.',not_requested:'No catalog export was requested.'}[result.catalog_export]||'Catalog export outcome is unknown.';
        $('status').textContent='Database change committed. '+exportNote+' The live game was not restarted. '+(result.messages||[]).map(m=>m.message).join(' ');
    }
    catch(e){
        // A lost response, conflict or server failure must never offer blind resubmission.
        if(!e.status||e.status>=500||e.status===409||e.status===404){
            uncertain=true;
            remote=undefined;
            $('reconcile').hidden=true;
            $('status').textContent=e.message+' The write may have completed or settings changed. Reload / compare before continuing; local inputs are retained.';
        }else{
            $('status').textContent=e.message+' The request was rejected. Correct the retained inputs and preview again.';
        }
    }
    finally{
        lock(false);
    }
};
load();
