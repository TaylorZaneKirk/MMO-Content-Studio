import {
    createRequest
}
from './studio-common.js';
const fields=[["lectern_definition_id", "Lectern definition id"], ["station_xp_percent", "Station xp percent"], ["station_auto_ms", "Station auto ms"], ["station_manual_ms", "Station manual ms"], ["focus_drain_ms", "Focus drain ms"], ["focus_reduction_percent", "Focus reduction percent"], ["fishing_focus_level", "Fishing focus level"], ["cooking_focus_level", "Cooking focus level"], ["mining_focus_level", "Mining focus level"], ["blacksmithing_focus_level", "Blacksmithing focus level"], ["woodcutting_focus_level", "Woodcutting focus level"], ["crafting_focus_level", "Crafting focus level"], ["farming_focus_level", "Farming focus level"], ["alchemy_focus_level", "Alchemy focus level"], ["study_duration_ms", "Inventory study duration (ms)"]];
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
const familyLabel=document.createElement('label');
familyLabel.textContent='Slime family members (published Mob IDs, one per line)';
const members=document.createElement('textarea'); members.rows=3;
members.addEventListener('input',invalidate); familyLabel.append(members); $('fields').append(familyLabel);
const specimenRows=document.createElement('div'); $('fields').append(specimenRows);
const specimenColumns=[['item_id','Item ID'],['required_level','Minimum Insight'],['base_xp','Base XP'],['mastery_points','Mastery points']];
function addSpecimen(specimen={}) {
    const row=document.createElement('fieldset'); row.className='specimen-row';
    const legend=document.createElement('legend'); legend.textContent='Study specimen — Slime family'; row.append(legend);
    for(const [key,label] of specimenColumns){
        const wrapper=document.createElement('label'); wrapper.textContent=label;
        const input=document.createElement('input'); input.dataset.field=key;
        input.type=key==='item_id'?'text':'number'; input.step='1'; input.value=specimen[key]??'';
        input.addEventListener('input',invalidate); wrapper.append(input); row.append(wrapper);
    }
    const remove=document.createElement('button'); remove.type='button'; remove.textContent='Remove specimen';
    remove.onclick=()=>{ row.remove(); invalidate(); }; row.append(remove); specimenRows.append(row);
}
const add=document.createElement('button'); add.type='button'; add.textContent='Add specimen';
add.onclick=()=>{addSpecimen();invalidate();}; $('fields').append(add);
const progression=document.createElement('details'); const summary=document.createElement('summary');
summary.textContent='Approved milestone progression (read only)'; progression.append(summary);
const progressionText=document.createElement('pre'); progressionText.style.whiteSpace='pre-wrap'; progression.append(progressionText); $('fields').append(progression);
function invalidate(){
    revision++;
    prepared=null;
    $('apply').disabled=true;
}
function lock(value){
    busy=value;
    for(const node of document.querySelectorAll('input,textarea,select,button'))node.disabled=value;
    $('apply').disabled=value||!prepared;
}
async function load(){
    if(busy)return;
    lock(true);
    try{
        session=await request('/session');
        current=await request('/lore-definitions/v1');
        for(const [key] of fields)$(key).value=current.draft[key];
        members.value=(current.draft.family?.mob_definition_ids??[]).join('\n');
        specimenRows.replaceChildren(); for(const specimen of current.draft.specimens??[])addSpecimen(specimen);
        progressionText.textContent=(current.mastery_preview??[]).map(m=>`${m.milestone}: ${m.points} points, Insight ${m.required_level} — ${m.category} ${(m.total_basis_points/100).toFixed(1)}%`).join('\n');
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
        draft.family={family_id:'slime',display_name:'Slime',defence_style:'melee',mob_definition_ids:members.value.split('\n').map(s=>s.trim()).filter(Boolean)};
        draft.specimens=[...specimenRows.children].map(row=>{
            const specimen={family_id:'slime'};
            for(const input of row.querySelectorAll('input'))specimen[input.dataset.field]=input.dataset.field==='item_id'?input.value.trim():Number(input.value);
            return specimen;
        });
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
