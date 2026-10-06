// One owner for Shop selection, ordered stock, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: [], definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, stockRow: null };
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
    $('shop-title').textContent = state.draft?.display_name || 'Untitled shop'; updateActions();
}
function busy(value) { state.pending = value; updateActions(); }
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-shop').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-shop').disabled = state.pending || !state.id;
    $('session-button').disabled = state.pending;
    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes = {
        save_draft: state.definition?.publication_state === 'Published'
            ? 'Save as Draft unpublishes this Shop. Published NPC references may prevent it; use Save & Publish to keep it published.'
            : 'Save the complete Shop and ordered stock as Draft. New Shops must be saved before publishing.',
        save_and_publish: 'Edit an existing Published Shop without disabling it. New or Draft Shops must use Save Draft, then Publish.',
        publish: 'Publish the saved Shop. Save or discard local edits first. Current item policies and prices are validated.',
        disable: 'Disable the saved Shop. Save or discard edits first. Published NPC references may prevent this.',
        delete: 'Permanently delete a Disabled Shop. Every NPC reference must be removed first. Save or discard edits first.'
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
    try {
        const result = await request('/shops/options'); if (revision !== state.optionsRevision) return;
        state.options = result.items;
        // Update lookup labels only; refreshing options never reconstructs or truncates a draft.
        for (const node of document.querySelectorAll('[data-stock-info]')) stockInfo(node, node.dataset.stockInfo);
        if ($('stock-dialog').open) renderPicker();
    } catch (error) { if (revision === state.optionsRevision) notice(`Item choices unavailable: ${error.message} Refresh to retry; local edits are retained.`, true); }
}
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/shops?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('shop-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} shops`;
        for (const shop of result.items) {
            const card = button('', () => loadShop(shop.shop_definition_id), `item-card${shop.shop_definition_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', shop.display_name), element('small', `${shop.shop_definition_id} · ${shop.publication_state}`));
            card.append(copy); $('shop-list').append(card);
        }
        if (!result.items.length) $('shop-list').append(element('p', 'No matching shops. Try another search or create a new shop.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('shop-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the Shop before replacing an uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved Shop changes?');
}
async function loadShop(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/shops/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    state.definition = definition; state.id = definition.shop_definition_id; state.version = definition.updated_at_utc;
    state.draft = clone(definition.draft); state.base = clone(definition.draft); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = definition.publication_state === 'Published' ? 'save_and_publish' : 'save_draft'; render();
}
function newShop() {
    if (!mayReplace()) return;
    state.loadRevision++; state.definition = null; state.id = ''; state.version = null; state.base = null;
    state.uncertain = false; state.remote = undefined;
    state.draft = { display_name: '', buys_unstocked_items: false, price_change_per_stock_percent: 0, notes: null, stock: [] };
    $('operation').value = 'save_draft'; $('reconcile').hidden = true; render(); location.hash = 'detail';
    notice('Choose a stable Shop ID, then add its stock. Preview before saving.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { integer = false, min = 0, max = 2147483647, readonly = false, multiline = false, path = key, hint = '', required = false } = {}) {
    const wrapper = element('div', null, 'field'); const input = element(multiline ? 'textarea' : 'input');
    input.id = `shop-field-${fields.size}`; fields.set(path, input); input.name = path;
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
        // Shop quantities are Int32, safely representable as JS numbers. Item prices remain strings.
        owner[key] = integer && validate() ? Number(input.value) : input.value; changed();
    });
    if (hint) wrapper.append(element('small', hint)); parent.append(wrapper); return input;
}
function stockInfo(node, id) {
    node.dataset.stockInfo = id; const item = state.options.find(option => option.item_id === id);
    node.textContent = item ? `${item.display_name} · ${item.runtime_enabled ? 'Published' : 'Not Published'} · ${item.shop_policy} · NPC buys for ${item.npc_buy_price ?? 'not set'} / sells for ${item.npc_sell_price ?? 'not set'}`
        : `${id || 'No item selected'} · ${id ? 'Item details unavailable; reference is preserved.' : 'Choose an item below.'}`;
}
function render() {
    const opened = new Set([...document.querySelectorAll('.section[open]')].map(node => node.dataset.title));
    $('empty').hidden = true; $('editor').hidden = false; $('form-fields').replaceChildren(); fields.clear();
    $('shop-state').textContent = state.definition?.publication_state?.toUpperCase() || 'NEW SHOP';
    $('shop-meta').textContent = state.definition ? `${state.id} · Updated ${state.version}` : 'Unsaved definition';
    const basics = section('Shop details', true, 'Item definitions own base prices. This Shop owns its stock and price adjustment rate.');
    const basicFields = grid(basics);
    field(basicFields, state, 'id', 'Shop ID', { readonly: !!state.definition, path: 'definition_id', required: true, hint: 'Stable lowercase ID, such as riverside_general_store.' });
    field(basicFields, state.draft, 'display_name', 'Display name', { required: true });
    field(basicFields, state.draft, 'price_change_per_stock_percent', 'Price change per stock (%)', { integer: true, max: 100 });
    const toggle = element('label', null, 'toggle'), check = element('input'); check.type = 'checkbox'; check.checked = state.draft.buys_unstocked_items;
    check.addEventListener('change', () => { state.draft.buys_unstocked_items = check.checked; changed(); });
    toggle.append(check, document.createTextNode('Buys unstocked items (eligible Item policy still required)')); basics.append(toggle);
    field(basics, state.draft, 'notes', 'Notes (authoring only)', { multiline: true });
    const stock = section('Ordered stock', true, 'Top to bottom is shop order. Default stock is equilibrium. Restock ticks are per item; one tick is 600 ms. Zero default stock requires an item the NPC may buy.');
    state.draft.stock.forEach((row, index) => {
        const box = element('div', null, 'repeat-row'); box.append(element('p', `Stock item ${index + 1}`, 'row-heading'));
        const info = element('p', null, 'stock-summary muted'); stockInfo(info, row.item_id); box.append(info);
        box.append(button(row.item_id ? 'Change item' : 'Choose item', () => openPicker(row)));
        const form = grid(box);
        field(form, row, 'item_id', 'Item ID', { readonly: true, path: `stock.${index}.item_id`, required: true });
        field(form, row, 'default_stock', 'Default stock', { integer: true, path: `stock.${index}.default_stock` });
        field(form, row, 'restock_ticks', 'Restock ticks', { integer: true, min: 1, path: `stock.${index}.restock_ticks` });
        const actions = element('div', null, 'row-actions');
        const up = button('Move up', () => { [state.draft.stock[index - 1], state.draft.stock[index]] = [row, state.draft.stock[index - 1]]; render(); }); up.disabled = index === 0;
        const down = button('Move down', () => { [state.draft.stock[index + 1], state.draft.stock[index]] = [row, state.draft.stock[index + 1]]; render(); }); down.disabled = index === state.draft.stock.length - 1;
        actions.append(up, down, button('Remove', () => { if (confirm(`Remove stock item ${index + 1} (${row.item_id || 'unselected'})?`)) { state.draft.stock.splice(index, 1); render(); } }));
        box.append(actions); stock.append(box);
    });
    if (!state.draft.stock.length) stock.append(element('p', 'No regular stock yet. A Shop may have an empty stock list.', 'muted'));
    stock.append(button('+ Add stock item', () => { state.draft.stock.push({ item_id: '', default_stock: 0, restock_ticks: 1 }); render(); }));
    for (const node of document.querySelectorAll('.section')) if (opened.has(node.dataset.title)) node.open = true;
    changed();
}
function openPicker(row) {
    if (state.pending || state.uncertain) return;
    state.stockRow = row; $('stock-search').value = ''; renderPicker(); $('stock-dialog').showModal(); $('stock-search').focus();
}
function renderPicker() {
    $('stock-results').replaceChildren(); const query = $('stock-search').value.trim().toLowerCase();
    const matches = state.options.filter(item => `${item.display_name} ${item.item_id}`.toLowerCase().includes(query));
    for (const item of matches) {
        const selectedElsewhere = state.draft?.stock.some(row => row !== state.stockRow && row.item_id === item.item_id);
        const choice = button(item.display_name, () => {
            if (state.pending || state.uncertain || !state.draft?.stock.includes(state.stockRow)) return;
            state.stockRow.item_id = item.item_id; $('stock-dialog').close(); state.stockRow = null; render();
        });
        choice.disabled = !!selectedElsewhere;
        choice.append(element('small', `${item.item_id} · ${item.runtime_enabled ? 'Published' : 'Not Published'} · ${item.shop_policy}${selectedElsewhere ? ' · Already in this stock list' : ''}`),
            element('small', `NPC buys: ${item.npc_buy_price ?? 'not set'} · NPC sells: ${item.npc_sell_price ?? 'not set'}`));
        $('stock-results').append(choice);
    }
    if (!matches.length) $('stock-results').append(element('p', state.options.length ? 'No matching items.' : 'No item choices loaded. Close this picker and refresh the library to retry.', 'muted'));
}
function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || (message.field === 'stock' ? fields.get('stock.0.item_id') : null);
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
    const invalid = $('shop-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (!['save_draft', 'save_and_publish'].includes(operation) && dirty()) { notice('Save or discard local edits before applying an operation to the saved Shop.', true); return; }
    if (!/^[a-z][a-z0-9]*(_[a-z0-9]+)*$/.test(state.id)) { notice('Use a stable lowercase Shop ID, with optional underscore-separated words.', true); fields.get('definition_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { draft: clone(state.draft), expected_updated_at_utc: state.version, preview_signature: null, target_operation: operation };
    state.preview = null; busy(true);
    try {
        const result = await request(`/shops/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
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
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} Shop ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/shops/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: { ...reviewed.payload, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'Shop catalog export succeeded.', failed: 'Shop catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'Shop catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the Shop changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/shops/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(state.remote.draft, null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The Shop is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing Shop'; $('keep-local').textContent = 'Keep edits as a new draft';
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
    $('operation').value = state.definition?.publication_state === 'Published' ? 'save_and_publish' : 'save_draft'; render();
    notice('Local edits retained against the current version. Preview again before applying.');
});
$('shops-workspace').addEventListener('click', () => location.hash = 'list');
$('new-shop').addEventListener('click', newShop);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', changed); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-shop').addEventListener('click', reconcile); $('shop-form').addEventListener('submit', event => event.preventDefault());
$('close-stock').addEventListener('click', () => $('stock-dialog').close()); $('stock-search').addEventListener('input', renderPicker);
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
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the Shop catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
