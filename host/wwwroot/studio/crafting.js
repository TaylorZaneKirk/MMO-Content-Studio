import {
    createRequest
}
from './studio-common.js';
const fields=[["saw_item_id", "Saw item id"], ["hammer_item_id", "Hammer item id"], ["log_item_id", "Log item id"], ["plank_item_id", "Plank item id"], ["nails_item_id", "Nails item id"], ["chair_item_id", "Chair item id"], ["station_definition_id", "Station definition id"], ["saw_level", "Saw level"], ["saw_xp_tenths", "Saw xp tenths"], ["saw_duration_ms", "Saw duration ms"], ["chair_level", "Chair level"], ["chair_xp_tenths", "Chair xp tenths"], ["chair_duration_ms", "Chair duration ms"], ["chair_planks", "Chair planks"], ["chair_nails", "Chair nails"], ["placement_level", "Placement level"], ["footprint_width_tiles", "Footprint width tiles"], ["footprint_height_tiles", "Footprint height tiles"], ["occupies_furniture_space", "Occupies furniture space"], ["blocks_movement", "Blocks movement"], ["east_texture_path", "East texture path"], ["west_texture_path", "West texture path"]];
const defaults={"saw_item_id": "inventory_175_saw", "hammer_item_id": "blacksmithing_hammer", "log_item_id": "inventory_174_logs", "plank_item_id": "normal_plank", "nails_item_id": "bronze_nails", "chair_item_id": "crude_wooden_chair", "station_definition_id": "home_workbench", "saw_level": 1, "saw_xp_tenths": 25, "saw_duration_ms": 3000, "chair_level": 1, "chair_xp_tenths": 580, "chair_duration_ms": 3000, "chair_planks": 2, "chair_nails": 2, "placement_level": 1, "footprint_width_tiles": 1, "footprint_height_tiles": 1, "occupies_furniture_space": true, "blocks_movement": false, "east_texture_path": "res://assets/maps/objects/world_objects/MapGFX_83_C4.png", "west_texture_path": "res://assets/maps/objects/world_objects/MapGFX_84_D4.png"};
let session={
}, current=null, prepared=null, busy=false, revision=0;
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
function invalidate(){
    revision++;
    prepared=null;
    $('apply').disabled=true;
}
function lock(value){
    busy=value;
    for(const node of document.querySelectorAll('input,select,button'))node.disabled=value;
    $('apply').disabled=value||!prepared;
}
async function load(){
    if(busy)return;
    lock(true);
    try{
        session=await request('/session');
        try { current=await request('/crafting-definitions/v1'); }
        catch(e) {
            if(e.status!==404) throw e;
            current={draft:structuredClone(defaults),updated_at_utc:null,publication_state:'New draft'};
        }
        for(const [key] of fields)$(key).value=current.draft[key];
        invalidate();
        $('connection').textContent=session.can_edit?'Editor connected':'Read only';
        $('status').textContent='Loaded '+current.publication_state;
    }
    catch(e){
        $('status').textContent=e.message;
    }
    finally{
        lock(false);
    }
}
$('reload').onclick=load;
$('operation').onchange=invalidate;
$('fields').onsubmit=e=>e.preventDefault();
$('preview').onclick=async()=>{
    if(busy||!current)return;
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
    if(busy||!prepared)return;
    lock(true);
    const body=prepared;
    invalidate();
    try{
        const draft=body.target_operation==='save_draft';
        const result=await request('/crafting-definitions/v1/'+(draft?'draft':body.target_operation),{
            method:draft?'PUT':'POST',body
        });
        current=result.definition||{draft:structuredClone(defaults),updated_at_utc:null,publication_state:"Deleted — ready for new draft"};
        for(const [key]of fields)$(key).value=current.draft[key];
        $('status').textContent='Saved '+current.publication_state+'. '+(result.messages||[]).map(m=>m.message).join(' ');
    }
    catch(e){
        $('status').textContent=e.message+' Reload before retrying; the server may have saved the operation.';
    }
    finally{
        lock(false);
    }
};
load();
