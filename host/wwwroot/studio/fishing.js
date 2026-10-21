// One owner for Fishing method fields, reference choices, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: { items: [] }, definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, reference: null, lookupStatus: { items: 'Loading…' } };
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
    $('method-title').textContent = state.draft?.display_name || 'Untitled method'; updateActions();
}
function busy(value) { state.pending = value; updateActions(); }
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-method').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-method').disabled = state.pending || !state.id;
    $('session-button').disabled = state.pending;
    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes = {
        save_draft: state.definition?.has_published_snapshot
            ? 'Saving retains the last published method until you publish or disable it.'
            : 'Save the complete method as Draft. New methods must be saved before publishing.',
        publish: 'Publish the saved method. Save or discard edits first. All referenced items must be Published. Methods are selected through world-object interactions.',
        disable: 'Disable the saved method and remove it from the exported catalog. Save or discard edits first.',
        delete: 'Permanently delete a method with no published snapshot. Disable its retained publication first.'
    };
    $('operation-note').textContent = notes[$('operation').value];
}
const request = createRequest(() => state.session, () => {
    state.session.authenticated = false; state.session.can_edit = false; updateSession();
});
async function refreshSession() { state.session = await request('/session'); updateSession(); }
function updateSession() {
    $('connection').textContent = (state.session.trusted_home_lan || state.session.external_without_password) ? (state.session.read_only ? 'Home LAN · read only' : 'Home LAN · shared editor')
        : !state.session.configured ? 'Local preview · read only' : !state.session.authenticated ? 'Sign in to edit'
        : state.session.read_only ? 'Signed in · read only' : 'Connected · owner';
    $('session-button').textContent = state.session.authenticated ? 'Sign out' : 'Sign in';
    $('session-button').hidden = !state.session.configured || (state.session.trusted_home_lan || state.session.external_without_password);
    $('access-notice').hidden = !(state.session.trusted_home_lan || state.session.external_without_password);
    $('access-notice').textContent = state.session.read_only
        ? 'Trusted home LAN · no individual sign-in. This host currently permits viewing only.'
        : 'Trusted home LAN · anyone on the allowed network can view, upload, edit, publish and delete content. No individual sign-in.';
    if (state.session.external_without_password) {
        $('connection').textContent = state.session.read_only ? 'External access · read only' : 'External access · shared editor';
        $('access-notice').textContent = state.session.read_only
            ? 'External access without sign-in. This host currently permits viewing only.'
            : 'External access without sign-in. Anyone who can reach this address can view and edit content.';
    }
    updateActions();
}
async function loadOptions() {
    const revision = ++state.optionsRevision;
    // Independent catalogs may fail separately. Never reconstruct a draft when choices refresh.
    const results = await Promise.allSettled([request('/items')]);
    if (revision !== state.optionsRevision) return;
    ['items'].forEach((kind, index) => {
        const result = results[index];
        if (result.status === 'fulfilled') { state.options[kind] = result.value.items; state.lookupStatus[kind] = `${state.options[kind].length} choices loaded`; }
        else { state.lookupStatus[kind] = `Choices unavailable: ${result.reason.message}. Refresh to retry; references are retained.`; }
    });
    for (const node of document.querySelectorAll('[data-reference-kind]')) referenceInfo(node, node.dataset.referenceKind, node.dataset.referenceId);
    if ($('reference-dialog').open) renderPicker();
}
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/fishing?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('method-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} methods`;
        for (const method of result.items) {
            const card = button('', () => loadMethod(method.action_id), `item-card${method.action_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', method.display_name), element('small', `${method.action_id} · ${method.publication_state}`));
            card.append(copy); $('method-list').append(card);
        }
        if (!result.items.length) $('method-list').append(element('p', 'No matching methods. Try another search or create a new method.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('method-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the Method before replacing an uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved Method changes?');
}
async function loadMethod(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/fishing/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    state.definition = definition; state.id = definition.action_id; state.version = definition.updated_at_utc;
    state.draft = clone(definition.draft); state.base = clone(definition.draft); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = 'save_draft'; render();
}
function newMethod() {
    if (!mayReplace()) return;
    state.loadRevision++; state.definition = null; state.id = ''; state.version = null; state.base = null;
    state.uncertain = false; state.remote = undefined;
    state.draft = { action_id:'', display_name:'', tool_item_id:'', consumable_item_id:null, consume_quantity:0, interval_ms:3000, catches:[] };
    $('operation').value = 'save_draft'; $('reconcile').hidden = true; render(); location.hash = 'detail';
    notice('Choose a stable Method ID, then choose a tool, optional consumable and ordered catches. Preview before saving.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { integer = false, min = 0, max = 2147483647, readonly = false, multiline = false, path = key, hint = '', required = false } = {}) {
    const wrapper = element('div', null, 'field'); const input = element(multiline ? 'textarea' : 'input');
    input.id = `method-field-${fields.size}`; fields.set(path, input); input.name = path;
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
        // Method quantities are Int32, safely representable as JS numbers. Item prices remain strings.
        owner[key] = integer && validate() ? Number(input.value) : input.value; changed();
    });
    if (hint) wrapper.append(element('small', hint)); parent.append(wrapper); return input;
}
function referenceInfo(node, kind, id) {
    node.dataset.referenceKind = kind; node.dataset.referenceId = id ?? '';
    const item = state.options[kind].find(row => (kind === 'items' ? row.item_id : row.definition_id) === id);
    node.textContent = !id ? 'No reference selected.' : item ? `${item.display_name} · ${item.publication_state}` : `${id} · Missing or unavailable; reference preserved.`;
}
function reference(parent, owner, key, label, kind, path = key, nullable = false) {
    const input = field(parent, owner, key, label, { path, required: !nullable, hint: nullable ? 'Blank means no consumable. Existing references are never silently replaced.' : 'Enter a stable ID, or use searchable choices.' });
    const info = element('p', null, 'muted stock-summary'); referenceInfo(info, kind, owner[key]); parent.append(info);
    const actions = element('div', null, 'row-actions'); actions.append(button('Choose ' + (kind === 'stations' ? 'station' : 'item'), () => {
        state.reference = { owner, key, kind, path, nullable }; $('reference-title').textContent = label; $('reference-search').value = ''; renderPicker(); $('reference-dialog').showModal(); $('reference-search').focus();
    }));
    const link = element('a', 'Find in ' + (kind === 'stations' ? 'World Objects' : 'Items') + ' ↗', 'reference-link'); link.target = '_blank'; link.rel = 'noopener';
    function updateLink() {
        referenceInfo(info, kind, owner[key]); link.hidden = !owner[key]; link.href = `${kind === 'stations' ? 'world-objects.html' : 'index.html'}?search=${encodeURIComponent(owner[key] || '')}#list`;
    }
    input.addEventListener('input', () => { if (nullable && input.value === '') { owner[key] = null; changed(); } updateLink(); });
    updateLink(); actions.append(link); parent.append(actions);
}
function render() {
    const opened = new Set([...document.querySelectorAll('.section[open]')].map(node => node.dataset.title));
    $('empty').hidden = true; $('editor').hidden = false; $('form-fields').replaceChildren(); fields.clear();
    $('method-state').textContent = state.definition?.publication_state?.toUpperCase() || 'NEW METHOD';
    $('method-meta').textContent = state.definition ? `${state.id} · Updated ${state.version}` : 'Unsaved method';
    const d = state.draft;
    const basics = section('Method details', true, 'The action ID matches a world-object interaction. The carried tool is reusable; consumables are spent only with a committed successful catch.');
    const basicFields = grid(basics);
    const idInput = field(basicFields, state, 'id', 'Action ID', {readonly:!!state.definition, path:'definition_id', required:true});
    idInput.addEventListener('input', () => { if (!state.definition) {d.action_id=state.id; changed();} });
    field(basicFields,d,'display_name','Method display name',{required:true});
    reference(basics,d,'tool_item_id','Reusable carried tool','items');
    reference(basics,d,'consumable_item_id','Stackable consumable (optional)','items','consumable_item_id',true);
    field(basicFields,d,'consume_quantity','Quantity per successful catch (zero without consumable)',{integer:true,min:0,max:1000});
    field(basicFields,d,'interval_ms','Attempt interval in milliseconds',{integer:true,min:600,max:60000});
    const catches = section('Ordered catches',true,'Try each eligible row in order, stopping at the first success. The lowest catch level admits the method. Each roll retains the existing separate-floor interpolation on a 0–255 random roll.');
    d.catches.forEach((row,index)=>{
        const box=element('div',null,'repeat-row');box.append(element('p',`Catch ${index+1}`,'row-heading'));
        reference(box,row,'item_id','Caught item','items',`catches.${index}.item_id`);
        for(const [key,label,min,max] of [['required_level','Fishing level',1,99],['xp','XP on success',1,100000],['success_low','Curve low',0,256],['success_high','Curve high',0,256]])
            field(box,row,key,label,{integer:true,min,max,path:`catches.${index}.${key}`});
        const up=button('Move up',()=>{[d.catches[index-1],d.catches[index]]=[row,d.catches[index-1]];render();});up.disabled=index===0;
        const down=button('Move down',()=>{[d.catches[index+1],d.catches[index]]=[row,d.catches[index+1]];render();});down.disabled=index===d.catches.length-1;
        box.append(up,down,button('Remove',()=>{d.catches.splice(index,1);render();}));catches.append(box);
    });
    catches.append(button('+ Add catch',()=>{d.catches.push({item_id:'',required_level:1,xp:1,success_low:32,success_high:192});render();}));
    for (const node of document.querySelectorAll('.section')) if (opened.has(node.dataset.title)) node.open = true;
    changed();
}
function renderPicker() {
    const ref = state.reference; if (!ref) return;
    $('reference-results').replaceChildren(); $('reference-status').textContent = state.lookupStatus[ref.kind];
    $('reference-hint').textContent = 'Choose a published item. Missing references stay visible until corrected.';
    if (ref.nullable) $('reference-results').append(button('No consumable', () => { if (state.pending || state.uncertain) return; ref.owner[ref.key] = null; $('reference-dialog').close(); render(); }));
    const query = $('reference-search').value.trim().toLowerCase();
    const matches = state.options[ref.kind].filter(item => `${item.display_name} ${item.item_id || item.definition_id}`.toLowerCase().includes(query));
    for (const item of matches) {
        const id = ref.kind === 'items' ? item.item_id : item.definition_id;
        const duplicate = ref.path.startsWith('catches.') && state.draft.catches.some(row => row !== ref.owner && row.item_id === id);
        const choice = button(item.display_name, () => { if (state.pending || state.uncertain) return; ref.owner[ref.key] = id; $('reference-dialog').close(); render(); }); choice.disabled = duplicate;
        choice.append(element('small', `${id} · ${item.publication_state}${duplicate ? ' · Already a catch' : ''}`)); $('reference-results').append(choice);
    }
    if (!matches.length) $('reference-results').append(element('p','No matching choices. Try another search or refresh. Existing references remain unchanged.','muted'));
}
function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || (message.field === 'action_id' ? fields.get('definition_id') : null);
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
function validForm() {
    const invalid = $('method-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (operation !== 'save_draft' && dirty()) { notice('Save or discard local edits before applying an operation to the saved Method.', true); return; }
    if (!/^[a-z][a-z0-9_]*$/.test(state.id)) { notice('Use a stable lowercase Method ID, with optional underscore-separated words.', true); fields.get('definition_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { draft: clone(state.draft), expected_updated_at_utc: state.version, preview_signature: null, target_operation: operation };
    state.preview = null; busy(true);
    try {
        const result = await request(`/fishing/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
        if (revision !== state.editRevision || id !== state.id) return;
        state.preview = { valid: result.applicable, signature: result.preview_signature, revision, id, operation, payload };
        messages(result.messages); showChanges(result.changes); $('review').hidden = false;
        notice(result.applicable ? 'Review the changes below, then apply this exact preview.' : 'Resolve the validation messages and preview again.', !result.applicable);
        $('review').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    finally { busy(false); }
}
async function apply() {
    const reviewed = state.preview;
    if (state.pending || state.uncertain || !reviewed?.valid || reviewed.revision !== state.editRevision || reviewed.id !== state.id) return;
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} Method ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/fishing/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: { ...reviewed.payload, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'Method catalog export succeeded.', failed: 'Method catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'Method catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the Method changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/fishing/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(state.remote.draft, null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The Method is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing Method'; $('keep-local').textContent = 'Keep edits as a new draft';
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
    state.definition = state.remote; state.version = state.remote?.updated_at_utc ?? null; state.base = state.remote ? clone(state.remote.draft) : null;
    state.uncertain = false; state.remote = undefined; $('reconcile').hidden = true;
    $('operation').value = 'save_draft'; render();
    notice('Local edits retained against the current version. Preview again before applying.');
});
$('fishing-workspace').addEventListener('click', () => location.hash = 'list');
$('new-method').addEventListener('click', newMethod);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', changed); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-method').addEventListener('click', reconcile); $('method-form').addEventListener('submit', event => event.preventDefault());
$('close-reference').addEventListener('click', () => $('reference-dialog').close()); $('reference-search').addEventListener('input', renderPicker); $('refresh-references').addEventListener('click', loadOptions);
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
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the Method catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !(state.session.trusted_home_lan || state.session.external_without_password)) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
