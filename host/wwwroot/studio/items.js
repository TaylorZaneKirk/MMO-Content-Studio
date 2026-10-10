import { createRequest, installNavigation } from './studio-common.js';
// One owner for item selection, editing, preview and apply. Backend services retain all domain decisions.
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: {}, assets: [], item: null, draft: null, base: null, id: '', version: null,
    preview: null, editRevision: 0, loadRevision: 0, searchRevision: 0, pending: false, uncertain: false, remote: null };
const fields = new Map();
const bonusNames = ['attack_thrust','attack_slash','attack_crush','attack_ranged','attack_magic','strength_melee','strength_ranged','magic_damage_percent','defence_thrust','defence_slash','defence_crush','defence_ranged','defence_magic','parry_base_chance_basis_points','block_base_chance_basis_points'];
const title = name => name.replaceAll('_', ' ').replace(/^./, x => x.toUpperCase());
const assetUrl = resource => `/studio/api/asset?resource=${encodeURIComponent(resource)}`;
function element(tag, text, className) { const node = document.createElement(tag); if (text != null) node.textContent = text; if (className) node.className = className; return node; }
function button(text, action, className) { const node = element('button', text, className); node.type = 'button'; node.addEventListener('click', action); return node; }
function notice(message, error = false) { $('notice').textContent = message; $('notice').classList.toggle('error', error); }
function dirty() { return state.draft !== null && (!state.base || JSON.stringify(state.draft) !== JSON.stringify(state.base)); }
function changed() {
    state.editRevision++; state.preview = null; $('review').hidden = true;
    $('dirty-badge').textContent = dirty() ? 'Unsaved edits' : 'Saved'; $('dirty-badge').classList.toggle('dirty', dirty());
    $('item-title').textContent = state.draft?.display_name || 'Untitled item'; updateActions();
}
function busy(value) {
    state.pending = value; $('form-fields').disabled = value;
    for (const tile of $('asset-grid').querySelectorAll('button')) tile.disabled = value;
    updateActions();
}
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('new-item').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-item').disabled = state.pending || !state.id;
    $('upload').disabled = state.pending || !state.session.can_edit;
    $('session-button').disabled = state.pending;
    const notes = {
        save_draft: state.item?.publication_state === 'Published' ? 'Saving as Draft removes Published state. Dependencies may prevent this.' : 'Save the complete definition. New items are saved as Draft before publication.',
        save_and_publish: 'Edit an existing Published item without disabling it. For new items, Save Draft first.',
        publish: 'Publish the saved definition. Save or discard local changes first.',
        disable: 'Remove Published state while retaining the item and existing possessions. References may block this.',
        delete: 'Permanently delete an unpublished item. Existing references may prevent deletion.'
    };
    $('operation-note').textContent = notes[$('operation').value];
}
const request = createRequest(() => state.session, () => { state.session.authenticated = false; state.session.can_edit = false; updateSession(); });
async function refreshSession() { state.session = await request('/session'); updateSession(); }
function updateSession() {
    if (state.session.trusted_home_lan) $('connection').textContent = state.session.read_only ? 'Home LAN · read only' : 'Home LAN · shared editor';
    else if (!state.session.configured) $('connection').textContent = 'Local preview · read only';
    else if (!state.session.authenticated) $('connection').textContent = 'Sign in to edit';
    else $('connection').textContent = state.session.read_only ? 'Signed in · read only' : 'Connected · owner';
    $('session-button').textContent = state.session.authenticated ? 'Sign out' : 'Sign in';
    $('session-button').hidden = !state.session.configured || state.session.trusted_home_lan;
    $('access-notice').hidden = !state.session.trusted_home_lan;
    $('access-notice').textContent = state.session.read_only
        ? 'Trusted home LAN · no individual sign-in. This host currently permits viewing only.'
        : 'Trusted home LAN · anyone on the allowed network can view, upload, edit, publish and delete content. No individual sign-in.';
    updateActions();
}
async function loadOptionsAndAssets() {
    const outcomes = await Promise.allSettled([request('/items/options'), request('/assets')]);
    if (outcomes[0].status === 'fulfilled') state.options = outcomes[0].value;
    if (outcomes[1].status === 'fulfilled') state.assets = outcomes[1].value.assets;
    const failures = outcomes.filter(result => result.status === 'rejected');
    if (failures.length) notice(`Some host data is unavailable. ${failures.map(result => result.reason.message).join(' ')} Local edits are retained.`, true);
}
async function search() {
    const revision = ++state.searchRevision;
    $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/items?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('item-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} items`;
        if (!result.items.length) $('item-list').append(element('p', 'No matching items. Try another search or create a new item.', 'list-message'));
        for (const item of result.items) {
            const card = button('', () => loadItem(item.item_id), `item-card${item.item_id === state.id ? ' selected' : ''}`);
            const image = element('img'); image.src = assetUrl(item.icon_texture_path); image.alt = ''; image.loading = 'lazy'; image.addEventListener('error', () => image.hidden = true);
            const copy = element('span'); copy.append(element('strong', item.display_name), element('small', `${item.item_id} · ${item.publication_state}`), element('small', item.classification_label));
            card.append(image, copy); $('item-list').append(card);
        }
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('item-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
// Explicit transport mapping keeps read-only labels out of the write contract. Every writable field is retained.
function toDraft(item) {
    const draft = { display_name: item.display_name, icon_texture_path: item.icon_texture_path, stackable: item.stackable,
        consumable_behavior: clone(item.consumable_behavior), equipment: clone(item.equipment),
        tool_capabilities: item.tool_capabilities.map(({ capability_id, power_tier, action_animation_id, effect_resource_id }) => ({ capability_id, power_tier, action_animation_id, effect_resource_id })),
        economy_lifecycle: clone(item.economy_lifecycle) };
    if (draft.equipment) {
        delete draft.equipment.equipment_slot_display_name;
        draft.equipment.requirements = draft.equipment.requirements.map(({ skill_id, required_value }) => ({ skill_id, required_value }));
        draft.equipment.skill_modifiers = draft.equipment.skill_modifiers.map(({ skill_id, modifier_value }) => ({ skill_id, modifier_value }));
    }
    return draft;
}
function mayLeave() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the item before leaving this uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved item changes?');
}
async function loadItem(id) {
    if (!mayLeave()) return;
    const revision = ++state.loadRevision; busy(true);
    try { const item = await request(`/items/${encodeURIComponent(id)}`); if (revision !== state.loadRevision) return; adopt(item); location.hash = 'detail'; notice(''); }
    catch (error) { notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(item) {
    state.item = item; state.id = item.item_id; state.version = item.updated_at_utc;
    state.draft = toDraft(item); state.base = clone(state.draft); state.uncertain = false; state.remote = null;
    $('reconcile').hidden = true; $('operation').value = item.publication_state === 'Published' ? 'save_and_publish' : 'save_draft'; render();
}
function newItem() {
    if (!mayLeave()) return;
    state.loadRevision++; state.item = null; state.id = ''; state.version = null; state.base = null; state.uncertain = false;
    state.draft = { display_name: '', icon_texture_path: '', stackable: false, consumable_behavior: null, equipment: null, tool_capabilities: [],
        economy_lifecycle: { reference_value: '0', trade_policy: 'tradeable', death_behavior: 'ordinary', death_transform_item_id: null, shop_policy: 'not_shop_traded', npc_buy_price: null, npc_sell_price: null, reclaim_policy: 'none', reclaim_value: null, condition_policy_id: null, repair_policy_id: null } };
    $('operation').value = 'save_draft'; $('reconcile').hidden = true; notice('New items begin as Draft. Choose a stable ID and artwork, then preview your changes.'); render(); location.hash = 'detail';
}
function options(key, fallback = []) { return state.options[key] || fallback.map(id => ({ id, display_name: title(id) })); }
function field(parent, object, key, label, { kind = 'text', choices, nullable = false, hint = '', path = key, readonly = false } = {}) {
    const wrapper = element('div', null, 'field'); const input = element(choices ? 'select' : 'input');
    input.id = `field-${fields.size}`; input.name = path; fields.set(path, input);
    const caption = element('label', label); caption.htmlFor = input.id; wrapper.append(caption);
    if (choices) {
        const values = [...choices]; const current = object[key];
        if (nullable) values.unshift({ id: '', display_name: 'None' });
        if (current != null && !values.some(option => option.id === String(current))) values.push({ id: String(current), display_name: `${current} (current value)` });
        if (current == null && !nullable) values.unshift({ id: '', display_name: 'Choose…' });
        for (const choice of values) { const option = element('option', choice.display_name); option.value = choice.id; input.append(option); }
        input.value = current ?? '';
    } else {
        input.type = 'text'; input.value = object[key] ?? '';
        if (kind === 'integer' || kind === 'long') input.inputMode = 'numeric';
        input.readOnly = readonly;
        let suggestions = [];
        if (['result_item_id','death_transform_item_id'].includes(key)) suggestions = options('published_item_references');
        if (key === 'rig_id') suggestions = (state.options.actor_rig_catalog?.rigs || []).map(rig => ({ id: rig.rig_id, display_name: rig.rig_id }));
        if (key === 'render_layer_id') suggestions = (state.options.actor_rig_catalog?.rigs || []).flatMap(rig => rig.layers.map(layer => ({ id: layer.layer_id, display_name: layer.layer_id })));
        if (key === 'socket_id') suggestions = (state.options.actor_rig_catalog?.rigs || []).flatMap(rig => rig.sockets.map(socket => ({ id: socket.socket_id, display_name: socket.socket_id })));
        if (suggestions.length) {
            const list = element('datalist'); list.id = input.id + '-choices'; input.setAttribute('list', list.id);
            for (const choice of suggestions) { const option = element('option', choice.display_name); option.value = choice.id; list.append(option); }
            wrapper.append(list);
        }
    }
    input.addEventListener(choices ? 'change' : 'input', () => {
        const value = input.value;
        const valid = !['integer','long'].includes(kind) || nullable && value === '' || /^-?\d+$/.test(value)
            && (kind === 'long' ? BigInt(value) >= -9223372036854775808n && BigInt(value) <= 9223372036854775807n : BigInt(value) >= -2147483648n && BigInt(value) <= 2147483647n);
        input.setCustomValidity(valid ? '' : 'Enter a whole number within the supported range.');
        object[key] = nullable && value === '' ? null : value; changed();
    });
    wrapper.append(input); if (hint) wrapper.append(element('small', hint)); parent.append(wrapper); return input;
}
function checkbox(parent, object, key, label, onChange = changed) {
    const wrapper = element('label', null, 'toggle'); const input = element('input'); input.type = 'checkbox'; input.checked = !!object[key];
    input.addEventListener('change', () => { object[key] = input.checked; onChange(); }); wrapper.append(input, document.createTextNode(label)); parent.append(wrapper); return input;
}
function section(name, open = false, hint = '') {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = name;
    details.append(element('summary', name)); const content = element('div', null, 'section-content'); if (hint) content.append(element('p', hint, 'muted'));
    details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function optional(parent, owner, key, label, makeDefault, renderFields) {
    const check = element('input'); check.type = 'checkbox'; check.checked = owner[key] != null;
    const wrapper = element('label', null, 'toggle'); wrapper.append(check, document.createTextNode(label)); parent.append(wrapper);
    check.addEventListener('change', () => {
        if (!check.checked && !confirm(`Remove ${label.toLowerCase()} and its saved fields? The change is applied only after preview and save.`)) { check.checked = true; return; }
        owner[key] = check.checked ? makeDefault() : null; render();
    });
    if (owner[key] != null) renderFields(owner[key]);
}
function rows(parent, values, path, label, makeDefault, renderFields, indexKey = null) {
    parent.append(element('h3', label, 'subheading'));
    values.forEach((row, index) => {
        const card = element('div', null, 'repeat-row'); card.append(element('div', `${label} · ${index + 1}`, 'row-heading'));
        renderFields(grid(card), row, `${path}[${index}]`);
        const actions = element('div', null, 'row-actions');
        const move = delta => { const destination = index + delta; [values[index], values[destination]] = [values[destination], values[index]]; renumber(); render(); };
        const up = button('↑ Up', () => move(-1)); up.disabled = index === 0;
        const down = button('↓ Down', () => move(1)); down.disabled = index === values.length - 1;
        actions.append(up, down, button('Remove', () => { if (confirm(`Remove this ${label.toLowerCase()} row?`)) { values.splice(index, 1); renumber(); render(); } })); card.append(actions); parent.append(card);
    });
    const renumber = () => { if (indexKey) values.forEach((row, index) => row[indexKey] = index); };
    parent.append(button(`+ Add ${label.toLowerCase()}`, () => { values.push(makeDefault()); renumber(); render(); }));
}
function readonly(parent, label, value) {
    parent.append(element('p', label, 'readonly-label'), element('pre', JSON.stringify(value, null, 2), 'readonly'));
}
function render() {
    const openSections = new Set([...document.querySelectorAll('.section[open]')].map(section => section.dataset.title));
    fields.clear(); $('form-fields').replaceChildren(); $('empty').hidden = true; $('editor').hidden = false;
    $('item-state').textContent = state.item?.publication_state || 'NEW ITEM';
    $('item-meta').textContent = state.item ? `${state.item.classification_label} · Updated ${state.version}` : 'Choose a permanent, lowercase item ID.';
    const draft = state.draft;
    let part = section('Identity & artwork', true); let form = grid(part);
    const identity = { item_id: state.id };
    const id = field(form, identity, 'item_id', 'Stable item ID', { readonly: !!state.item, hint: 'Lowercase words separated by underscores. Cannot be renamed after creation.' });
    id.addEventListener('input', () => { state.id = id.value; });
    field(form, draft, 'display_name', 'Display name');
    const artwork = element('div', null, 'artwork'); const image = element('img', null, 'art-thumb'); image.alt = 'Item icon';
    if (draft.icon_texture_path) image.src = assetUrl(draft.icon_texture_path); else image.hidden = true;
    image.addEventListener('error', () => { image.hidden = true; }); const artCopy = element('div'); artCopy.append(button('Choose / upload artwork', openAssets), element('small', draft.icon_texture_path || 'No artwork selected'));
    artwork.append(image, artCopy); part.append(artwork); const iconField = field(part, draft, 'icon_texture_path', 'Canonical icon reference', { hint: 'Existing res://assets/items/… PNG reference.' });
    iconField.addEventListener('change', () => { image.hidden = !draft.icon_texture_path; if (draft.icon_texture_path) image.src = assetUrl(draft.icon_texture_path); });
    checkbox(part, draft, 'stackable', 'Stackable in inventory');

    part = section('Consumable behavior');
    optional(part, draft, 'consumable_behavior', 'Consumable behavior', () => ({ use_action: 'eat', consume_quantity: 1, result_item_id: null, success_message: null, usable_in_combat: false, cooldown_ms: 0, use_animation_id: null, use_sound_resource_path: null, requirements: [], effects: [] }), value => {
        const form = grid(part); const prefix = 'consumable_behavior.';
        field(form, value, 'use_action', 'Use action', { choices: options('use_actions',['eat','drink','use']), path: prefix+'use_action' });
        field(form, value, 'consume_quantity', 'Quantity consumed', { kind:'integer', path:prefix+'consume_quantity' });
        field(form, value, 'result_item_id', 'Result item ID', { nullable:true, path:prefix+'result_item_id' });
        field(form, value, 'success_message', 'Success message', { nullable:true, path:prefix+'success_message' });
        field(form, value, 'cooldown_ms', 'Cooldown (milliseconds)', { kind:'integer', path:prefix+'cooldown_ms' });
        field(form, value, 'use_animation_id', 'Use animation ID', { nullable:true, path:prefix+'use_animation_id' });
        field(form, value, 'use_sound_resource_path', 'Sound resource reference', { nullable:true, path:prefix+'use_sound_resource_path' });
        checkbox(part,value,'usable_in_combat','Usable in combat');
        rows(part,value.requirements,prefix+'requirements','Requirement',()=>({requirement_index:0,requirement_type:'skill_minimum',target_id:'',minimum_value:1}),(form,row,path)=>{
            field(form,row,'requirement_type','Type',{choices:options('requirement_types',['skill_minimum']),path:path+'.requirement_type'});
            field(form,row,'target_id','Skill / target ID',{path:path+'.target_id'}); field(form,row,'minimum_value','Minimum value',{kind:'integer',path:path+'.minimum_value'});
        },'requirement_index');
        rows(part,value.effects,prefix+'effects','Effect',()=>({effect_index:0,effect_type:'restore_resource',target_id:'health',amount:1}),(form,row,path)=>{
            field(form,row,'effect_type','Type',{choices:options('effect_types',['restore_resource']),path:path+'.effect_type'});
            field(form,row,'target_id','Resource / target',{choices:options('resource_targets',['health','concentration','special']),path:path+'.target_id'}); field(form,row,'amount','Amount',{kind:'integer',path:path+'.amount'});
        },'effect_index');
    });

    part = section('Equipment & combat');
    optional(part,draft,'equipment','Equipable',()=>({equipment_slot_id:'right_hand',required_strength:1,requirements:[],skill_modifiers:[],combat_bonuses:Object.fromEntries(bonusNames.map(key=>[key,0])),weapon_profile:null,equipped_visual:null,ammunition_profile:null,two_handed:false}),value=>{
        let form=grid(part); const prefix='equipment.';
        field(form,value,'equipment_slot_id','Equipment slot',{choices:options('equipment_slots'),path:prefix+'equipment_slot_id',hint:'Changing slot can invalidate weapon/ammunition data. Existing fields stay visible until you explicitly remove them.'});
        field(form,value,'required_strength','Required strength',{kind:'integer',path:prefix+'required_strength'}); checkbox(part,value,'two_handed','Two-handed');
        rows(part,value.requirements,prefix+'requirements','Skill requirement',()=>({skill_id:'',required_value:1}),(form,row,path)=>{
            field(form,row,'skill_id','Skill',{choices:options('skills'),path:path+'.skill_id'}); field(form,row,'required_value','Required value',{kind:'integer',path:path+'.required_value'});
        });
        rows(part,value.skill_modifiers,prefix+'skill_modifiers','Skill modifier',()=>({skill_id:'',modifier_value:0}),(form,row,path)=>{
            field(form,row,'skill_id','Skill',{choices:options('skills'),path:path+'.skill_id'}); field(form,row,'modifier_value','Modifier',{kind:'integer',path:path+'.modifier_value'});
        });
        part.append(element('h3','Combat bonuses','subheading')); form=grid(part);
        for(const key of bonusNames) {
            const label = key === 'parry_base_chance_basis_points' ? 'Melee parry base chance (bp)'
                : key === 'block_base_chance_basis_points' ? 'Shield block base chance (bp)' : title(key);
            field(form,value.combat_bonuses,key,label,{kind:'integer',path:prefix+'combat_bonuses.'+key});
        }
        part.append(element('p','Passive responses: 100 bp = 1%. Zero disables; positive chance explicitly enables a right-hand melee weapon parry or left-hand shield block. Base Defence adds 5 bp per level above 1, capped at 1000 bp each. No Concentration cost. The Block attack style is separate.','muted'));
        part.append(element('h3','Weapon profile','subheading'));
        optional(part,value,'weapon_profile','Weapon profile',()=>({profile_id:'',attack_type:'melee',minimum_range_tiles:1,maximum_range_tiles:1,attack_speed_units:4,ranged_damage_type:null,ammunition_family:null,maximum_ammunition_tier:null,melee_combat_options:[]}),weapon=>{
            const form=grid(part), base=prefix+'weapon_profile.';
            field(form,weapon,'profile_id','Profile ID',{path:base+'profile_id'});
            field(form,weapon,'attack_type','Attack type',{choices:options('attack_families',['melee','ranged','magic']),path:base+'attack_type'});
            for(const key of ['minimum_range_tiles','maximum_range_tiles','attack_speed_units']) field(form,weapon,key,title(key),{kind:'integer',path:base+key,hint:key==='attack_speed_units'?`One unit = ${state.options.combat_unit_milliseconds || 600} ms.`:''});
            field(form,weapon,'ranged_damage_type','Ranged damage type',{choices:options('ranged_damage_types',['light','standard','heavy']),nullable:true,path:base+'ranged_damage_type'});
            field(form,weapon,'ammunition_family','Ammunition family',{choices:options('ammunition_families',['arrow']),nullable:true,path:base+'ammunition_family'});
            field(form,weapon,'maximum_ammunition_tier','Maximum ammunition tier',{kind:'integer',nullable:true,path:base+'maximum_ammunition_tier'});
            // Preserve a nullable options array until an explicit Add action needs an array.
            if(weapon.melee_combat_options===null) part.append(button('Add melee combat options',()=>{weapon.melee_combat_options=[];render();}));
            else rows(part,weapon.melee_combat_options || (weapon.melee_combat_options=[]),base+'melee_combat_options','Melee option',()=>({option_slot:0,option_id:'',display_name:'',combat_style:'accurate',accuracy_style:'slash'}),(form,row,path)=>{
                for(const key of ['option_id','display_name']) field(form,row,key,title(key),{path:path+'.'+key});
                field(form,row,'combat_style','Combat style',{choices:options('combat_styles',['accurate','aggressive','defensive','controlled']),path:path+'.combat_style'});
                field(form,row,'accuracy_style','Accuracy style',{choices:options('attack_styles',['thrust','slash','crush']),path:path+'.accuracy_style'});
            },'option_slot');
        });
        part.append(element('h3','Ammunition profile','subheading'));
        optional(part,value,'ammunition_profile','Ammunition profile',()=>({ammunition_family:'arrow',ranged_damage_type:'standard',ammunition_tier:1}),ammo=>{
            const form=grid(part),base=prefix+'ammunition_profile.';
            field(form,ammo,'ammunition_family','Ammunition family',{choices:options('ammunition_families',['arrow']),path:base+'ammunition_family'});
            field(form,ammo,'ranged_damage_type','Ranged damage type',{choices:options('ranged_damage_types',['light','standard','heavy']),path:base+'ranged_damage_type'});
            field(form,ammo,'ammunition_tier','Ammunition tier',{kind:'integer',nullable:true,path:base+'ammunition_tier'});
        });
    });

    part = section('Equipped appearance',false,'Precise paper-doll positioning stays in desktop Studio. Existing nudge, grip anchors and pose flags are shown below and preserved with the item.');
    if(!draft.equipment) part.append(element('p','Enable equipment to assign an equipped appearance.','muted'));
    else optional(part,draft.equipment,'equipped_visual','Equipped appearance',()=>({asset_key:'',rig_id:'',binding_type:'rig_layer',render_layer_id:'',socket_id:null,secondary_socket_id:null,nudge:{x:0,y:0},grip_anchors:{},flip_x:null,hidden_poses:null,item_over_grip:null}),visual=>{
        const form=grid(part),base='equipment.equipped_visual.';
        field(form,visual,'asset_key','Visual asset key',{path:base+'asset_key'});
        field(form,visual,'rig_id','Rig ID',{path:base+'rig_id'});
        field(form,visual,'binding_type','Binding',{choices:options('equipped_visual_binding_types',['rig_layer','socket']),path:base+'binding_type'});
        field(form,visual,'render_layer_id','Render layer ID',{path:base+'render_layer_id'});
        field(form,visual,'socket_id','Socket ID',{nullable:true,path:base+'socket_id'});
        readonly(part,'Positioning and per-pose settings · read only',Object.fromEntries(['secondary_socket_id','nudge','grip_anchors','flip_x','hidden_poses','item_over_grip'].map(key=>[key,visual[key]])));
        part.append(element('p','Changing a rig or binding does not clear these settings. Validation may require a follow-up in desktop Studio.','muted'));
    });

    part = section('Tool capabilities');
    rows(part,draft.tool_capabilities,'tool_capabilities','Capability',()=>({capability_id:'',power_tier:1,action_animation_id:null,effect_resource_id:null}),(form,row,path)=>{
        field(form,row,'capability_id','Capability',{choices:options('tool_capabilities'),path:path+'.capability_id'});
        field(form,row,'power_tier','Power tier',{kind:'integer',path:path+'.power_tier'});
        field(form,row,'action_animation_id','Action animation ID',{nullable:true,path:path+'.action_animation_id'});
        field(form,row,'effect_resource_id','Effect resource ID',{nullable:true,path:path+'.effect_resource_id'});
    });
    part=section('Economy & lifecycle');
    optional(part,draft,'economy_lifecycle','Economy metadata',()=>({reference_value:'0',trade_policy:'tradeable',death_behavior:'ordinary',death_transform_item_id:null,shop_policy:'not_shop_traded',npc_buy_price:null,npc_sell_price:null,reclaim_policy:'none',reclaim_value:null,condition_policy_id:null,repair_policy_id:null}),economy=>{
        const form=grid(part),base='economy_lifecycle.';
        field(form,economy,'reference_value','Reference value',{kind:'long',path:base+'reference_value'});
        field(form,economy,'trade_policy','Trade policy',{choices:options('trade_policies',['tradeable','untradeable']),path:base+'trade_policy'});
        field(form,economy,'death_behavior','Death behavior',{choices:options('death_behaviors',['ordinary','always_keep','always_destroy','transform','reclaim']),path:base+'death_behavior'});
        field(form,economy,'death_transform_item_id','Death transform item ID',{nullable:true,path:base+'death_transform_item_id'});
        field(form,economy,'shop_policy','Shop policy',{choices:options('shop_policies',['not_shop_traded','npc_buys','npc_sells','npc_buys_and_sells']),path:base+'shop_policy'});
        for(const key of ['npc_buy_price','npc_sell_price']) field(form,economy,key,title(key),{kind:'long',nullable:true,path:base+key});
        field(form,economy,'reclaim_policy','Reclaim policy',{choices:options('reclaim_policies',['none','fixed_cost']),path:base+'reclaim_policy'});
        field(form,economy,'reclaim_value','Reclaim value',{kind:'long',nullable:true,path:base+'reclaim_value'});
        for(const key of ['condition_policy_id','repair_policy_id']) field(form,economy,key,title(key),{nullable:true,path:base+key,hint:'Reserved for draft planning; existing publication rules apply.'});
    });
    for(const details of document.querySelectorAll('.section')) if(openSections.has(details.dataset.title)) details.open=true;
    changed();
}
function showMessages(messages) {
    $('review-messages').replaceChildren();
    for(const summary of document.querySelectorAll('.section summary')) summary.textContent=summary.parentElement.dataset.title;
    for(const message of messages) {
        const box=element('div',`${message.message}${message.remediation ? ' '+message.remediation : ''}`,`message ${message.severity}`);
        let input=fields.get(message.field);
        if(!input && message.field) input=[...fields.entries()].find(([path])=>path.startsWith(message.field) || message.field.startsWith(path))?.[1];
        if(input) {
            const target=input; const details=target.closest('details');
            if(details && !details.querySelector('summary').textContent.includes(' •')) details.querySelector('summary').textContent+=' • needs attention';
            box.append(button(`Go to ${message.field}`,()=>{if(details)details.open=true;target.focus();target.scrollIntoView({block:'center'});}));
        } else if(message.field) box.append(element('small',message.field));
        $('review-messages').append(box);
    }
}
function formValid() {
    for(const input of fields.values()) if(!input.checkValidity()) { const section=input.closest('details'); if(section)section.open=true;input.reportValidity();return false; }
    if(!state.id || !/^[a-z0-9]+(?:_[a-z0-9]+)*$/.test(state.id)) {notice('Use a stable item ID such as iron_sword.',true);return false;}
    return true;
}
async function preview() {
    if(state.pending || state.uncertain || !formValid()) return;
    const revision=state.editRevision, operation=$('operation').value, id=state.id;
    const payload={...clone(state.draft),expected_updated_at_utc:state.version,target_operation:operation};
    busy(true);state.preview=null;
    try {
        const result=await request(`/items/${encodeURIComponent(id)}/preview`,{method:'POST',body:payload});
        if(revision!==state.editRevision || id!==state.id || operation!==$('operation').value)return;
        state.preview={signature:result.preview_signature,operation,revision,valid:operation==='publish'||operation==='save_and_publish'?result.valid_for_publication:result.valid_for_draft};
        showMessages(result.messages);$('review-changes').replaceChildren();
        if(!result.changes.length)$('review-changes').append(element('p','No logical field changes.','muted'));
        for(const change of result.changes){const row=element('div',null,'change');row.append(element('strong',change.field),element('pre',`Before: ${change.before ?? '—'}\nAfter: ${change.after ?? '—'}`));$('review-changes').append(row);}
        $('review').hidden=false;notice(state.preview.valid?'Preview ready. Review the changes before applying.':'Resolve the validation messages before applying.',!state.preview.valid);
        $('review').scrollIntoView({behavior:'smooth',block:'start'});
    } catch(error) {showMessages(error.errors||[]);$('review').hidden=false;notice(error.message,true); if(error.status===409)state.uncertain=true;}
    finally{busy(false);}
}
async function apply() {
    const current=state.preview;
    if(state.pending || state.uncertain || !current?.valid || current.revision!==state.editRevision)return;
    if(['disable','delete'].includes(current.operation) && !confirm(`${current.operation==='delete'?'Permanently delete':'Disable'} ${state.id}?`))return;
    if(current.operation==='save_draft' && state.item?.publication_state==='Published' && !confirm('Save as Draft and remove Published state?'))return;
    const id=state.id, meta={expected_updated_at_utc:state.version,preview_signature:current.signature};
    const save=current.operation==='save_draft'||current.operation==='save_and_publish';
    const route={save_draft:'draft',save_and_publish:'save-and-publish',publish:'publish',disable:'disable',delete:'delete'}[current.operation];
    busy(true);let result;
    try {
        result=await request(`/items/${encodeURIComponent(id)}/${route}`,{method:current.operation==='save_draft'?'PUT':'POST',body:save?{...clone(state.draft),...meta}:meta});
    } catch(error) {
        state.preview=null;
        // A transport failure or server error can follow a committed DB transaction/export attempt.
        state.uncertain=!error.status || error.status>=500 || error.status===409;
        notice(state.uncertain?`${error.message} The write outcome needs checking. Use Reload / compare; do not apply again yet.`:error.message,true);
        showMessages(error.errors||[]);$('review').hidden=false;busy(false);return;
    }
    if(current.operation==='delete') {
        state.item=null;state.draft=null;state.base=null;state.id='';state.version=null;state.preview=null;
        $('editor').hidden=true;$('empty').hidden=false;notice(`Deleted ${result.deleted_id}.`);location.hash='list';
    } else {
        adopt(result.item);
        const warnings=(result.messages||[]).filter(message=>message.severity==='Warning');
        const exportCopy={not_requested:'No catalog export requested.',succeeded:'Equipment visual catalog export succeeded.',skipped:'Catalog export was skipped; host follow-up is needed.',failed:'Catalog export failed after the database commit; host follow-up is needed.'};
        notice(`Database ${result.item.publication_state==='Published'?'published':'saved as Draft'}. ${exportCopy[result.catalog_export] || 'Catalog export outcome unavailable.'} The live game was not restarted.${warnings.length?' Review the warnings below.':''}`);
        if(result.messages?.length){showMessages(result.messages);$('review-changes').replaceChildren();$('review').hidden=false;}
    }
    busy(false);void search();
}
async function reconcile() {
    if(state.pending || !state.id)return;
    busy(true);
    try {
        const remote=await request(`/items/${encodeURIComponent(state.id)}`);
        state.remote=remote;state.preview=null;state.uncertain=true;
        $('comparison').replaceChildren();
        readonly($('comparison'),'Your local definition',state.draft);
        readonly($('comparison'),`Server definition · ${remote.publication_state} · ${remote.updated_at_utc}`,toDraft(remote));
        $('use-server').textContent='Use server version';$('keep-local').hidden=false;$('reconcile').hidden=false;
        notice('Compare the definitions below. A matching server definition confirms stored content, not successful export or live-game reload.');
        $('reconcile').scrollIntoView({block:'start',behavior:'smooth'});
    } catch(error) {
        if(error.status===404){state.remote=null;state.uncertain=true;$('comparison').replaceChildren(element('p','The item is absent on the server. A prior delete may have completed. Your local definition is still shown in the form.'));
            $('use-server').textContent='Acknowledge missing item';$('keep-local').hidden=true;$('reconcile').hidden=false;notice('Server confirms this item does not currently exist.');}
        else notice(`Could not reconcile: ${error.message} Your local edits remain.`,true);
    } finally{busy(false);}
}
async function openAssets() {
    if(state.pending)return;
    $('asset-dialog').showModal();$('upload-status').textContent='';renderAssets();
    try {const catalog=await request('/assets');state.assets=catalog.assets;renderAssets();}
    catch(error){$('upload-status').textContent=error.message;}
}
function renderAssets() {
    $('asset-grid').replaceChildren();const query=$('asset-search').value.toLowerCase();
    const matches=state.assets.filter(asset=>`${asset.display_name} ${asset.resource_path}`.toLowerCase().includes(query));
    for(const asset of matches){const tile=button(asset.display_name,()=>{if(state.pending)return;state.draft.icon_texture_path=asset.resource_path;$('asset-dialog').close();render();});
        tile.disabled=state.pending;const image=element('img');image.src=asset.url;image.alt='';image.loading='lazy';tile.prepend(image);$('asset-grid').append(tile);}
    if(!matches.length)$('asset-grid').append(element('p','No matching PNGs.','muted'));
}
async function upload() {
    const file=$('upload-file').files[0],name=$('upload-name').value;
    if(!file){$('upload-status').textContent='Choose a PNG first.';return;}
    if(file.size>16*1024*1024){$('upload-status').textContent='PNG exceeds 16 MiB.';return;}
    if(name.length>120 || !/^[A-Za-z0-9_ -][A-Za-z0-9._ -]*\.png$/i.test(name)){$('upload-status').textContent='Choose a simple filename ending in .png, without folders.';return;}
    busy(true);$('close-assets').disabled=true;
    try {
        const result=await request(`/assets/upload?name=${encodeURIComponent(name)}`,{method:'POST',body:file,raw:true});
        state.draft.icon_texture_path=result.asset.resource_path;
        if(!state.assets.some(asset=>asset.resource_path===result.asset.resource_path))state.assets.push(result.asset);
        $('asset-dialog').close();render();notice(result.message+' Import does not save or publish the item.');
    } catch(error){$('upload-status').textContent=`${error.message} If the outcome is unclear, refresh artwork or retry the same file and name; an identical asset is reused.`;}
    finally{busy(false);$('close-assets').disabled=false;}
}
$('items-workspace').addEventListener('click',()=>location.hash='list');
$('new-item').addEventListener('click',newItem);$('refresh-list').addEventListener('click',()=>{void search();void loadOptionsAndAssets();});
let searchTimer;$('search').addEventListener('input',()=>{state.searchRevision++;clearTimeout(searchTimer);searchTimer=setTimeout(search,200);});
$('operation').addEventListener('change',changed);$('preview').addEventListener('click',preview);$('apply').addEventListener('click',apply);
$('reload-item').addEventListener('click',reconcile);$('item-form').addEventListener('submit',event=>event.preventDefault());
$('use-server').addEventListener('click',()=>{
    if(!confirm('Discard local edits and use the current server result?'))return;
    if(state.remote)adopt(state.remote);else{state.uncertain=false;state.draft=null;state.base=null;state.id='';$('editor').hidden=true;$('empty').hidden=false;location.hash='list';}
    notice('Server result acknowledged. Export status is not inferred from the item definition.');updateActions();
});
$('keep-local').addEventListener('click',()=>{
    if(!state.remote||!confirm('Keep your local edits against this server version? Preview will show the changes again before any write.'))return;
    state.item=state.remote;state.version=state.remote.updated_at_utc;state.base=toDraft(state.remote);state.uncertain=false;state.remote=null;$('reconcile').hidden=true;changed();notice('Local edits kept. Preview again before applying.');
});
$('session-button').addEventListener('click',async()=>{
    if(state.session.authenticated){try{await request('/logout',{method:'POST'});await refreshSession();notice('Signed out. Unsaved edits remain in this tab.');}catch(error){notice(error.message,true);}return;}
    $('login-dialog').showModal();$('password').focus();
});
$('cancel-login').addEventListener('click',()=>$('login-dialog').close());
$('login-form').addEventListener('submit',async event=>{
    event.preventDefault();const submit=event.submitter;submit.disabled=true;$('login-error').textContent='';
    try{await refreshSession();await request('/login',{method:'POST',body:{password:$('password').value}});$('password').value='';await refreshSession();$('login-dialog').close();await loadOptionsAndAssets();void search();notice('Signed in. Your local edits are retained.');}
    catch(error){$('login-error').textContent=error.message;}finally{submit.disabled=false;}
});
$('close-assets').addEventListener('click',()=>$('asset-dialog').close());$('asset-dialog').addEventListener('cancel',event=>{if(state.pending)event.preventDefault();});
$('asset-search').addEventListener('input',renderAssets);$('upload').addEventListener('click',upload);
$('upload-file').addEventListener('change',()=>{$('upload-name').value=$('upload-file').files[0]?.name||'';});
$('search').value = new URLSearchParams(location.search).get('search') || '';
installNavigation(() => dirty() || state.pending || state.uncertain, () => state.pending || state.uncertain, message => notice(message, true));
try{await refreshSession();if(state.session.configured&&!state.session.authenticated){notice('Sign in to load the item catalog.');$('catalog-count').textContent='Sign in required';}
    else{await loadOptionsAndAssets();await search();if(!state.session.configured && !state.session.trusted_home_lan)notice('Local read-only preview. Browser writes require host-configured credentials; none are created by this application.');}}
catch(error){notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`,true);$('connection').textContent='Disconnected';}
