// One owner for Loot Table editing, ordered groups/outcomes, preview/apply and recovery.
// The existing host service decides roll rules, references, exact EV and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: {}, definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, reference: null, evRevision: null };
const fields = new Map();
const expandedCards = new WeakMap();
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
    $('loot-title').textContent = state.draft?.display_name || 'Untitled loot table';
    if (state.evRevision !== null) $('expected-value').dataset.stale = 'true'; updateActions();
}
function busy(value) { state.pending = value; updateActions(); }
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-loot').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-loot').disabled = state.pending || !state.id;
    $('session-button').disabled = state.pending;
    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes = {
        save_draft: 'Save the complete table as Draft, including an existing Published table. This removes Published state; dependent content may be affected. Preview current diagnostics first.',
        publish: 'Publish the saved table. Save or discard local edits first. Item/nested-table publication, cycles and limits are validated.',
        disable: 'Disable the saved table. Published parent-table or Mob references may block this. Save or discard local edits first.',
        delete: 'Permanently delete a Disabled table. References may block deletion. Save or discard local edits first.'
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
        const result = await request('/loot-tables/options'); if (revision !== state.optionsRevision) return;
        state.options = result;
        if ($('reference-dialog').open) renderPicker();
    } catch (error) { if (revision === state.optionsRevision) notice(`Reference choices unavailable: ${error.message} Refresh to retry; local edits remain.`, true); }
}
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/loot-tables?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('loot-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} loot tables`;
        for (const loot of result.items) {
            const card = button('', () => loadTable(loot.loot_table_id), `item-card${loot.loot_table_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', loot.display_name), element('small', `${loot.loot_table_id} · ${loot.publication_state}`));
            copy.append(element('small', `${loot.group_count} groups · ${loot.outcome_count} outcomes`));
            card.append(copy); $('loot-list').append(card);
        }
        if (!result.items.length) $('loot-list').append(element('p', 'No matching loot tables. Try another search or create a new table.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('loot-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the Table before replacing an uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved Table changes?');
}
async function loadTable(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/loot-tables/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    state.definition = definition; state.id = definition.loot_table_id; state.version = definition.updated_at_utc;
    state.draft = toDraft(definition); state.base = clone(state.draft); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = 'save_draft'; render(); showExpectedValue(definition.expected_value, 'Saved definition');
}
function newTable() {
    if (!mayReplace()) return;
    state.loadRevision++; state.definition = null; state.id = ''; state.version = null; state.base = null;
    state.uncertain = false; state.remote = undefined;
    state.draft = { display_name: '', description: '', groups: [] }; state.evRevision = null; $('expected-value').hidden = true;
    $('operation').value = 'save_draft'; $('reconcile').hidden = true; render(); location.hash = 'detail';
    notice('Choose a stable Table ID, then add its roll groups. Preview before saving.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
// Never coerce Int64 probability values through Number; null, zero and blank differ.
function field(parent, owner, key, label, { integer = false, long = false, nullable = false, readonly = false, multiline = false, path = key, hint = '', required = false } = {}) {
    const wrapper = element('div', null, 'field'), input = element(multiline ? 'textarea' : 'input');
    input.id = `loot-field-${fields.size}`; fields.set(path, input); input.name = path;
    input.value = owner[key] ?? ''; input.readOnly = readonly; input.required = required;
    if (multiline) input.rows = 3; else { input.type = 'text'; if (integer || long) input.inputMode = 'numeric'; }
    const caption = element('label', label); caption.htmlFor = input.id; wrapper.append(caption, input);
    function validate() {
        const numeric = integer || long, value = input.value;
        const valid = !numeric || nullable && value === '' || /^-?\d+$/.test(value)
            && BigInt(value) >= (long ? -9223372036854775808n : -2147483648n)
            && BigInt(value) <= (long ? 9223372036854775807n : 2147483647n);
        input.setCustomValidity(valid ? '' : `Enter a whole ${long ? '64-bit' : '32-bit'} integer${nullable ? ', or leave blank for none' : ''}.`); return valid;
    }
    validate(); input.addEventListener('input', () => {
        const valid = validate();
        owner[key] = nullable && input.value === '' ? null : integer && valid ? Number(input.value) : input.value; changed();
    });
    if (hint) wrapper.append(element('small', hint)); parent.append(wrapper); return input;
}
function choice(parent, owner, key, label, optionsKey, path, nullable = false) {
    const wrapper = element('div', null, 'field'), input = element('select'); input.id = `loot-field-${fields.size}`; fields.set(path, input);
    const caption = element('label', label); caption.htmlFor = input.id; wrapper.append(caption, input);
    const choices = [...(state.options[optionsKey] || vocabulary[optionsKey])];
    if (nullable) choices.unshift({ id: '', display_name: 'None' });
    if (owner[key] != null && !choices.some(option => option.id === owner[key])) choices.push({ id: owner[key], display_name: `${owner[key]} (stored value)` });
    for (const option of choices) { const node = element('option', option.display_name); node.value = option.id; input.append(node); }
    input.value = owner[key] ?? '';
    input.addEventListener('change', () => { owner[key] = nullable && input.value === '' ? null : input.value; render(); }); parent.append(wrapper);
}
const vocabulary = Object.fromEntries(Object.entries({
    section_kinds: ['guaranteed', 'pre_roll', 'main', 'tertiary'], roll_kinds: ['guaranteed_all', 'weighted_one', 'independent'],
    outcome_kinds: ['item', 'loot_table', 'no_drop'], pre_roll_failure_behaviors: ['continue', 'fallthrough_to_main', 'stop'],
    pre_roll_success_sequence_behaviors: ['continue', 'stop'], pre_roll_success_main_behaviors: ['keep_main', 'suppress_main']
}).map(([key, values]) => [key, values.map(id => ({ id, display_name: id.replaceAll('_', ' ') }))]));
function toDraft(definition) {
    return { display_name: definition.display_name, description: definition.description, groups: definition.groups.map(group => ({
        roll_group_id: group.roll_group_id, order: group.order, section_kind: group.section_kind, roll_kind: group.roll_kind, roll_count: group.roll_count,
        pre_roll_failure_behavior: group.pre_roll_failure_behavior, pre_roll_success_sequence_behavior: group.pre_roll_success_sequence_behavior,
        pre_roll_success_main_behavior: group.pre_roll_success_main_behavior, display_name: group.display_name,
        outcomes: group.outcomes.map(({ item_display_name, ...outcome }) => clone(outcome))
    })) };
}
function nextOrder(rows) { const used = new Set(rows.map(row => row.order)); let order = 0; while (used.has(order)) order++; return order; }
function nextId(rows, key, prefix) { let n = 1; while (rows.some(row => row[key] === `${prefix}_${n}`)) n++; return `${prefix}_${n}`; }
function newOutcome(group) {
    return { outcome_id: nextId(group.outcomes, 'outcome_id', 'outcome'), order: nextOrder(group.outcomes), outcome_kind: 'item', item_id: null,
        nested_loot_table_id: null, min_quantity: 1, max_quantity: 1, weight: group.roll_kind === 'weighted_one' ? 1 : null,
        probability_numerator: group.roll_kind === 'independent' ? '1' : null, probability_denominator: group.roll_kind === 'independent' ? '1' : null };
}
function addGroup(sectionKind) {
    const group = { roll_group_id: nextId(state.draft.groups, 'roll_group_id', sectionKind), display_name: null,
        order: nextOrder(state.draft.groups.filter(row => row.section_kind === sectionKind)), section_kind: sectionKind,
        roll_kind: sectionKind === 'guaranteed' ? 'guaranteed_all' : sectionKind === 'tertiary' ? 'independent' : 'weighted_one', roll_count: 1,
        pre_roll_failure_behavior: sectionKind === 'pre_roll' ? 'continue' : null,
        pre_roll_success_sequence_behavior: sectionKind === 'pre_roll' ? 'continue' : null,
        pre_roll_success_main_behavior: sectionKind === 'pre_roll' ? 'keep_main' : null, outcomes: [] };
    group.outcomes.push(newOutcome(group)); state.draft.groups.push(group); render();
}
function ordered(rows) { return [...rows].sort((a, b) => Number(a.order) - Number(b.order)); }
function reorderControls(parent, rows, row, remove) {
    const order = ordered(rows), index = order.indexOf(row), actions = element('div', null, 'row-actions');
    function move(offset) { const other = order[index + offset]; [row.order, other.order] = [other.order, row.order]; render(); }
    const up = button('Move up', () => move(-1)); up.disabled = index === 0;
    const down = button('Move down', () => move(1)); down.disabled = index === order.length - 1;
    actions.append(up, down, button('Remove', () => { if (confirm('Remove this card and its contents from the draft?')) { remove(); render(); } })); parent.append(actions);
}
function render() {
    const opened = new Set([...document.querySelectorAll('.section[open]')].map(node => node.dataset.title));
    $('empty').hidden = true; $('editor').hidden = false; $('form-fields').replaceChildren(); fields.clear();
    $('loot-state').textContent = state.definition?.publication_state?.toUpperCase() || 'NEW LOOT TABLE';
    $('loot-meta').textContent = state.definition ? `${state.id} · Updated ${state.version}` : 'Unsaved definition';
    const basics = section('Table details', true, 'Save complete roll groups as a draft, then preview publication. The host owns roll rules and dependency validation.');
    const basicFields = grid(basics);
    field(basicFields, state, 'id', 'Loot table ID', { readonly: !!state.definition, path: 'loot_table_id', required: true, hint: 'Stable lowercase letters, digits and underscore-separated words.' });
    field(basicFields, state.draft, 'display_name', 'Display name', { required: true });
    field(basics, state.draft, 'description', 'Description', { multiline: true });
    const groups = section('Ordered roll groups', true, 'Sections resolve in order: Guaranteed, Pre-roll, Main, Tertiary. Order values are explicit and must be unique within each section. Changing a kind preserves every existing field; clear incompatible values explicitly.');
    const limits = state.options.supported_limits;
    if (limits) groups.append(element('p', `Host limits: nesting depth ${limits.max_nesting_depth}; bounded expansion ${limits.max_bounded_expansion}. Preview checks direct/indirect cycles and published dependencies.`, 'muted'));
    const sectionIds = [...new Set(['guaranteed', 'pre_roll', 'main', 'tertiary', ...state.draft.groups.map(group => group.section_kind)])];
    for (const sectionKind of sectionIds) {
        const peers = state.draft.groups.filter(group => group.section_kind === sectionKind);
        if (!peers.length) continue;
        groups.append(element('h3', sectionKind.replaceAll('_', ' '), 'subheading'));
        for (const group of ordered(peers)) renderGroup(groups, group, peers);
    }
    if (!state.draft.groups.length) groups.append(element('p', 'No roll groups yet. Add a section below.', 'muted'));
    const additions = element('div', null, 'button-row');
    for (const kind of ['guaranteed', 'pre_roll', 'main', 'tertiary']) additions.append(button(`+ ${kind.replaceAll('_', ' ')} group`, () => addGroup(kind)));
    groups.append(additions);
    for (const node of document.querySelectorAll('.section')) if (opened.has(node.dataset.title)) node.open = true;
    changed();
}
function renderGroup(parent, group, peers) {
    const card = element('details', null, 'loot-group'); card.open = expandedCards.get(group) ?? true;
    card.addEventListener('toggle', () => { if (card.isConnected) expandedCards.set(group, card.open); });
    card.append(element('summary', `${group.display_name || group.roll_group_id || 'Unnamed group'} · ${group.roll_kind.replaceAll('_', ' ')} · order ${group.order}`));
    const content = element('div', null, 'loot-card-content'); card.append(content); const form = grid(content), path = `groups.${group.roll_group_id}`;
    field(form, group, 'roll_group_id', 'Group ID', { required: true, path });
    field(form, group, 'display_name', 'Group label (optional)', { nullable: true, path: path + '.display_name' });
    field(form, group, 'order', 'Order within section', { integer: true, path: path + '.order' });
    choice(form, group, 'section_kind', 'Section', 'section_kinds', path + '.section_kind');
    choice(form, group, 'roll_kind', 'Roll behavior', 'roll_kinds', path + '.roll_kind');
    field(form, group, 'roll_count', 'Roll count', { integer: true, path: path + '.roll_count', hint: 'Positive count. Guaranteed sections require Guaranteed All with exactly one roll.' });
    const pre = element('details', null, 'loot-settings'); pre.open = group.section_kind === 'pre_roll' || ['pre_roll_failure_behavior','pre_roll_success_sequence_behavior','pre_roll_success_main_behavior'].some(key => group[key] !== null);
    pre.append(element('summary', 'Pre-roll behavior')); const preFields = grid(pre);
    for (const [key, label, options] of [['pre_roll_failure_behavior','On failure','pre_roll_failure_behaviors'],['pre_roll_success_sequence_behavior','On success: remaining pre-rolls','pre_roll_success_sequence_behaviors'],['pre_roll_success_main_behavior','On success: Main section','pre_roll_success_main_behaviors']]) choice(preFields, group, key, label, options, path + '.' + key, true);
    pre.append(element('p', 'All three are required for Pre-roll. Other sections must use None. Fall through to Main requires a Main group.', 'muted')); content.append(pre);
    group.outcomes = group.outcomes || [];
    for (const outcome of ordered(group.outcomes)) renderOutcome(content, group, outcome);
    content.append(button('+ Add outcome', () => { group.outcomes.push(newOutcome(group)); render(); }));
    reorderControls(content, peers, group, () => state.draft.groups.splice(state.draft.groups.indexOf(group), 1)); parent.append(card);
}
function renderOutcome(parent, group, outcome) {
    const box = element('details', null, 'loot-outcome'); box.open = expandedCards.get(outcome) ?? true;
    box.addEventListener('toggle', () => { if (box.isConnected) expandedCards.set(outcome, box.open); });
    box.append(element('summary', `${outcome.outcome_id || 'Unnamed outcome'} · ${outcome.outcome_kind.replaceAll('_', ' ')} · order ${outcome.order}`));
    const content = element('div', null, 'loot-card-content'), form = grid(content), path = `groups.${group.roll_group_id}.outcomes.${outcome.outcome_id}`; box.append(content);
    field(form, outcome, 'outcome_id', 'Outcome ID', { required: true, path });
    field(form, outcome, 'order', 'Order within group', { integer: true, path: path + '.order' });
    choice(form, outcome, 'outcome_kind', 'Outcome kind', 'outcome_kinds', path + '.outcome_kind');
    // Conditional fields stay editable when populated, so invalid/legacy values never vanish.
    if (outcome.outcome_kind === 'item' || outcome.item_id !== null || outcome.min_quantity !== null || outcome.max_quantity !== null) {
        const item = grid(content);
        field(item, outcome, 'item_id', 'Item ID', { nullable: true, path: path + '.item_id', hint: 'Choose below, or enter a stable reference. Blank means none.' });
        content.append(button('Find item', () => openPicker(outcome, 'item_id')));
        field(item, outcome, 'min_quantity', 'Minimum quantity', { integer: true, nullable: true, path: path + '.min_quantity' });
        field(item, outcome, 'max_quantity', 'Maximum quantity', { integer: true, nullable: true, path: path + '.max_quantity', hint: 'Item quantities must be positive, with minimum ≤ maximum. Blank means none.' });
    }
    if (outcome.outcome_kind === 'loot_table' || outcome.nested_loot_table_id !== null) {
        field(content, outcome, 'nested_loot_table_id', 'Nested loot table ID', { nullable: true, path: path + '.nested_loot_table_id', hint: 'Cycles and nesting/expansion limits are checked in Preview.' });
        content.append(button('Find loot table', () => openPicker(outcome, 'nested_loot_table_id')));
    }
    const chance = grid(content);
    if (group.roll_kind === 'weighted_one' || outcome.weight !== null) field(chance, outcome, 'weight', 'Weight', { integer: true, nullable: true, path: path + '.weight', hint: 'Weighted One requires a positive integer. Other roll behaviors require blank.' });
    if (group.roll_kind === 'independent' || outcome.probability_numerator !== null || outcome.probability_denominator !== null) {
        field(chance, outcome, 'probability_numerator', 'Probability numerator', { long: true, nullable: true, path: path + '.probability_numerator', hint: 'Exact integer. Zero is allowed when another outcome has a positive probability.' });
        field(chance, outcome, 'probability_denominator', 'Probability denominator', { long: true, nullable: true, path: path + '.probability_denominator', hint: 'Must be positive; numerator must be between zero and denominator. Blank differs from zero.' });
    }
    content.append(element('p', outcome.outcome_kind === 'no_drop' ? 'No Drop produces nothing; item/table/quantity fields must be blank. It cannot be used with Guaranteed All.'
        : outcome.outcome_kind === 'loot_table' ? 'Nested tables must not carry item or quantity fields. Publication requires a Published nested table.' : 'Item outcomes must not carry a nested-table reference.', 'muted'));
    content.append(button('Clear incompatible fields', () => {
        if (!confirm('Clear references, quantities and chance fields that do not apply to this outcome and roll kind? Applicable values remain unchanged.')) return;
        if (outcome.outcome_kind !== 'item') { outcome.item_id = null; outcome.min_quantity = null; outcome.max_quantity = null; }
        if (outcome.outcome_kind !== 'loot_table') outcome.nested_loot_table_id = null;
        if (group.roll_kind !== 'weighted_one') outcome.weight = null;
        if (group.roll_kind !== 'independent') { outcome.probability_numerator = null; outcome.probability_denominator = null; }
        render();
    }));
    reorderControls(content, group.outcomes, outcome, () => group.outcomes.splice(group.outcomes.indexOf(outcome), 1)); parent.append(box);
}
function openPicker(outcome, key) {
    if (state.pending || state.uncertain) return; state.reference = { outcome, key };
    $('reference-title').textContent = key === 'item_id' ? 'Choose an item' : 'Choose a nested loot table';
    $('reference-hint').textContent = key === 'item_id' ? 'Reference values are exact. Unpublished items remain selectable; the host decides publication eligibility.'
        : 'Self references are disabled here. Indirect cycles, missing references, publication and expansion limits are validated by the host.';
    $('reference-search').value = ''; renderPicker(); $('reference-dialog').showModal(); $('reference-search').focus();
}
function renderPicker() {
    $('reference-results').replaceChildren(); const { outcome, key } = state.reference, items = key === 'item_id';
    const query = $('reference-search').value.toLowerCase(), idKey = items ? 'item_id' : 'loot_table_id';
    const matches = (state.options[items ? 'item_options' : 'loot_table_options'] || []).filter(row => `${row[idKey]} ${row.display_name}`.toLowerCase().includes(query));
    for (const row of matches) {
        const self = !items && row[idKey] === state.id;
        const entry = button(row.display_name, () => {
            if (state.pending || state.uncertain || !state.draft.groups.some(group => group.outcomes.includes(outcome))) return;
            outcome[key] = row[idKey]; $('reference-dialog').close(); state.reference = null; render();
        }); entry.disabled = self;
        entry.append(element('small', `${row[idKey]} · ${items ? row.runtime_enabled ? 'Published' : 'Not Published' : row.publication_state}${self ? ' · Cannot reference itself' : ''}`));
        if (items) entry.append(element('small', `Reference value: ${row.reference_value}`)); $('reference-results').append(entry);
    }
    if (!matches.length) $('reference-results').append(element('p', 'No matching references. Close and refresh to reload choices, or retain/enter a stable ID in the form.', 'muted'));
}
function exact(value) { return value ? `${value.numerator}/${value.denominator}` : 'Unavailable'; }
function showExpectedValue(report, context) {
    const host = $('expected-value'); host.replaceChildren(); host.hidden = false; host.dataset.stale = 'false'; state.evRevision = state.editRevision;
    host.append(element('h3', `Expected value · ${context}`), element('p', 'This report is stale after edits. Preview again to calculate the current draft.', 'ev-stale'));
    if (!report) { host.append(element('p', 'Expected value unavailable.')); return; }
    host.append(element('p', report.valid ? 'Host calculation is valid for this snapshot.' : 'Calculation is invalid; resolve the diagnostics before interpreting these values.', report.valid ? 'muted' : 'message Error'));
    host.append(element('p', `Total reference value: ${exact(report.total_reference_value)} · No-drop probability: ${exact(report.no_drop_probability)}`));
    host.append(element('p', `Currency injection configured: ${report.currency_injection_configured ? 'yes' : 'no'}. Values are exact fractions; no client-side rounding.`, 'muted'));
    for (const message of report.diagnostics || []) host.append(element('div', `${message.code}: ${message.message}`, `message ${message.severity}`));
    function reportSection(title, rows, describe) {
        const details = element('details'); details.append(element('summary', `${title} (${rows.length})`));
        for (const row of rows) details.append(element('p', describe(row), 'change')); host.append(details);
    }
    reportSection('Item totals', report.item_totals || [], row => `${row.display_name} (${row.item_id}) · Quantity ${exact(row.expected_quantity)} · Probability ${exact(row.effective_probability)} · Expected value ${exact(row.expected_reference_value)} · Reference value ${row.reference_value}${row.zero_reference_value ? ' (zero reference value)' : ''}`);
    reportSection('Section totals', report.section_totals || [], row => `${row.section_kind} · Expected value ${exact(row.expected_reference_value)}`);
    reportSection('Path contributions', report.path_contributions || [], row => `${row.path} · ${row.section_kind} · ${row.display_name || row.item_id || 'No item'} · Quantity ${exact(row.expected_quantity)} · Probability ${exact(row.effective_probability)} · Expected value ${exact(row.expected_reference_value)}`);
}
function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || [...fields.entries()].find(([path]) => message.field && (path.startsWith(message.field + '.') || message.field.startsWith(path + '.')))?.[1];
        if (input) box.append(button('Go to field', () => { for (let node = input.parentElement; node; node = node.parentElement) if (node.tagName === 'DETAILS') node.open = true; input.focus(); input.scrollIntoView({ block: 'center' }); }));
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
    const invalid = $('loot-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { for (let node = invalid.parentElement; node; node = node.parentElement) if (node.tagName === 'DETAILS') node.open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (operation !== 'save_draft' && dirty()) { notice('Save or discard local edits before applying an operation to the saved Table.', true); return; }
    if (!/^[a-z0-9]+(_[a-z0-9]+)*$/.test(state.id)) { notice('Use a stable lowercase Table ID, with optional underscore-separated words.', true); fields.get('loot_table_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { ...clone(state.draft), expected_updated_at_utc: state.version, target_operation: operation };
    state.preview = null; busy(true);
    try {
        const result = await request(`/loot-tables/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
        if (revision !== state.editRevision || id !== state.id) return;
        const applicable = operation === 'save_draft' ? result.valid_for_draft : !result.messages.some(message => message.severity === 'Error');
        state.preview = { valid: applicable, signature: result.preview_signature, revision, id, operation, payload };
        messages(result.messages); showChanges(result.changes); showExpectedValue(result.expected_value, 'Preview'); $('review').hidden = false;
        notice(applicable ? 'Review the changes below, then apply this exact preview.' : 'Resolve the validation messages and preview again.', !applicable);
        $('review').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    finally { busy(false); }
}
async function apply() {
    const reviewed = state.preview;
    if (state.pending || state.uncertain || !reviewed?.valid || reviewed.revision !== state.editRevision || reviewed.id !== state.id) return;
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} Table ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/loot-tables/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: reviewed.operation === 'save_draft'
                ? { display_name: reviewed.payload.display_name, description: reviewed.payload.description, groups: reviewed.payload.groups, expected_updated_at_utc: reviewed.payload.expected_updated_at_utc, preview_signature: reviewed.signature }
                : { expected_updated_at_utc: reviewed.payload.expected_updated_at_utc, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        notice('Database change committed. The existing Loot Table service requests no catalog export. The live game was not restarted.');
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the Table changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/loot-tables/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(toDraft(state.remote), null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The Table is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing Table'; $('keep-local').textContent = 'Keep edits as a new draft';
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
$('loot-workspace').addEventListener('click', () => location.hash = 'list');
$('new-loot').addEventListener('click', newTable);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', changed); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-loot').addEventListener('click', reconcile); $('loot-form').addEventListener('submit', event => event.preventDefault());
$('close-reference').addEventListener('click', () => $('reference-dialog').close()); $('reference-search').addEventListener('input', renderPicker);
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
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the Table catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
