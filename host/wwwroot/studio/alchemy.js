import {
    createRequest
}
from './studio-common.js';
const fields=[["crystal_item_id", "Crystal Item Id"], ["hammer_item_id", "Hammer Item Id"], ["shard_item_id", "Shard Item Id"], ["dust_item_id", "Dust Item Id"], ["flower_item_id", "Flower Item Id"], ["petals_item_id", "Petals Item Id"], ["bottle_item_id", "Bottle Item Id"], ["potion3_item_id", "Potion3 Item Id"], ["potion2_item_id", "Potion2 Item Id"], ["potion1_item_id", "Potion1 Item Id"], ["station_definition_id", "Station Definition Id"], ["crystal_low_percent", "Crystal Low Percent"], ["crystal_high_percent", "Crystal High Percent"], ["crystal_cap_level", "Crystal Cap Level"], ["crush_level", "Crush Level"], ["crush_duration_ms", "Crush Duration (milliseconds)"], ["crush_xp_tenths", "Crush XP (tenths)"], ["shard_min", "Shard Min"], ["shard_max", "Shard Max"], ["dust_min", "Dust Min"], ["dust_max", "Dust Max"], ["prepare_level", "Prepare Level"], ["prepare_duration_ms", "Prepare Duration (milliseconds)"], ["prepare_xp_tenths", "Prepare XP (tenths)"], ["prepare_flower_quantity", "Prepare Flower Quantity"], ["prepare_petals_quantity", "Prepare Petals Quantity"], ["brew_level", "Brew Level"], ["brew_duration_ms", "Brew Duration (milliseconds)"], ["brew_xp_tenths", "Brew XP (tenths)"], ["brew_bottle_quantity", "Brew Bottle Quantity"], ["brew_dust_quantity", "Brew Dust Quantity"], ["brew_petals_quantity", "Brew Petals Quantity"], ["brew_potion_quantity", "Brew Potion Quantity"], ["attack_bonus_flat", "Attack Bonus Flat"], ["attack_bonus_percent", "Attack Bonus Percent"], ["decay_interval_ms", "Decay Interval (milliseconds)"], ["sip_cooldown_ms", "Sip Cooldown (milliseconds)"]];
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
        current=await request('/alchemy-definitions/v1');
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
        const result=await request('/alchemy-definitions/v1/preview',{
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
        const result=await request('/alchemy-definitions/v1/'+(draft?'draft':'publish'),{
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
