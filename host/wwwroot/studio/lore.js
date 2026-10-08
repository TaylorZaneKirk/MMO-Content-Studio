// This page owns settings edits, preview/apply, and explicit draft reconciliation.
import {
    createRequest, installNavigation
}
from './studio-common.js';
const fields=[["lectern_definition_id", "Lectern definition id"], ["station_xp_percent", "Station xp percent"], ["station_auto_ms", "Station auto ms"], ["station_manual_ms", "Station manual ms"], ["focus_drain_ms", "Focus drain ms"], ["focus_reduction_percent", "Focus reduction percent"], ["fishing_focus_level", "Fishing focus level"], ["cooking_focus_level", "Cooking focus level"], ["mining_focus_level", "Mining focus level"], ["blacksmithing_focus_level", "Blacksmithing focus level"], ["woodcutting_focus_level", "Woodcutting focus level"], ["crafting_focus_level", "Crafting focus level"], ["farming_focus_level", "Farming focus level"], ["alchemy_focus_level", "Alchemy focus level"], ["study_duration_ms", "Inventory study duration (ms)"]];
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
    prepared=null;
    $('apply').disabled=true;
}
// Compare raw inputs, not parsed numbers: even an incomplete edit must be retained.
function localInputs(){
    const local=Object.fromEntries(fields.map(([key])=>[key,$(key).value]));
    local.family_members=members.value;
    local.specimens=[...specimenRows.children].map(row=>Object.fromEntries(
        [...row.querySelectorAll('input')].map(input=>[input.dataset.field,input.value])));
    return local;
}
function dirty(){ return retainedEdits || (base!==null && JSON.stringify(localInputs())!==base); }
function renderDefinition(definition){
    for(const [key] of fields)$(key).value=definition.draft[key];
    members.value=(definition.draft.family?.mob_definition_ids??[]).join('\n');
    specimenRows.replaceChildren();
    for(const specimen of definition.draft.specimens??[])addSpecimen(specimen);
    progressionText.textContent=(definition.mastery_preview??[]).map(m=>`${m.milestone}: ${m.points} points, Insight ${m.required_level} — ${m.category} ${(m.total_basis_points/100).toFixed(1)}%`).join('\n');
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
    try { return await request('/lore-definitions/v1'); }
    catch(e) { if(e.status!==404)throw e; return null; }
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
    // Rebase only the version. Keep every raw input, including Insight's row edits.
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
    if(busy||uncertain||!prepared||!session.can_edit)return;
    lock(true);
    const body=prepared;
    invalidate();
    try{
        const draft=body.target_operation==='save_draft';
        const result=await request('/lore-definitions/v1/'+(draft?'draft':'publish'),{
            method:draft?'PUT':'POST',body
        });
        if(result?.database_committed!==true || (!result.definition && body.target_operation!=='delete'))
            throw new Error('The host did not confirm the complete write outcome.');
        adopt(result.definition||null);
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
