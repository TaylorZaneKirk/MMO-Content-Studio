import {
    createRequest
}
from './studio-common.js';
const fields=[["lectern_definition_id", "Lectern definition id"], ["station_xp_percent", "Station xp percent"], ["station_auto_ms", "Station auto ms"], ["station_manual_ms", "Station manual ms"], ["focus_drain_ms", "Focus drain ms"], ["focus_reduction_percent", "Focus reduction percent"], ["fishing_focus_level", "Fishing focus level"], ["cooking_focus_level", "Cooking focus level"], ["mining_focus_level", "Mining focus level"], ["blacksmithing_focus_level", "Blacksmithing focus level"], ["woodcutting_focus_level", "Woodcutting focus level"], ["crafting_focus_level", "Crafting focus level"], ["farming_focus_level", "Farming focus level"], ["alchemy_focus_level", "Alchemy focus level"], ["goo_item_id", "Goo item ID"], ["mob_definition_id", "Mob definition ID"], ["study_level", "Study level"], ["study_duration_ms", "Study duration (milliseconds)"], ["study_xp", "Insight XP per study"], ["mastery_studies", "Studies required"], ["mastery_level", "Permanent Insight level required"], ["accuracy_basis_points", "Accuracy bonus (basis points; 50 = 0.5%)"]];
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
    input.type=key.endsWith('_id')?'text':'number';
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
        current=await request('/lore-definitions/v1');
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
            draft[key]=key.endsWith('_id')?value.trim():Number(value);
        }
        const body={
            draft,expected_updated_at_utc:current.updated_at_utc,target_operation:$('operation').value
        };
        const result=await request('/lore-definitions/v1/preview',{
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
        const result=await request('/lore-definitions/v1/'+(draft?'draft':'publish'),{
            method:draft?'PUT':'POST',body
        });
        current=result.definition;
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
