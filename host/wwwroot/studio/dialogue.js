// One owner for Dialogue entries/nodes/choices, diagnostics, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: {}, definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, diagnosticRevision: 0, selectedNode: 0, nodeSearch: '', simulation: null, simulationRevision: -1, graph: { x: 0, y: 0, zoom: 1 } };
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
    updateSimulationActions();
    $('dialogue-title').textContent = state.draft?.display_name || 'Untitled dialogue';
    $('diagnostics').dataset.stale = 'true'; $('diagnostic-status').textContent = 'Analysis is stale. Preview current edits, or refresh the saved definition analysis.'; updateActions();
}
function busy(value) { state.pending = value; updateActions(); if (!value) void loadDiagnostics(); }
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-dialogue').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-dialogue').disabled = state.pending || !state.id;
    $('session-button').disabled = state.pending;
    $('refresh-diagnostics').disabled = state.pending || state.uncertain || !state.definition || dirty();
    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    updateSimulationActions();
    const notes = {
        save_draft: 'Save the complete conversation as Draft. Saving a Published conversation removes publication. No catalog export is requested.',
        publish: 'Publish the saved conversation after graph and reference validation. Save or discard local changes first.',
        disable: 'Disable the saved conversation. Published NPC references can block this. No catalog export is requested.',
        delete: 'Permanently delete a Disabled conversation with no known NPC references. No catalog export is requested.'
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
    try { const options = await request('/dialogue/options'); if (revision === state.optionsRevision) state.options = options; }
    catch (error) { if (revision === state.optionsRevision) notice(`Dialogue options unavailable: ${error.message} Refresh to retry. Local edits remain.`, true); }
}
function toDraft(definition) { return Object.fromEntries(['display_name','schema_version','entry_points','nodes','metadata_description','notes'].map(key => [key,clone(definition[key])])); }
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/dialogue?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('dialogue-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} ${result.items.length === 1 ? 'dialogue' : 'dialogue'}`;
        for (const dialogue of result.items) {
            const card = button('', () => loadDialogue(dialogue.dialogue_definition_id), `item-card${dialogue.dialogue_definition_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', dialogue.display_name), element('small', `${dialogue.dialogue_definition_id} · ${dialogue.publication_state}`));
            card.append(copy); $('dialogue-list').append(card);
        }
        if (!result.items.length) $('dialogue-list').append(element('p', 'No matching dialogue. Try another search or create a new dialogue.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('dialogue-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the Dialogue before replacing an uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved Dialogue changes?');
}
async function loadDialogue(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/dialogue/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    state.definition = definition; state.id = definition.dialogue_definition_id; state.version = definition.updated_at_utc;
    state.draft = toDraft(definition); state.base = toDraft(definition); state.uncertain = false; state.remote = undefined;
    state.selectedNode=0;state.nodeSearch='';state.simulation=null;state.simulationRevision=-1;$('sim-output').replaceChildren();$('sim-status').textContent='Start a structural playthrough of this conversation.';state.graph={x:0,y:0,zoom:1};
    $('reconcile').hidden = true; $('operation').value = 'save_draft'; render(true); fitGraph();
}
function newDialogue() {
    if (!mayReplace()) return;
    state.loadRevision++; state.definition=null;state.id='';state.version=null;state.base=null;state.uncertain=false;state.remote=undefined;
    state.selectedNode=0;state.nodeSearch='';state.simulation=null;state.simulationRevision=-1;$('sim-output').replaceChildren();$('sim-status').textContent='Start a structural playthrough of this conversation.';state.graph={x:0,y:0,zoom:1};
    state.draft={display_name:'',schema_version:1,entry_points:[{entry_id:'default',node_id:'start',priority:0,entry_order:0,conditions:[]}],nodes:[newNode('start')],metadata_description:null,notes:null};
    $('operation').value='save_draft';$('reconcile').hidden=true;render(true);fitGraph();location.hash='detail';
    notice('Choose a stable Dialogue ID. Add entries, nodes and explicit targets, then preview before saving.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { kind='text', nullable=false, min=0, max=2147483647, readonly=false, path=key, hint='', required=false }={}) {
    const wrapper=element('div',null,'field'), input=element(kind==='multiline'?'textarea':'input');
    input.id=`dialogue-field-${fields.size}`;fields.set(path,input);input.name=path;input.value=owner[key]??'';input.readOnly=readonly;input.required=required;
    if(kind==='multiline')input.rows=4;else {input.type='text';if(['integer','long','coordinate'].includes(kind))input.inputMode=kind==='coordinate'?'decimal':'numeric';}
    const caption=element('label',label);caption.htmlFor=input.id;wrapper.append(caption,input);
    const validate=()=>{
        const v=input.value;let valid=true;
        if(v===''&&nullable)valid=true;
        else if(kind==='integer'||kind==='long')valid=/^-?\d+$/.test(v)&&BigInt(v)>=BigInt(min)&&BigInt(v)<=BigInt(max);
        else if(kind==='coordinate')valid=v.trim()!==''&&Number.isFinite(Number(v));
        input.setCustomValidity(valid?'':kind==='coordinate'?'Enter a finite coordinate.':`Enter a whole number from ${min} through ${max}.`);return valid;
    };
    validate();input.addEventListener('input',()=>{
        const valid=validate(),v=input.value;
        owner[key]=v===''&&nullable?null:valid&&['integer','coordinate'].includes(kind)?Number(v):v;
        changed();if(['node_id','text','canvas_x','canvas_y','next_node_id','target_node_id'].includes(key))drawGraph();
    });
    if(hint)wrapper.append(element('small',hint));parent.append(wrapper);return input;
}
function selectField(parent,owner,key,label,values,path=key) {
    const box=element('div',null,'field'),input=element('select');input.id=`dialogue-field-${fields.size}`;fields.set(path,input);
    const caption=element('label',label);caption.htmlFor=input.id;box.append(caption,input);
    for(const value of values){const option=element('option',value.display_name??value);option.value=value.id??value;input.append(option);}
    if(!values.some(value=>(value.id??value)===owner[key])){const option=element('option',`Stored value: ${owner[key]??'(empty)'}`);option.value=owner[key]??'';input.append(option);}
    input.value=owner[key]??'';input.addEventListener('change',()=>{owner[key]=input.value;changed();drawGraph();});parent.append(box);return input;
}
function checkField(parent,owner,key,label,path=key){
    const box=element('label',null,'dialogue-checkbox'),input=element('input');input.type='checkbox';input.checked=owner[key];fields.set(path,input);
    input.addEventListener('change',()=>{owner[key]=input.checked;changed();});box.append(input,document.createTextNode(label));parent.append(box);
}
function options(key,fallback){return state.options[key]||fallback.map(id=>({id,display_name:id.replaceAll('_',' ')}));}
function nextOrder(rows,key){return rows.reduce((max,row)=>Number.isInteger(row[key])?Math.max(max,row[key]):max,-1)+1;}
function listActions(parent,rows,index,key,label){
    const actions=element('div',null,'row-actions');
    const move=to=>{[rows[index],rows[to]]=[rows[to],rows[index]];if(key)rows.forEach((row,i)=>row[key]=i);render(true);};
    const up=button('Move up',()=>move(index-1));up.disabled=index===0;
    const down=button('Move down',()=>move(index+1));down.disabled=index===rows.length-1;
    actions.append(up,down,button('Remove',()=>{if(confirm(`Remove ${label}? References elsewhere remain for explicit repair.`)){rows.splice(index,1);render(true);}}));parent.append(actions);
}
let referenceTarget=null;
function reference(parent,owner,key,label,type,path,nullable=true){
    field(parent,owner,key,label,{path,nullable,required:!nullable,hint:'Choose a reference or enter its stable ID. Missing references remain visible.'});
    parent.append(button(`Choose ${label.toLowerCase()}`,()=>{
        referenceTarget={owner,key,type};$('reference-title').textContent=`Choose ${label.toLowerCase()}`;$('reference-search').value='';renderReferences();$('reference-dialog').showModal();$('reference-search').focus();
    }));
}
function referenceValues(type,owner){
    if(type==='node')return state.draft.nodes.map(node=>({id:node.node_id,display_name:`${node.speaker||node.node_type} · ${node.text||''}`}));
    if(type==='quest')return(state.options.quest_references||[]).map(q=>({id:q.quest_id,display_name:q.display_name}));
    const quest=state.options.quest_references?.find(q=>q.quest_id===owner.quest_id);
    if(type==='step')return quest?.steps||[];
    if(type==='transition')return(quest?.transitions||[]).map(t=>({id:t.transition_id,display_name:`${t.display_name} · ${t.source_status}/${t.source_step_id||'—'} → ${t.target_status}/${t.target_step_id||'—'}`}));
    return state.options[`${type}_references`]||[];
}
function renderReferences(){
    const target=referenceTarget;if(!target)return;
    const query=$('reference-search').value.toLowerCase(),values=referenceValues(target.type,target.owner);
    $('reference-hint').textContent=target.type==='node'?'Targets use the current draft node IDs. Renaming a node never silently rewrites connections.':'Published/runtime references only. If the list is empty, the reference may be unavailable or unpublished. Existing values are preserved. Choose a quest before choosing its step or transition.';
    $('reference-results').replaceChildren();
    for(const row of values.filter(row=>`${row.id} ${row.display_name}`.toLowerCase().includes(query))){
        const pick=button('',()=>{target.owner[target.key]=row.id;$('reference-dialog').close();render(true);});pick.append(element('strong',row.id),element('small',row.display_name));$('reference-results').append(pick);
    }
    if(!$('reference-results').children.length)$('reference-results').append(element('p','No matching references. You can still type the ID in the form.'));
}
function conditionRows(parent,rows,path){
    parent.append(element('h4','Conditions'),element('p','Order is retained. quest_status uses quest + status; quest_step uses quest + step; has_item uses item + quantity. Clear unused fields explicitly.','muted'));
    rows.forEach((row,index)=>{
        const box=element('div',null,'repeat-row'),g=grid(box),prefix=`${path}.${index}`;
        selectField(g,row,'condition_type','Condition type',options('condition_types',['quest_status','quest_step','has_item']),`${prefix}.condition_type`);
        reference(g,row,'quest_id','Quest','quest',`${prefix}.quest_id`);
        field(g,row,'status','Quest status',{nullable:true,path:`${prefix}.status`,hint:'not_started, active or completed; blank when unused.'});
        reference(g,row,'step_id','Quest step','step',`${prefix}.step_id`);
        reference(g,row,'item_id','Item','item',`${prefix}.item_id`);
        field(g,row,'quantity','Item quantity',{kind:'integer',nullable:true,min:1,path:`${prefix}.quantity`});
        listActions(box,rows,index,null,`condition ${index+1}`);parent.append(box);
    });
    parent.append(button('+ Add condition',()=>{rows.push({condition_type:'quest_status',quest_id:null,status:null,step_id:null,item_id:null,quantity:null});render(true);}));
}
function effectRows(parent,choice,path){
    parent.append(element('h4','Effects'),element('p','Quest effects use quest + transition; item effects use item + quantity; experience uses skill + XP. Clear other fields explicitly. Move up/down renumbers effect order.','muted'));
    const rows=choice.effects||[];
    rows.forEach((row,index)=>{
        const box=element('div',null,'repeat-row'),g=grid(box),prefix=`${path}.${index}`;
        field(g,row,'effect_id','Effect ID',{required:true,path:`${prefix}.effect_id`});field(g,row,'effect_order','Effect order',{kind:'integer',max:10000,path:`${prefix}.effect_order`});
        selectField(g,row,'effect_type','Effect type',options('effect_types',['start_quest','advance_quest','complete_quest','grant_item','remove_item','grant_experience']),`${prefix}.effect_type`);
        reference(g,row,'quest_id','Quest','quest',`${prefix}.quest_id`);reference(g,row,'transition_id','Quest transition','transition',`${prefix}.transition_id`);
        reference(g,row,'item_id','Item','item',`${prefix}.item_id`);field(g,row,'quantity','Item quantity',{kind:'integer',nullable:true,min:1,path:`${prefix}.quantity`});
        reference(g,row,'skill_id','Skill','skill',`${prefix}.skill_id`);field(g,row,'xp_amount','XP amount',{kind:'long',nullable:true,min:1,max:'9223372036854775807',path:`${prefix}.xp_amount`,hint:'Exact whole XP; never rounded through a browser number.'});
        listActions(box,rows,index,'effect_order',`effect ${index+1}`);parent.append(box);
    });
    parent.append(button('+ Add effect',()=>{choice.effects??=[];choice.effects.push({effect_id:'',effect_order:nextOrder(choice.effects,'effect_order'),effect_type:'grant_item',quest_id:null,transition_id:null,item_id:null,quantity:null,skill_id:null,xp_amount:null});render(true);}));
}
function newNode(id=''){return{node_id:id,node_type:'speaker_text',speaker:null,text:null,next_node_id:null,dismissible:true,canvas_x:0,canvas_y:0,editor_notes:null,choices:[]};}
function render(markChanged=false){
    const open=new Set([...document.querySelectorAll('.section[open]')].map(node=>node.dataset.title));
    $('empty').hidden=true;$('editor').hidden=false;$('form-fields').replaceChildren();fields.clear();
    $('dialogue-state').textContent=state.definition?.publication_state?.toUpperCase()||'NEW DIALOGUE';
    $('dialogue-meta').textContent=state.definition?`${state.id} · Updated ${state.version}`:'Unsaved conversation';
    const d=state.draft,g=grid(section('Dialogue details',true,'Stable identity and metadata. All authored sections travel together when saved.'));
    field(g,state,'id','Dialogue ID',{required:true,readonly:!!state.definition,path:'dialogue_definition_id'});
    field(g,d,'display_name','Display name',{required:true});field(g,d,'schema_version','Schema version',{kind:'integer',min:1});
    field(g,d,'metadata_description','Description',{kind:'multiline',nullable:true});field(g,d,'notes','Author notes',{kind:'multiline',nullable:true});
    const entries=section('Entry points',false,'Higher priority is considered first. Entry order breaks ties. Conditions remain in their authored order. Move up/down renumbers entry order only.');
    d.entry_points.forEach((row,index)=>{
        const box=element('div',null,'repeat-row'),g=grid(box),path=`entry_points.${index}`;box.prepend(element('h4',`Entry ${index+1}`));
        field(g,row,'entry_id','Entry ID',{required:true,path:`${path}.entry_id`});reference(g,row,'node_id','Entry target','node',`${path}.node_id`,false);
        field(g,row,'priority','Priority',{kind:'integer',min:-2147483648,path:`${path}.priority`});field(g,row,'entry_order','Entry order',{kind:'integer',max:10000,path:`${path}.entry_order`});
        conditionRows(box,row.conditions,`${path}.conditions`);listActions(box,d.entry_points,index,'entry_order',`entry ${index+1}`);entries.append(box);
    });
    entries.append(button('+ Add entry',()=>{d.entry_points.push({entry_id:'',node_id:'',priority:0,entry_order:nextOrder(d.entry_points,'entry_order'),conditions:[]});render(true);}));
    const graph=section('Graph overview',true,'Tap a node to inspect it. Pan and zoom change only this view. Edit coordinates below to change saved layout. Connections use explicit target selectors.');
    renderGraph(graph);
    const nodes=section('Nodes & choices',true,'Search and select a node, then edit its complete inspector. Rename/removal retains all references for explicit repair.');
    const searchBox=element('div',null,'field'),searchLabel=element('label','Search nodes'),searchInput=element('input');searchInput.id='node-search';searchLabel.htmlFor=searchInput.id;searchInput.type='search';searchInput.value=state.nodeSearch;
    searchBox.append(searchLabel,searchInput);nodes.append(searchBox);const layout=element('div',null,'dialogue-node-layout'),list=element('div',null,'dialogue-node-list');list.id='node-list';layout.append(list);nodes.append(layout);
    searchInput.addEventListener('input',()=>{state.nodeSearch=searchInput.value;renderNodeList();});
    const add=button('+ Add node',()=>{d.nodes.push(newNode());state.selectedNode=d.nodes.length-1;state.nodeSearch='';render(true);});nodes.append(add);
    const inspector=element('div',null,'dialogue-inspector');layout.append(inspector);
    state.selectedNode=Math.min(Math.max(0,state.selectedNode),Math.max(0,d.nodes.length-1));const node=d.nodes[state.selectedNode];
    if(node)renderInspector(inspector,node,state.selectedNode);else inspector.append(element('p','Add a node to begin.'));
    renderNodeList();
    for(const node of document.querySelectorAll('.section'))if(open.has(node.dataset.title))node.open=true;
    $('diagnostics').hidden=false;$('simulation').hidden=false;refreshSimulationEntries();
    if(!state.definition){$('graph-analysis').replaceChildren();$('diagnostic-messages').replaceChildren();}
    if(markChanged)changed();else updateActions();drawGraph();
}
function renderNodeList(){
    const list=$('node-list');if(!list)return;list.replaceChildren();const query=state.nodeSearch.toLowerCase();
    state.draft.nodes.forEach((node,index)=>{
        if(!`${node.node_id} ${node.speaker||''} ${node.text||''}`.toLowerCase().includes(query))return;
        const pick=button('',()=>selectNode(index),`dialogue-node-pick${index===state.selectedNode?' selected':''}`);
        pick.append(element('strong',node.node_id||'(new node)'),element('small',`${node.node_type} · ${node.text||'No text'}`));list.append(pick);
    });
    if(!list.children.length)list.append(element('p','No matching nodes.'));
}
function selectNode(index){state.selectedNode=index;render();document.querySelector('.dialogue-inspector')?.scrollIntoView({block:'nearest'});}
function renderInspector(parent,node,index){
    parent.append(element('h3',`Node ${index+1} · ${node.node_id||'new'}`));const g=grid(parent),path=`nodes.${index}`;
    field(g,node,'node_id','Node ID',{required:true,path:`${path}.node_id`});
    selectField(g,node,'node_type','Node type',options('node_types',['speaker_text','player_choice','end']),`${path}.node_type`);
    field(g,node,'speaker','Speaker',{nullable:true,path:`${path}.speaker`});field(g,node,'text','Dialogue text',{kind:'multiline',nullable:true,path:`${path}.text`});
    reference(g,node,'next_node_id','Next node','node',`${path}.next_node_id`);checkField(g,node,'dismissible','Player may dismiss',`${path}.dismissible`);
    field(g,node,'canvas_x','Canvas X',{kind:'coordinate',path:`${path}.canvas_x`});field(g,node,'canvas_y','Canvas Y',{kind:'coordinate',path:`${path}.canvas_y`});
    const nudge=element('div',null,'row-actions');for(const [label,x,y]of[['← 40',-40,0],['→ 40',40,0],['↑ 40',0,-40],['↓ 40',0,40]])nudge.append(button(label,()=>{if(typeof node.canvas_x!=='number'||typeof node.canvas_y!=='number')return;node.canvas_x+=x;node.canvas_y+=y;render(true);}));parent.append(nudge);
    field(parent,node,'editor_notes','Node author notes',{kind:'multiline',nullable:true,path:`${path}.editor_notes`});
    parent.append(element('p','Speaker text uses Next node and no choices. Player choice uses choices and an empty Next node. End requires no targets or choices and Dismissible enabled. Changing type never clears these fields automatically.','muted'));
    node.choices.forEach((choice,i)=>{
        const box=element('details',null,'dialogue-choice');box.open=true;box.append(element('summary',`Choice ${i+1} · ${choice.choice_id||'new'}`));const body=element('div',null,'section-content'),g=grid(body),prefix=`${path}.choices.${i}`;box.append(body);
        field(g,choice,'choice_id','Choice ID',{required:true,path:`${prefix}.choice_id`});field(g,choice,'text','Choice text',{required:true,kind:'multiline',path:`${prefix}.text`});
        field(g,choice,'choice_order','Choice order',{kind:'integer',max:10000,path:`${prefix}.choice_order`});reference(g,choice,'target_node_id','Choice target','node',`${prefix}.target_node_id`,false);
        conditionRows(body,choice.conditions,`${prefix}.conditions`);effectRows(body,choice,`${prefix}.effects`);listActions(body,node.choices,i,'choice_order',`choice ${i+1}`);parent.append(box);
    });
    parent.append(element('p','Move up/down renumbers choice order; typed order values are preserved.','muted'),button('+ Add choice',()=>{node.choices.push({choice_id:'',text:'',target_node_id:'',choice_order:nextOrder(node.choices,'choice_order'),conditions:[],effects:null});render(true);}),button('Remove this node',()=>{if(confirm('Remove this node? Incoming targets remain for explicit repair.')){state.draft.nodes.splice(index,1);render(true);}}));
}
// View coordinates are independent of authored canvas positions. No graph drag writes data.
let graphCanvas=null,graphHits=[];
function renderGraph(parent){
    const actions=element('div',null,'row-actions');
    for(const [label,dx,dy]of[['Pan left',80,0],['Pan right',-80,0],['Pan up',0,80],['Pan down',0,-80]])actions.append(button(label,()=>{state.graph.x+=dx;state.graph.y+=dy;drawGraph();}));
    actions.append(button('Zoom in',()=>zoomGraph(1.3)),button('Zoom out',()=>zoomGraph(1/1.3)),button('Fit graph',fitGraph));parent.append(actions);
    graphCanvas=element('canvas');graphCanvas.width=960;graphCanvas.height=560;graphCanvas.className='dialogue-graph';graphCanvas.setAttribute('aria-label','Conversation graph. The searchable node list and target selectors below provide keyboard editing.');parent.append(graphCanvas);
    let drag=null;
    const point=event=>{const bounds=graphCanvas.getBoundingClientRect();return{x:(event.clientX-bounds.left)*960/bounds.width,y:(event.clientY-bounds.top)*560/bounds.height};};
    graphCanvas.addEventListener('pointerdown',event=>{const p=point(event);drag={...p,last:p,moved:false};graphCanvas.setPointerCapture(event.pointerId);});
    graphCanvas.addEventListener('pointermove',event=>{if(!drag)return;const p=point(event);if(Math.hypot(p.x-drag.x,p.y-drag.y)>6)drag.moved=true;if(drag.moved){state.graph.x+=p.x-drag.last.x;state.graph.y+=p.y-drag.last.y;drawGraph();}drag.last=p;});
    graphCanvas.addEventListener('pointerup',event=>{if(!drag)return;const p=point(event),wasDrag=drag.moved;drag=null;if(!wasDrag){const hit=graphHits.find(h=>p.x>=h.x&&p.x<=h.x+h.w&&p.y>=h.y&&p.y<=h.y+h.h);if(hit)selectNode(hit.index);}});
    graphCanvas.addEventListener('pointercancel',()=>drag=null);
}
function zoomGraph(factor){const old=state.graph.zoom,next=Math.min(4,Math.max(.000001,old*factor));state.graph.x=480-(480-state.graph.x)*next/old;state.graph.y=280-(280-state.graph.y)*next/old;state.graph.zoom=next;drawGraph();}
function fitGraph(){
    const nodes=state.draft?.nodes.filter(n=>Number.isFinite(n.canvas_x)&&Number.isFinite(n.canvas_y))||[];if(!nodes.length)return;
    const minX=Math.min(...nodes.map(n=>n.canvas_x)),minY=Math.min(...nodes.map(n=>n.canvas_y)),maxX=Math.max(...nodes.map(n=>n.canvas_x)),maxY=Math.max(...nodes.map(n=>n.canvas_y));
    const zoom=Math.min(1.5,860/(maxX-minX+210),460/(maxY-minY+90));
    if(!Number.isFinite(zoom)||zoom<=0){notice('Layout coordinates exceed the graph view range. Exact coordinates remain editable in the node inspector.',true);return;}
    state.graph={zoom,x:50-minX*zoom,y:50-minY*zoom};drawGraph();
}
function drawGraph(){
    if(!graphCanvas||!state.draft)return;const ctx=graphCanvas.getContext('2d'),view=state.graph;ctx.clearRect(0,0,960,560);ctx.fillStyle='#101c29';ctx.fillRect(0,0,960,560);graphHits=[];
    const projected=state.draft.nodes.map((node,index)=>({node,index,x:node.canvas_x*view.zoom+view.x,y:node.canvas_y*view.zoom+view.y,w:190*view.zoom,h:76*view.zoom}));
    const valid=p=>Number.isFinite(p.x)&&Number.isFinite(p.y)&&Math.abs(p.x)<1e7&&Math.abs(p.y)<1e7;
    for(const p of projected){if(!valid(p))continue;const targets=[...(p.node.next_node_id?[p.node.next_node_id]:[]),...p.node.choices.map(c=>c.target_node_id)];
        for(const id of targets){const target=projected.find(other=>other.node.node_id===id);if(!target||!valid(target))continue;const ax=p.x+p.w,ay=p.y+p.h/2,bx=target.x,by=target.y+target.h/2,angle=Math.atan2(by-ay,bx-ax);ctx.strokeStyle='#7aa8b9';ctx.lineWidth=1.5;ctx.beginPath();ctx.moveTo(ax,ay);ctx.lineTo(bx,by);ctx.stroke();ctx.beginPath();ctx.moveTo(bx,by);ctx.lineTo(bx-10*Math.cos(angle-.4),by-10*Math.sin(angle-.4));ctx.moveTo(bx,by);ctx.lineTo(bx-10*Math.cos(angle+.4),by-10*Math.sin(angle+.4));ctx.stroke();}}
    for(const p of projected){if(!valid(p))continue;ctx.fillStyle=p.index===state.selectedNode?'#286b68':'#24394d';ctx.fillRect(p.x,p.y,p.w,p.h);ctx.strokeStyle='#99b6c5';ctx.strokeRect(p.x,p.y,p.w,p.h);graphHits.push(p);
        if(view.zoom>.25){ctx.save();ctx.beginPath();ctx.rect(p.x,p.y,p.w,p.h);ctx.clip();ctx.fillStyle='#ffffff';ctx.font=`${Math.max(10,14*view.zoom)}px sans-serif`;ctx.fillText(p.node.node_id||'(new)',p.x+8,p.y+22*view.zoom);ctx.fillStyle='#c8dce7';ctx.font=`${Math.max(9,11*view.zoom)}px sans-serif`;ctx.fillText(p.node.node_type,p.x+8,p.y+43*view.zoom);const entries=state.draft.entry_points.filter(e=>e.node_id===p.node.node_id).map(e=>e.entry_id);if(entries.length)ctx.fillText(`Entry: ${entries.join(', ')}`,p.x+8,p.y+64*view.zoom);ctx.restore();}}
}
function graphAnalysis(preview,label){
    $('graph-analysis').replaceChildren();$('diagnostic-messages').replaceChildren();$('diagnostics').hidden=false;$('diagnostics').dataset.stale='false';$('diagnostic-status').textContent=label;
    for(const [key,values]of Object.entries(preview.analysis||{})){const box=element('div',null,'quest-analysis-row');box.append(element('strong',key.replaceAll('_',' ')),element('p',values.length?values.join(', '):'None'));$('graph-analysis').append(box);}
    const refs=preview.reference_summary;if(refs)$('graph-analysis').append(element('p',`${refs.known_reference_count} known references · check ${refs.reference_check_complete?'complete':'incomplete'}. ${(refs.reference_sources||[]).join(', ')}`));
    for(const message of preview.messages||[])$('diagnostic-messages').append(element('div',`${message.message}${message.remediation?' '+message.remediation:''}`,`message ${message.severity}`));
}
// Every node is checked, including inspectors which are not currently mounted.
function validDraftNumbers(){
    let problem=null;
    function visit(value,path){if(problem||value===null||typeof value!=='object')return;for(const [key,v]of Object.entries(value)){
        const p=`${path}.${key}`;
        if(['schema_version','priority','entry_order','choice_order','effect_order','quantity'].includes(key)&&v!==null&&!Number.isInteger(v))problem=p;
        if(['canvas_x','canvas_y'].includes(key)&&!Number.isFinite(v))problem=p;
        if(key==='xp_amount'&&v!==null&&(!/^\d+$/.test(String(v))||BigInt(v)<1n||BigInt(v)>9223372036854775807n))problem=p;
        visit(v,p);
    }}
    visit(state.draft,'dialogue');
    if(problem){notice(`Correct the numeric field ${problem} before continuing.`,true);const match=problem.match(/nodes\.(\d+)/);if(match)selectNode(Number(match[1]));return false;}return true;
}
function refreshSimulationEntries(){
    const select=$('sim-entry'),previous=select.value;select.replaceChildren();const first=element('option','Default · highest priority');first.value='';select.append(first);
    for(const entry of state.draft.entry_points){const option=element('option',`${entry.entry_id} → ${entry.node_id} · priority ${entry.priority}`);option.value=entry.entry_id;select.append(option);}select.value=[...select.options].some(o=>o.value===previous)?previous:'';
}
function updateSimulationActions(){
    if(!$('sim-restart'))return;
    const stale=state.simulationRevision!==state.editRevision,locked=state.pending||state.uncertain||!state.draft;
    $('sim-restart').disabled=locked||(!state.session.can_edit&&dirty());$('sim-entry').disabled=locked;$('sim-limit').disabled=locked;
    for(const action of $('sim-output').querySelectorAll('button'))action.disabled=locked||stale;
    if(state.simulation&&stale)$('sim-status').textContent='The draft or operation changed. Restart to simulate the current definition.';
}
async function simulate(restart=false,choice=null,acknowledge=false){
    if(state.pending||state.uncertain||!state.draft||!validForm())return;
    if(!restart&&state.simulationRevision!==state.editRevision)return;
    const limit=Number($('sim-limit').value);if(!Number.isInteger(limit)||limit<1||limit>128){notice('Simulation step limit must be from 1 through 128.',true);return;}
    if(!state.session.can_edit&&dirty()){notice('Sign in to simulate an unsaved draft.',true);return;}
    const revision=state.editRevision,id=state.id,version=state.version;
    const body={draft:null,entry_id:$('sim-entry').value||null,current_node_id:restart?null:state.simulation?.current_node?.node_id??null,selected_choice_id:choice,acknowledge_end:acknowledge,restart,visited_node_ids:restart?[]:state.simulation?.visited_node_ids||[],maximum_step_count:limit};
    busy(true);
    try{
        let result;
        if(dirty()){
            if(!id){notice('Enter a stable Dialogue ID before simulating.',true);return;}
            body.draft={...clone(state.draft),expected_updated_at_utc:version,preview_signature:null};
            result=await request(`/dialogue/${encodeURIComponent(id)}/playthrough`,{method:'POST',body});
        }else{
            const response=await request(`/dialogue/${encodeURIComponent(id)}/playthrough`,{headers:{'X-Studio-Playthrough':JSON.stringify(body)}});
            if(response.definition_version!==version){notice('Saved conversation changed. Reload / compare before simulating again.',true);return;}result=response.simulation;
        }
        if(revision!==state.editRevision||id!==state.id)return;
        state.simulation=result;state.simulationRevision=revision;showSimulation();
    }catch(error){notice(`Simulation unavailable: ${error.message} Your draft is unchanged.`,true);}
    finally{busy(false);}
}
function showSimulation(){
    const result=state.simulation,out=$('sim-output');out.replaceChildren();$('sim-status').textContent=`${dirty()?'Unsaved draft':'Saved definition'} · structural simulation only`;
    if(!result.current_node){out.append(element('p',result.is_end?'Conversation ended. Restart to try another path.':'No current node could be resolved. Check entries and targets, then restart.'));for(const warning of result.warnings)out.append(element('p',warning.message,`message ${warning.severity}`));return;}
    out.append(element('h4',`${result.current_node.node_id} · ${result.speaker||'No speaker'}`),element('p',result.text||'(No dialogue text)','dialogue-spoken'));
    const blocked=result.warnings.some(w=>w.code==='dialogue_playthrough_invalid_state');
    for(const choice of result.visible_choices){const box=element('div',null,'repeat-row');box.append(element('strong',`${choice.choice_id} → ${choice.target_node_id}`));const pick=button(choice.text||'Choose',()=>simulate(false,choice.choice_id));if(!blocked)box.append(pick);box.append(element('pre',JSON.stringify({conditions:choice.conditions,effects:choice.effects},null,2),'readonly'));out.append(box);}
    if(!blocked&&result.can_continue&&!result.is_end)out.append(button('Continue',()=>simulate()));
    if(!blocked&&result.is_end)out.append(button('Acknowledge end',()=>simulate(false,null,true)));
    out.append(element('h4','Effects this step would apply'),element('pre',JSON.stringify(result.would_apply_effects,null,2),'readonly'),element('p',`Visited: ${result.visited_node_ids.join(' → ')}`));
    for(const warning of result.warnings)out.append(element('p',warning.message,`message ${warning.severity}`));
}
$('sim-restart').addEventListener('click',()=>simulate(true));
$('sim-entry').addEventListener('change',()=>{state.simulationRevision=-1;updateSimulationActions();});
$('sim-limit').addEventListener('input',()=>{state.simulationRevision=-1;updateSimulationActions();});
$('close-reference').addEventListener('click',()=>$('reference-dialog').close());$('reference-search').addEventListener('input',renderReferences);

async function loadDiagnostics() {
    if(!state.definition || dirty() || state.pending || state.uncertain)return;
    const revision=++state.diagnosticRevision,edit=state.editRevision,id=state.id,version=state.version,operation=$('operation').value;
    $('diagnostic-status').textContent='Checking saved graph and references…';
    try{
        const result=await request(`/dialogue/${encodeURIComponent(id)}/diagnostics?operation=${encodeURIComponent(operation)}`);
        if(revision!==state.diagnosticRevision || edit!==state.editRevision || id!==state.id || version!==state.version)return;
        if(result.definition_version!==version){$('diagnostic-status').textContent='The server definition changed. Reload / compare before continuing.';return;}
        graphAnalysis(result.preview,`Saved definition · ${operation.replaceAll('_',' ')} diagnostics. Preview current changes before applying.`);
    }catch(error){if(revision===state.diagnosticRevision && edit===state.editRevision)$('diagnostic-status').textContent=`Analysis unavailable: ${error.message} Refresh saved analysis to retry.`;}
}
function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || null;
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
    for (const key of ['display_name','schema_version','entry_points','nodes','metadata_description','notes']) {
        if (JSON.stringify(before[key]) === JSON.stringify(normalized[key])) continue;
        const box = element('details', null, 'change'); box.open = true;
        box.append(element('summary', `Complete ${key.replaceAll('_',' ')} change`), element('pre', `Before\n${JSON.stringify(before[key] ?? null,null,2)}\n\nAfter normalization\n${JSON.stringify(normalized[key],null,2)}`)); $('review-changes').append(box);
    }
    if (JSON.stringify(state.draft) !== JSON.stringify(normalized)) $('review-changes').prepend(element('p','The host normalizes ID casing, whitespace and row order. The complete normalized changes below are what this signature authorizes.','message Warning'));
}
function validForm() {
    if (!validDraftNumbers()) return false;
    const invalid = $('dialogue-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (operation !== 'save_draft' && dirty()) { notice('Save or discard local edits before applying an operation to the saved Dialogue.', true); return; }
    if (!/^[a-z][a-z0-9]*(_[a-z0-9]+)*$/.test(state.id)) { notice('Use a stable lowercase Dialogue ID, with optional underscore-separated words.', true); fields.get('dialogue_definition_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { ...clone(state.draft), expected_updated_at_utc: state.version, target_operation: operation };
    state.preview = null; busy(true);
    try {
        const response = await request(`/dialogue/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
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
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} Dialogue ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/dialogue/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: reviewed.operation === 'save_draft'
                ? { display_name: reviewed.payload.display_name, schema_version: reviewed.payload.schema_version, entry_points: reviewed.payload.entry_points, nodes: reviewed.payload.nodes, metadata_description: reviewed.payload.metadata_description, notes: reviewed.payload.notes, expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
                : { expected_updated_at_utc: state.version, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'Dialogue catalog export succeeded.', failed: 'Dialogue catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'Dialogue catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the Dialogue changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/dialogue/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(toDraft(state.remote), null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The Dialogue is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing Dialogue'; $('keep-local').textContent = 'Keep edits as a new draft';
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
    $('operation').value = 'save_draft'; render(true);
    notice('Local edits retained against the current version. Preview again before applying.');
});
$('dialogue-workspace').addEventListener('click', () => location.hash = 'list');
$('new-dialogue').addEventListener('click', newDialogue);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', () => { changed(); void loadDiagnostics(); }); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-dialogue').addEventListener('click', reconcile); $('dialogue-form').addEventListener('submit', event => event.preventDefault());
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
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the Dialogue catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
