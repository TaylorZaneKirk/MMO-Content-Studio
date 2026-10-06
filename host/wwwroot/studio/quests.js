// One owner for Quest steps/transitions, diagnostics, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: {}, definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, diagnosticRevision: 0 };
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
    state.editRevision++; state.preview = null; $('review').hidden = true;
    $('dirty-badge').textContent = dirty() ? 'Unsaved edits' : 'Saved'; $('dirty-badge').classList.toggle('dirty', dirty());
    $('quest-title').textContent = state.draft?.display_name || 'Untitled quest';
    $('diagnostics').dataset.stale = 'true'; $('diagnostic-status').textContent = 'Analysis is stale. Preview current edits, or refresh the saved definition analysis.'; updateActions();
}
function busy(value) { state.pending = value; updateActions(); if (!value) void loadDiagnostics(); }
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-quest').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-quest').disabled = state.pending || !state.id;
    $('session-button').disabled = state.pending;
    $('refresh-diagnostics').disabled = state.pending || state.uncertain || !state.definition || dirty();
    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes = {
        save_draft: state.definition?.publication_state === 'Published'
            ? 'Saving changes Published to Draft. Character state or published Dialogue references can block this. Save Draft does not export the catalog.'
            : 'Save the complete quest as Draft. New quests must be saved before publishing.',
        publish: 'Publish the saved quest. Save or discard edits first. The graph must have valid start and completion paths and retain active character-state steps.',
        disable: 'Disable the saved quest. Character state, pending settlements or published Dialogue references can block this.',
        delete: 'Permanently delete a Disabled quest with no character-state, pending-settlement or published Dialogue references.'
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
    const revision = ++state.optionsRevision;
    try { const options = await request('/quests/options'); if (revision === state.optionsRevision) state.options = options; }
    catch (error) { if (revision === state.optionsRevision) notice(`Quest options unavailable: ${error.message} Refresh to retry. Local edits remain.`, true); }
}
function toDraft(definition) { return { display_name: definition.display_name, schema_version: definition.schema_version, steps: clone(definition.steps), transitions: clone(definition.transitions) }; }
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/quests?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('quest-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} ${result.items.length === 1 ? 'quest' : 'quests'}`;
        for (const quest of result.items) {
            const card = button('', () => loadQuest(quest.quest_id), `item-card${quest.quest_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', quest.display_name), element('small', `${quest.quest_id} · ${quest.publication_state}`));
            card.append(copy); $('quest-list').append(card);
        }
        if (!result.items.length) $('quest-list').append(element('p', 'No matching quests. Try another search or create a new quest.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('quest-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the Quest before replacing an uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved Quest changes?');
}
async function loadQuest(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/quests/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    state.definition = definition; state.id = definition.quest_id; state.version = definition.updated_at_utc;
    state.draft = toDraft(definition); state.base = toDraft(definition); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = 'save_draft'; render();
}
function newQuest() {
    if (!mayReplace()) return;
    state.loadRevision++; state.definition = null; state.id = ''; state.version = null; state.base = null;
    state.uncertain = false; state.remote = undefined;
    state.draft = { display_name: '', schema_version: state.options.defaults?.schema_version ?? 1, steps: [], transitions: [] };
    $('operation').value = 'save_draft'; $('reconcile').hidden = true; render(); location.hash = 'detail';
    notice('Choose a stable Quest ID, then add steps and transitions. Preview before saving.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { integer = false, min = 0, max = 2147483647, readonly = false, multiline = false, path = key, hint = '', required = false } = {}) {
    const wrapper = element('div', null, 'field'); const input = element(multiline ? 'textarea' : 'input');
    input.id = `quest-field-${fields.size}`; fields.set(path, input); input.name = path;
    input.value = owner[key] ?? ''; input.readOnly = readonly; input.required = required;
    if (multiline) input.rows = 4;
    else { input.type = 'text'; if (integer) input.inputMode = 'numeric'; }
    const caption = element('label', label); caption.htmlFor = input.id; wrapper.append(caption, input);
    function validate() {
        const valid = !integer || /^\d+$/.test(input.value) && BigInt(input.value) >= BigInt(min) && BigInt(input.value) <= BigInt(max);
        input.setCustomValidity(valid ? '' : `Enter a whole number from ${min} through ${max}.`); return valid;
    }
    validate();
    input.addEventListener('input', () => {
        // Quest quantities are Int32, safely representable as JS numbers. Item prices remain strings.
        owner[key] = integer && validate() ? Number(input.value) : input.value; changed();
    });
    if (hint) wrapper.append(element('small', hint)); parent.append(wrapper); return input;
}
function selectField(parent, owner, key, label, choices, path) {
    const wrapper = element('div', null, 'field'), select = element('select'); select.id = `quest-field-${fields.size}`; select.name = path; fields.set(path, select);
    const caption = element('label', label); caption.htmlFor = select.id; wrapper.append(caption,select);
    for (const [value,text] of choices) { const option=element('option',text);option.value=value;select.append(option); }
    if (!choices.some(([value])=>value===owner[key])) { const option=element('option',`Unrecognized: ${owner[key]}`);option.value=owner[key];select.append(option); }
    select.value=owner[key];select.addEventListener('change',()=>{ owner[key]=select.value;changed(); });parent.append(wrapper);return select;
}
function nextOrder(array,key) { return array.reduce((max,row)=>typeof row[key]==='number' ? Math.max(max,row[key]) : max,-1)+1; }
function orderActions(parent,array,index,key,label) {
    const actions=element('div',null,'row-actions');
    function move(to) { [array[index],array[to]]=[array[to],array[index]];array.forEach((row,position)=>row[key]=position);render(); }
    const up=button('Move up',()=>move(index-1));up.disabled=index===0;
    const down=button('Move down',()=>move(index+1));down.disabled=index===array.length-1;
    actions.append(up,down,button('Remove',()=>{if(confirm(`Remove ${label} ${index+1}? References are retained so you can repair them explicitly.`)){array.splice(index,1);render();}}));parent.append(actions);
}
function stepReference(parent,row,key,label,path) {
    const input=field(parent,row,key,label,{path,hint:'Use a defined step ID for Active; leave blank for Not Started/Completed. Missing IDs remain visible.'});
    const list=element('datalist');list.id=`quest-step-options-${fields.size}`;input.setAttribute('list',list.id);parent.append(list);
    input.addEventListener('input',()=>{if(input.value===''){row[key]=null;changed();}});
    parent.append(button('Clear '+label.toLowerCase(),()=>{row[key]=null;input.value='';changed();}));
}
function updateStepChoices() {
    for(const list of document.querySelectorAll('datalist')) {
        list.replaceChildren();for(const step of state.draft.steps){const option=element('option');option.value=step.step_id;option.label=step.display_name;list.append(option);}
    }
}
function render() {
    const opened=new Set([...document.querySelectorAll('.section[open]')].map(node=>node.dataset.title));
    $('empty').hidden=true;$('editor').hidden=false;$('form-fields').replaceChildren();fields.clear();
    $('quest-state').textContent=state.definition?.publication_state?.toUpperCase() || 'NEW QUEST';
    $('quest-meta').textContent=state.definition ? `${state.id} · Updated ${state.version}` : 'Unsaved quest';
    const d=state.draft;
    const basics=section('Quest details',true,'Define the quest state graph. Objectives and rewards are not fields in this quest definition.');
    const basicFields=grid(basics);
    field(basicFields,state,'id','Quest ID',{readonly:!!state.definition,path:'quest_id',required:true,hint:'Stable lowercase ID, such as a_meal_delayed.'});
    field(basicFields,d,'display_name','Display name',{required:true});
    field(basicFields,d,'schema_version','Schema version',{integer:true,min:1,hint:`Current supported schema: ${state.options.defaults?.schema_version ?? 1}. Existing values are never rewritten on load.`});
    const steps=section('Ordered steps',true,`Step ID, player-facing label and explicit order. Limit: ${state.options.supported_limits?.max_steps ?? 128}. Typing order numbers preserves them; Move up/down explicitly renumbers this list from 0. Renaming/removing steps does not silently rewrite transitions.`);
    fields.set('steps',steps.parentElement.querySelector('summary'));
    d.steps.forEach((row,index)=>{
        const box=element('div',null,'repeat-row');box.append(element('p',`Step ${index+1}`,'row-heading'));const inputs=grid(box);
        field(inputs,row,'step_id','Step ID',{required:true,path:`steps.${index}.step_id`}).addEventListener('input',updateStepChoices);
        field(inputs,row,'display_name','Step display name',{required:true,path:`steps.${index}.display_name`}).addEventListener('input',updateStepChoices);
        field(inputs,row,'step_order','Step order',{integer:true,max:10000,path:`steps.${index}.step_order`});
        orderActions(box,d.steps,index,'step_order','step');steps.append(box);
    });
    if(!d.steps.length)steps.append(element('p','Add at least one step.','muted'));
    steps.append(button('+ Add step',()=>{d.steps.push({step_id:'',display_name:'',step_order:nextOrder(d.steps,'step_order')});render();}));
    const transitions=section('Ordered transitions',true,`Each row connects a source state to a target state. Source: Not Started or Active. Target: Active or Completed. Active requires a step; other statuses require an empty step. Limit: ${state.options.supported_limits?.max_transitions ?? 256}. Move up/down explicitly renumbers transitions from 0.`);
    fields.set('transitions',transitions.parentElement.querySelector('summary'));
    d.transitions.forEach((row,index)=>{
        const box=element('div',null,'repeat-row');box.append(element('p',`Transition ${index+1}`,'row-heading'));const identity=grid(box);
        field(identity,row,'transition_id','Transition ID',{required:true,path:`transitions.${index}.transition_id`});
        field(identity,row,'transition_order','Transition order',{integer:true,max:10000,path:`transitions.${index}.transition_order`});
        const source=element('div',null,'quest-state-card');source.append(element('h4','From · source state'));const sourceFields=grid(source);
        selectField(sourceFields,row,'source_status','Source status',[['not_started','Not Started'],['active','Active']],`transitions.${index}.source_status`);
        stepReference(source,row,'source_step_id','Source step',`transitions.${index}.source_step_id`);box.append(source);
        const target=element('div',null,'quest-state-card');target.append(element('h4','To · target state'));const targetFields=grid(target);
        selectField(targetFields,row,'target_status','Target status',[['active','Active'],['completed','Completed']],`transitions.${index}.target_status`);
        stepReference(target,row,'target_step_id','Target step',`transitions.${index}.target_step_id`);box.append(target);
        orderActions(box,d.transitions,index,'transition_order','transition');transitions.append(box);
    });
    if(!d.transitions.length)transitions.append(element('p','Add a start transition and a path to completion.','muted'));
    transitions.append(button('+ Add transition',()=>{d.transitions.push({transition_id:'',source_status:'not_started',source_step_id:null,target_status:'active',target_step_id:null,transition_order:nextOrder(d.transitions,'transition_order')});render();}));
    for(const node of document.querySelectorAll('.section'))if(opened.has(node.dataset.title))node.open=true;
    $('diagnostics').hidden=false;if(!state.definition){$('graph-analysis').replaceChildren();$('diagnostic-messages').replaceChildren();}
    changed();updateStepChoices();
}
function graphAnalysis(preview,label) {
    const analysis=preview.analysis;$('graph-analysis').replaceChildren();$('diagnostic-messages').replaceChildren();
    $('diagnostics').hidden=false;$('diagnostics').dataset.stale='false';$('diagnostic-status').textContent=label;
    if(analysis){
        const metrics=element('div',null,'fields');
        for(const [text,value] of [['Start transition',analysis.has_start_transition],['Reachable completion',analysis.has_completion_path]])metrics.append(element('p',`${text}: ${value ? 'Yes' : 'No'}`,`message ${value ? 'Info' : 'Warning'}`));
        $('graph-analysis').append(metrics);
        for(const [text,key] of [['Reachable steps','reachable_step_ids'],['Unreachable steps','unreachable_step_ids'],['Unreachable transitions','unreachable_transition_ids'],['Steps without a completion path','dead_end_step_ids']]){
            const box=element('div',null,'quest-analysis-row');box.append(element('strong',text),element('p',analysis[key]?.length ? analysis[key].join(', ') : 'None'));$('graph-analysis').append(box);
        }
    }
    for(const message of preview.messages||[])$('diagnostic-messages').append(element('div',`${message.message}${message.remediation ? ' '+message.remediation : ''}`,`message ${message.severity}`));
    if(!preview.messages?.length)$('diagnostic-messages').append(element('p','No validation or reference restrictions reported for this operation.','muted'));
}
async function loadDiagnostics() {
    if(!state.definition || dirty() || state.pending || state.uncertain)return;
    const revision=++state.diagnosticRevision,edit=state.editRevision,id=state.id,version=state.version,operation=$('operation').value;
    $('diagnostic-status').textContent='Checking saved graph and references…';
    try{
        const result=await request(`/quests/${encodeURIComponent(id)}/diagnostics?operation=${encodeURIComponent(operation)}`);
        if(revision!==state.diagnosticRevision || edit!==state.editRevision || id!==state.id || version!==state.version)return;
        if(result.definition_version!==version){$('diagnostic-status').textContent='The server definition changed. Reload / compare before continuing.';return;}
        graphAnalysis(result.preview,`Saved definition · ${operation.replaceAll('_',' ')} diagnostics. Preview current changes before applying.`);
    }catch(error){if(revision===state.diagnosticRevision && edit===state.editRevision)$('diagnostic-status').textContent=`Analysis unavailable: ${error.message} Refresh saved analysis to retry.`;}
}
function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || (message.field?.startsWith('steps') ? fields.get('steps') : message.field?.startsWith('transitions') ? fields.get('transitions') : null);
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
    for (const key of ['display_name','schema_version','steps','transitions']) {
        if (JSON.stringify(before[key]) === JSON.stringify(normalized[key])) continue;
        const box = element('details', null, 'change'); box.open = true;
        box.append(element('summary', `Complete ${key.replaceAll('_',' ')} change`), element('pre', `Before\n${JSON.stringify(before[key] ?? null,null,2)}\n\nAfter normalization\n${JSON.stringify(normalized[key],null,2)}`)); $('review-changes').append(box);
    }
    if (JSON.stringify(state.draft) !== JSON.stringify(normalized)) $('review-changes').prepend(element('p','The host normalizes ID casing, whitespace and row order. The complete normalized changes below are what this signature authorizes.','message Warning'));
}
function validForm() {
    const invalid = $('quest-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (operation !== 'save_draft' && dirty()) { notice('Save or discard local edits before applying an operation to the saved Quest.', true); return; }
    if (!/^[a-z][a-z0-9]*(_[a-z0-9]+)*$/.test(state.id)) { notice('Use a stable lowercase Quest ID, with optional underscore-separated words.', true); fields.get('quest_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { ...clone(state.draft), expected_updated_at_utc: state.version, target_operation: operation };
    state.preview = null; busy(true);
    try {
        const response = await request(`/quests/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
        const result = response.preview;
        if (revision !== state.editRevision || id !== state.id) return;
        const applicable = (operation === 'publish' ? result.valid_for_publication : result.valid_for_draft) && !result.messages.some(message=>String(message.severity).toLowerCase()==='error');
        state.preview = { valid: applicable, signature: result.preview_signature, revision, id, operation, payload };
        messages(result.messages); showChanges(result.changes); showPayloadChanges(response.normalized_draft, operation); graphAnalysis(result,'Current draft · exact validation preview'); $('review').hidden = false;
        notice(applicable ? 'Review the changes below, then apply this exact preview.' : 'Resolve the validation messages and preview again.', !applicable);
        $('review').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    finally { busy(false); }
}
async function apply() {
    const reviewed = state.preview;
    if (state.pending || state.uncertain || !reviewed?.valid || reviewed.revision !== state.editRevision || reviewed.id !== state.id) return;
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} Quest ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/quests/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: reviewed.operation === 'save_draft'
                ? { display_name: reviewed.payload.display_name, schema_version: reviewed.payload.schema_version, steps: reviewed.payload.steps, transitions: reviewed.payload.transitions, expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
                : { expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'Quest catalog export succeeded.', failed: 'Quest catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'Quest catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the Quest changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/quests/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(toDraft(state.remote), null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The Quest is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing Quest'; $('keep-local').textContent = 'Keep edits as a new draft';
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
$('quests-workspace').addEventListener('click', () => location.hash = 'list');
$('new-quest').addEventListener('click', newQuest);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', () => { changed(); void loadDiagnostics(); }); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-quest').addEventListener('click', reconcile); $('quest-form').addEventListener('submit', event => event.preventDefault());
$('refresh-diagnostics').addEventListener('click', loadDiagnostics);
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
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the Quest catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
