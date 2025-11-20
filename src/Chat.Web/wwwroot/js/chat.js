// Duplicate include guard (wrap to avoid top-level return issues in bundlers)
if(window.__chatAppBooted){
  console.warn('chat.js: duplicate include detected, skipping second init');
} else {
  window.__chatAppBooted = true;
}
/**
 * chat.js - Framework-free chat frontend.
 * Responsibilities:
 *  - Bootstraps auth probe and establishes SignalR hub connection with exponential backoff
 *  - Maintains in-memory state (rooms, users, messages, profile) and DOM rendering
 *  - Optimistic message sending (temporary negative IDs + correlationId reconciliation)
 *  - Client-side pagination of historical messages (infinite scroll up)
 *  - Emits reconnect telemetry to /api/telemetry/reconnect including failure classification
 *  - Minimal DOM querying & mutation; no external framework dependency
 *
 * Reconnect Telemetry Fields (sent):
 *  - attempt: sequential attempt number
 *  - delayMs: backoff delay chosen for next attempt
 *  - errorCategory: classified category of the previous failure (auth|timeout|transport|server|other|unknown)
 *  - errorMessage: truncated error message for diagnostics
 *
 * Message Reconciliation Strategy:
 * 1. When sending, assign a temporary negative id and a correlationId.
 * 2. On hub echo, first match by correlationId; if absent, fallback to matching pending negative id by content.
 * 3. Replace pending entry to preserve ordering & local timestamp until server timestamp merges.
 */
 (function(){
  const log = (lvl,msg,obj)=> (window.appLogger && window.appLogger[lvl]) ? window.appLogger[lvl](msg,obj) : console.log(`[${lvl}] ${msg}`, obj||'');

  // ---------------- State ----------------
  const AuthStatus = { UNKNOWN:'UNKNOWN', PROBING:'PROBING', AUTHENTICATED:'AUTHENTICATED', UNAUTHENTICATED:'UNAUTHENTICATED' };
  const state = { loading:true, profile:null, rooms:[], users:[], messages:[], joinedRoom:null, filter:'', oldestLoaded:null, canLoadMore:true, pageSize:20, loadingMore:false, lastSendAt:0, minSendIntervalMs:800, joinInProgress:false, pendingJoin:null, outbox:[], pendingAck:{}, authStatus: AuthStatus.UNKNOWN, pendingMessages:{}, isOffline:false, unreadCount:0, unsentByRoom:{}, ackTimers:{}, autoScroll:true, _firstRender:true, _autoFillPass:0 };
  // Extended auth / hub timing metadata
  state._hubStartedEarly = false;       // whether hub started before auth probe resolved
  state._graceStartedAt = null;         // timestamp when grace window started
  state._graceEnded = false;            // flag to ensure single grace end telemetry
  const els = {};

  // ---------------- Connection State Tracking ----------------
  // Explicit connection state tracking to handle all reconnection scenarios:
  // - Scenario 1: App down, dependencies up (manual reconnect)
  // - Scenario 2: Dependencies down, app up (automatic reconnect)
  // - Scenario 3: Both down (hybrid reconnect)
  // - Scenario 4: Network interruption (both types)
  let _connectionState = {
    current: 'disconnected',        // 'connected' | 'reconnecting' | 'disconnected'
    lastUpdate: 0,                  // timestamp of last state change
    isReconnecting: false,          // explicit reconnection flag
    reconnectSource: null           // 'automatic' | 'manual' | null
  };

  // Grace window helper (auth probe or transitional period)
  function isWithinAuthGrace(){
    return !!(state.authGraceUntil && Date.now() < state.authGraceUntil);
  }

  function cacheDom(){
    els.app = document.querySelector('.app');
    // Select the actual loading spinner container, not the outer layout wrapper.
    // There are two elements with class 'vh-100': the layout wrapper and the loader div.
    // Prefer the one that contains a spinner-border child.
    const vhCandidates = Array.from(document.querySelectorAll('.vh-100'));
    els.loaderWrapper = vhCandidates.find(el => el.querySelector('.spinner-border')) || document.querySelector('.d-flex.vh-100') || vhCandidates[0];
    if(els.loaderWrapper === vhCandidates[0] && vhCandidates.length > 1){
      // Safety: if we accidentally picked outer wrapper, but a second candidate exists, swap.
      const loaderAlt = vhCandidates.find(el => el !== vhCandidates[0]);
      if(loaderAlt) els.loaderWrapper = loaderAlt;
    }
    els.roomsList = document.getElementById('rooms-list');
    els.usersList = document.getElementById('users-list');
    els.messagesList = document.getElementById('messages-list');
    els.messageInput = document.getElementById('message-input');
    els.joinedRoomTitle = document.getElementById('joinedRoom');
    els.filterInput = document.querySelector('.users-container input[type=text]');
    els.errorAlert = document.getElementById('errorAlert');
    els.itemToDelete = document.getElementById('itemToDelete');
  els.profileAvatarImg = document.getElementById('profileAvatarImg') || document.querySelector('.profile img.avatar');
  els.profileAvatarInitial = document.getElementById('profileAvatarInitial') || document.querySelector('.profile span.avatar');
  els.profileName = document.getElementById('profileName') || document.querySelector('.profile span:not(.avatar)');
    els.btnLogin = document.getElementById('btn-login');
    els.btnLogout = document.getElementById('btn-logout');
  els.roomsNoSelection = document.querySelector('[data-role="no-room-selected"]');
  els.roomsPanel = document.querySelector('[data-role="room-panel"]');
  els.usersHeader = document.getElementById('users-header');
  els.queueBadge = document.getElementById('queue-badge');
  els.roomHeader = document.querySelector('.main-content[data-role="room-panel"] .header');
  }

  function setLoading(v){
    state.loading=v;
    if(els.loaderWrapper) els.loaderWrapper.classList.toggle('d-none', !v);
    if(els.app) els.app.classList.toggle('d-none', v);
  }

  // --------------- Connection Visual (header color) ---------------
  function applyConnectionVisual(stateName){
    if(!els.roomHeader) return;
    els.roomHeader.classList.remove('connection-state-connected','connection-state-reconnecting','connection-state-disconnected');
    // Preserve original room name so we can append/remove status annotation without losing it.
    if(!state._baseRoomTitle && els.joinedRoomTitle){
      state._baseRoomTitle = els.joinedRoomTitle.textContent || '';
    }
    switch(stateName){
      case 'connected':
        els.roomHeader.classList.add('connection-state-connected');
        if(els.joinedRoomTitle && state._baseRoomTitle){
          els.joinedRoomTitle.textContent = state._baseRoomTitle;
        }
        break;
      case 'reconnecting':
        els.roomHeader.classList.add('connection-state-reconnecting');
        if(els.joinedRoomTitle && state._baseRoomTitle){
          els.joinedRoomTitle.textContent = state._baseRoomTitle + ' (' + (window.i18n.Reconnecting || 'RECONNECTING…') + ')';
        }
        break;
      case 'disconnected':
        els.roomHeader.classList.add('connection-state-disconnected');
        if(els.joinedRoomTitle){
          const base = state._baseRoomTitle || els.joinedRoomTitle.textContent || '';
          // Use explicit variation selector for broader rendering + fallback triangle if some fonts strip emoji style
          const warn = '\u26A0\uFE0F'; // ⚠️
          els.joinedRoomTitle.textContent = base + ' (' + warn + ' ' + (window.i18n.Disconnected || 'DISCONNECTED') + ')';
        }
        break;
    }
  }
  function computeConnectionState(){
    // Trust tracked reconnection state (within last 60 seconds to cover max exponential backoff)
    const stateAge = Date.now() - _connectionState.lastUpdate;
    
    // If actively reconnecting via automatic reconnect (SignalR's withAutomaticReconnect), show reconnecting
    if(_connectionState.isReconnecting && _connectionState.reconnectSource === 'automatic' && stateAge < 60000){
      return 'reconnecting';
    }
    
    // If manually reconnecting (backend down scenario) but attempts are still fresh, show reconnecting
    // However, if manual reconnect has been going on for >10s, show disconnected (backend is likely down)
    if(_connectionState.isReconnecting && _connectionState.reconnectSource === 'manual'){
      if(stateAge < 10000){
        return 'reconnecting';  // First 10 seconds of manual reconnect attempts
      } else {
        return 'disconnected';  // After 10s of failed manual reconnects, backend is down
      }
    }
    
    // Trust recent event-driven state (within last 5 seconds)
    if(stateAge < 5000){
      return _connectionState.current;
    }
    
    // Fall back to polling hub.state for stale/missed events
    if(!hub) return 'disconnected';
    const stateStr = hub.state && hub.state.toLowerCase ? hub.state.toLowerCase() : '';
    if(stateStr==='connected') return 'connected';
    if(stateStr==='connecting') return 'reconnecting';
    if(stateStr==='reconnecting') return 'reconnecting';
    return 'disconnected';
  }
  function startConnectionStateLoop(){
    // Poll every 2.5s to catch any missed transitions (safety net)
    setInterval(()=>{ applyConnectionVisual(computeConnectionState()); }, 2500);
  }

  // --------------- Rendering ---------------
  function initialFrom(name, alt){
    const source = (name && typeof name === 'string' && name.trim().length>0 ? name : (alt||''));
    if(!source) return '?';
    const ch = source.trim().charAt(0).toUpperCase();
    return ch || '?';
  }
  function renderRooms(){ if(!els.roomsList) return; els.roomsList.innerHTML=''; state.rooms.forEach(r=>{ const li=document.createElement('li'); const a=document.createElement('a'); a.href='#'; a.textContent=r.name; a.dataset.roomId=r.id; 
    if(state.pendingJoin === r.name && (!state.joinedRoom || state.joinedRoom.name!==r.name)) a.classList.add('joining');
    if(state.joinedRoom && state.joinedRoom.name===r.name) a.classList.add('active');
    a.addEventListener('click',e=>{ e.preventDefault(); joinRoom(r.name); }); li.appendChild(a); els.roomsList.appendChild(li); }); }
  function renderUsers(){ if(!els.usersList) return; const term=state.filter.toLowerCase(); els.usersList.innerHTML=''; const filtered=state.users.filter(u=>!term|| (u.fullName||u.userName||'').toLowerCase().includes(term)); filtered.forEach(u=>{ const li=document.createElement('li'); li.dataset.username=u.userName; const wrap=document.createElement('div'); wrap.className='user'; if(!u.avatar){ const span=document.createElement('span'); span.className='avatar me-2 text-uppercase'; span.textContent=initialFrom(u.fullName, u.userName); wrap.appendChild(span);} else { const img=document.createElement('img'); img.className='avatar me-2'; img.src='/avatars/'+u.avatar; wrap.appendChild(img);} const info=document.createElement('div'); info.className='user-info'; const nameSpan=document.createElement('span'); nameSpan.className='name'; nameSpan.textContent=u.fullName || u.userName; info.appendChild(nameSpan); const deviceSpan=document.createElement('span'); deviceSpan.className='device'; deviceSpan.textContent=u.device || ''; info.appendChild(deviceSpan); wrap.appendChild(info); li.appendChild(wrap); els.usersList.appendChild(li); }); if(els.usersHeader && window.i18n?.whosHere) { const template = window.i18n.whosHere; els.usersHeader.textContent = template.replace('{0}', filtered.length); } }
  function formatDateParts(ts){ 
    const date=new Date(ts); 
    const now=new Date(); 
    const diffDays=Math.round((date-now)/(1000*3600*24)); 
    const day=date.getDate(); 
    const month=date.getMonth()+1; 
    const year=date.getFullYear(); 
    const culture = document.documentElement.lang || 'en';
    const use24Hour = !culture.startsWith('en'); // English uses 12-hour format
    let hours=date.getHours(); 
    const minutes=('0'+date.getMinutes()).slice(-2); 
    let ampm=''; 
    let timeHours;
    if (use24Hour) {
      timeHours = ('0'+hours).slice(-2);
    } else {
      ampm = hours>=12 ? (window.i18n.PM || 'PM') : (window.i18n.AM || 'AM'); 
      if(hours>12) hours=hours%12; 
      if(hours===0) hours=12;
      timeHours = hours;
    }
    const dateOnly=`${day}/${month}/${year}`; 
    const timeOnly = use24Hour ? `${timeHours}:${minutes}` : `${timeHours}:${minutes} ${ampm}`;
    const full=`${dateOnly} ${timeOnly}`; 
    let relative=dateOnly; 
    const today = window.i18n.Today || 'Today';
    const yesterday = window.i18n.Yesterday || 'Yesterday';
    if(diffDays===0) relative=`${today}, ${timeOnly}`; 
    else if(diffDays===-1) relative=`${yesterday}, ${timeOnly}`; 
    return {relative, full}; 
  }
  function renderMessages(){
    if(!els.messagesList) return;
    els.messagesList.innerHTML='';
    state.messages.forEach(m=> renderSingleMessage(m, false));
    finalizeMessageRender();
  }
  function renderSingleMessage(m, scrollIntoView){
    if(!els.messagesList) return;
    const li=document.createElement('li');
    li.dataset.cid = m.correlationId || '';
    if(typeof m.id === 'number') li.dataset.id = String(m.id);
    if(m.failed) li.classList.add('failed');
  const wrap=document.createElement('div'); wrap.className='message-item'; if(m.isMine) wrap.classList.add('ismine');
    if(!m.avatar){ const span=document.createElement('span'); span.className='avatar avatar-lg mx-2 text-uppercase'; span.textContent=initialFrom(m.fromFullName, m.fromUserName); wrap.appendChild(span);} else { const img=document.createElement('img'); img.className='avatar avatar-lg mx-2'; img.src='/avatars/'+m.avatar; wrap.appendChild(img);} 
    const content=document.createElement('div'); content.className='message-content';
    const info=document.createElement('div'); info.className='message-info d-flex flex-wrap align-items-center';
    const author=document.createElement('span'); author.className='author'; author.textContent=m.fromFullName||m.fromUserName; info.appendChild(author);
    const time=document.createElement('span'); time.className='timestamp'; const fp=formatDateParts(m.timestamp); time.textContent=fp.relative; time.dataset.bsTitle=fp.full; time.setAttribute('data-bs-toggle','tooltip'); info.appendChild(time);
    if(m.failed){
      const status=document.createElement('span'); status.className='send-status ms-2 text-danger'; status.textContent=window.i18n.MessageFailed || '(failed)'; info.appendChild(status);
      const retryBtn=document.createElement('button'); retryBtn.type='button'; retryBtn.className='btn btn-link p-0 ms-2 retry-send'; retryBtn.textContent=window.i18n.Retry || 'Retry'; retryBtn.addEventListener('click',()=> retrySend(m.correlationId)); info.appendChild(retryBtn);
    } else if(m.pending){
      const status=document.createElement('span'); status.className='send-status ms-2 text-muted'; status.textContent=window.i18n.MessagePending || '…'; info.appendChild(status);
    }
    content.appendChild(info);
  const body=document.createElement('div'); body.className='content'; body.textContent=m.content; content.appendChild(body);
  // Read receipt indicator (compact)
  const rr = document.createElement('div'); rr.className='read-receipt small text-muted';
  updateReadReceiptDom(rr, m);
  content.appendChild(rr);
    wrap.appendChild(content); li.appendChild(wrap); els.messagesList.appendChild(li);
    if(scrollIntoView){
      const mc=document.querySelector('.messages-container');
      if(mc && (state.autoScroll || state._firstRender)) mc.scrollTop=mc.scrollHeight;
    }
    return li;
  }
  function updateMessageDom(m){
    if(!els.messagesList || !m.correlationId) return false;
    const node = els.messagesList.querySelector('li[data-cid="'+m.correlationId+'"]');
    if(!node) return false;
    if(typeof m.id === 'number') node.dataset.id = String(m.id);
  const timeEl = node.querySelector('.timestamp');
    if(timeEl){ const fp=formatDateParts(m.timestamp); timeEl.textContent=fp.relative; timeEl.dataset.bsTitle=fp.full; }
    const bodyEl = node.querySelector('.content'); if(bodyEl) bodyEl.textContent = m.content;
    // Update status indicators
    node.classList.toggle('failed', !!m.failed);
    let statusEl = node.querySelector('.send-status');
    if(!statusEl && (m.failed||m.pending)){
      const info = node.querySelector('.message-info');
      if(info){ statusEl = document.createElement('span'); statusEl.className='send-status ms-2'; info.appendChild(statusEl); }
    }
    if(statusEl){
      if(m.failed){ statusEl.textContent=window.i18n.MessageFailed || '(failed)'; statusEl.className='send-status ms-2 text-danger'; }
      else if(m.pending){ statusEl.textContent=window.i18n.MessagePending || '…'; statusEl.className='send-status ms-2 text-muted'; }
      else { statusEl.remove(); }
    }
    // Retry button handling
    let retryBtn = node.querySelector('button.retry-send');
    if(m.failed){
      if(!retryBtn){
        const info = node.querySelector('.message-info');
        if(info){ retryBtn=document.createElement('button'); retryBtn.type='button'; retryBtn.className='btn btn-link p-0 ms-2 retry-send'; retryBtn.textContent=window.i18n.Retry || 'Retry'; retryBtn.addEventListener('click',()=> retrySend(m.correlationId)); info.appendChild(retryBtn); }
      }
    } else if(retryBtn){ retryBtn.remove(); }
    node.dataset.cid = m.correlationId || '';
    // Update read receipt
    let rr = node.querySelector('.read-receipt');
    if(!rr){ rr = document.createElement('div'); rr.className='read-receipt small text-muted'; const content = node.querySelector('.message-content'); if(content) content.appendChild(rr); }
    updateReadReceiptDom(rr, m);
    return true;
  }
  function updateReadReceiptDom(rrEl, m){
    if(!rrEl) return;
    const readers = Array.isArray(m.readBy) ? m.readBy : [];
    const mine = !!m.isMine;
    if(!mine){ rrEl.textContent=''; rrEl.classList.add('d-none'); return; }
    rrEl.classList.remove('d-none');
    // Show label for sender: 'Delivered' when nobody else read; otherwise list readers (excluding self)
    const selfName = (state.profile && state.profile.userName || '').toLowerCase();
    const others = readers.filter(u => u && u.toLowerCase() !== selfName);
    if(others.length === 0){ rrEl.textContent = window.i18n?.delivered || 'Delivered'; return; }
    rrEl.textContent = (window.i18n?.readBy || 'Read by') + ' ' + others.join(', ');
  }
  function finalizeMessageRender(){
    const noInfo=document.querySelector('.no-messages-info'); if(noInfo) noInfo.classList.toggle('d-none', state.messages.length>0);
    const mc=document.querySelector('.messages-container');
    if(mc && !state.loadingMore && (state.autoScroll || state._firstRender)) mc.scrollTop=mc.scrollHeight;
    state._firstRender = false;
    if(window.bootstrap){ const ttEls = [].slice.call(els.messagesList.querySelectorAll('[data-bs-toggle="tooltip"]')); ttEls.forEach(el=>{ try{ new window.bootstrap.Tooltip(el); }catch(_){} }); }
    // If container cannot scroll yet but more pages are available, auto-load a few pages to enable scrolling
    maybeAutoFillHistory();
    // After render settles, scan visible messages and mark them as read for this user (viewport-based)
    scheduleMarkVisibleRead();
  }
  function maybeAutoFillHistory(){
    try {
      const mc = document.querySelector('.messages-container');
      if(!mc) return;
      if(state.loadingMore || !state.canLoadMore || !state.joinedRoom || !state.oldestLoaded) return;
      // If content height is not enough to scroll, fetch older until scroll becomes possible (cap passes)
      const canScroll = mc.scrollHeight > (mc.clientHeight + 4);
      if(!canScroll && state._autoFillPass < 3){
        state._autoFillPass++;
        // small delay to allow DOM to settle before next page request
        setTimeout(()=> loadOlderMessages(), 0);
      }
    } catch(_) { /* ignore */ }
  }
  function renderProfile(){ const p=state.profile; if(!els.profileName) return; if(!p){ els.profileName.textContent='Not signed in'; if(els.profileAvatarImg) els.profileAvatarImg.classList.add('d-none'); if(els.profileAvatarInitial){ els.profileAvatarInitial.classList.remove('d-none'); els.profileAvatarInitial.textContent='?'; } if(els.btnLogin) els.btnLogin.classList.remove('d-none'); if(els.btnLogout) els.btnLogout.classList.add('d-none'); } else { els.profileName.textContent=p.fullName||p.userName; if(p.avatar){ if(els.profileAvatarImg){ els.profileAvatarImg.src='/avatars/'+p.avatar; els.profileAvatarImg.classList.remove('d-none'); } if(els.profileAvatarInitial) els.profileAvatarInitial.classList.add('d-none'); } else { if(els.profileAvatarInitial){ els.profileAvatarInitial.textContent=initialFrom(p.fullName, p.userName); els.profileAvatarInitial.classList.remove('d-none'); } if(els.profileAvatarImg) els.profileAvatarImg.classList.add('d-none'); } if(els.btnLogin) els.btnLogin.classList.add('d-none'); if(els.btnLogout) els.btnLogout.classList.remove('d-none'); } }
  // Enhanced profile renderer: show transitional 'Signing in…' during auth probe / grace
  const _renderProfileOriginal = renderProfile;
  renderProfile = function(){
    if(!els.profileName){ _renderProfileOriginal(); return; }
    if(!state.profile){
      const inGrace = isWithinAuthGrace();
      if(state.authStatus===AuthStatus.PROBING || inGrace){
        els.profileName.textContent='Signing in…';
        if(els.profileAvatarImg) els.profileAvatarImg.classList.add('d-none');
        if(els.profileAvatarInitial){ els.profileAvatarInitial.classList.remove('d-none'); els.profileAvatarInitial.textContent='?'; }
        if(els.btnLogin) els.btnLogin.classList.add('d-none'); // hide login while we are actively probing
        if(els.btnLogout) els.btnLogout.classList.add('d-none');
        return;
      }
    }
    _renderProfileOriginal();
  };

  // Defensive: ensure avatar initial shows correct letter if placeholder reappears
  function ensureProfileAvatar(){
    if(!state.profile || state.profile.avatar) return;
    if(els.profileAvatarInitial){
      const desired = initialFrom(state.profile.fullName, state.profile.userName);
      els.profileAvatarInitial.textContent = desired;
      els.profileAvatarInitial.classList.remove('d-none');
      if(els.profileAvatarImg) els.profileAvatarImg.classList.add('d-none');
    }
  }
  function renderRoomContext(){ if(!els.roomsPanel||!els.roomsNoSelection) return; const hasRoom=!!state.joinedRoom; els.roomsPanel.classList.toggle('d-none', !hasRoom); els.roomsNoSelection.classList.toggle('d-none', hasRoom); if(state.joinedRoom && els.joinedRoomTitle) els.joinedRoomTitle.textContent=state.joinedRoom.name; renderRooms(); }
  function renderAll(){ renderProfile(); renderRoomContext(); renderUsers(); renderMessages(); }
  function renderQueueBadge(){ if(!els.queueBadge) return; const qLen = state.outbox.length; els.queueBadge.textContent = qLen; els.queueBadge.classList.toggle('d-none', qLen===0); }

  // Ensure there is a single optimistic message in UI for a given correlationId; if missing, create it.
  function ensureOptimisticMessage(text, correlationId){
    if(!correlationId) return;
    const existing = state.messages.find(m=> m.correlationId === correlationId);
    if(existing) return;
    const nowIso = new Date().toISOString();
    const rec={id: pendingIdCounter--, content:text, timestamp:nowIso, fromUserName:state.profile?.userName, fromFullName:state.profile?.fullName, avatar:state.profile?.avatar, isMine: !!state.profile, correlationId, pending:true};
    state.messages.push(rec);
    if(els.messagesList && state.messages.length>1){ renderSingleMessage(rec, true); finalizeMessageRender(); } else { renderMessages(); }
  }

  // Preserve unsent (pending/failed) messages across forced room reloads (e.g., reconnect)
  function snapshotUnsentForRoom(roomName, reason){
    try {
      if(!roomName || !Array.isArray(state.messages) || !state.messages.length) return;
      const keep = state.messages.filter(m=> m && m.isMine && (m.pending || m.failed));
      if(!keep.length) return;
      state.unsentByRoom = state.unsentByRoom || {};
      state.unsentByRoom[roomName] = keep.map(m=> ({
        content: m.content,
        timestamp: m.timestamp,
        fromUserName: m.fromUserName,
        fromFullName: m.fromFullName,
        avatar: m.avatar,
        isMine: true,
        correlationId: m.correlationId || null,
        pending: !!m.pending,
        failed: !!m.failed,
        id: m.id
      }));
      postTelemetry('unsent.snapshot',{room: roomName, count: keep.length, reason});
    } catch(_) { /* ignore */ }
  }
  function restoreUnsentForRoom(roomName){
    try {
      if(!roomName || !state.unsentByRoom || !state.unsentByRoom[roomName]) return;
      const items = state.unsentByRoom[roomName];
      delete state.unsentByRoom[roomName];
      // Filter out any duplicates by correlationId/content already present after load
      const existingCids = new Set(state.messages.map(m=> m.correlationId).filter(Boolean));
      const existingPairs = new Set(state.messages.map(m=> (m.isMine? (m.content+'|'+m.timestamp): null)).filter(Boolean));
      const toAppend = [];
      for(const m of items){
        if(m.correlationId && existingCids.has(m.correlationId)) continue;
        if(existingPairs.has(m.content+'|'+m.timestamp)) continue;
        toAppend.push(m);
      }
      if(!toAppend.length) return;
      // Ensure pending flags preserved; if they were pending at snapshot, keep pending; failed stays failed
      toAppend.forEach(m=>{ if(m.pending && !m.failed) m.pending = true; });
      state.messages = state.messages.concat(toAppend);
      postTelemetry('unsent.restore',{room: roomName, count: toAppend.length});
    } catch(_) { /* ignore */ }
  }

  // --------------- API ----------------
  const apiGet = url => fetch(url,{credentials:'include'}).then(r=> r.ok? r.json(): Promise.reject(r));
  const apiPost = (u,o)=> fetch(u,{method:'POST',headers:{'Content-Type':'application/json'},credentials:'include',body:JSON.stringify(o)});
  const apiPut = (u,o)=> fetch(u,{method:'PUT',headers:{'Content-Type':'application/json'},credentials:'include',body:JSON.stringify(o)});
  const apiDelete = u => fetch(u,{method:'DELETE',credentials:'include'});

  // --------------- SignalR --------------
  let hub=null, reconnectAttempts=0; const maxBackoff=30000;
  // Infinite reconnect policy for SignalR (keeps trying forever with capped backoff)
  const InfiniteRetryPolicy = {
    nextRetryDelayInMilliseconds(context){
      // context has previousRetryCount, elapsedMilliseconds, retryReason
      const attempt = (context && typeof context.previousRetryCount==='number') ? context.previousRetryCount + 1 : 1;
      // Start at 0, 2s, 5s, 10s then cap at 30s
      const schedule = [0, 2000, 5000, 10000];
      const base = attempt <= schedule.length ? schedule[attempt-1] : 30000;
      const delay = Math.min(base, 30000);
      return delay;
    }
  };
  // Secure random utilities -------------------------------------------------
  function secureRandomBytes(len){
    const arr = new Uint8Array(len);
    if(window.crypto && window.crypto.getRandomValues){ window.crypto.getRandomValues(arr); }
    else {
      // Fallback: VERY rare (older browsers); degrade to Math.random with warning (still better than throwing)
      for(let i=0;i<len;i++){ arr[i] = Math.floor(Math.random()*256); }
    }
    return arr;
  }
  function secureRandomId(prefix, length){
    // produce base36-ish string from random bytes
    const bytes = secureRandomBytes(length);
    let chars = '';
    for(const b of bytes){ chars += (b & 0x0f).toString(16); } // hex nibble -> 1x entropy per byte (4 bits used)
    // trim / slice to requested nibble length (length * 2 nibbles currently but we used only lower nibble for stability)
    // We used only lower nibble, so chars length === length. Acceptable for correlation uniqueness.
    return (prefix||'') + chars + '_' + Date.now().toString(36);
  }
  // Telemetry helper (fire-and-forget) with session correlation (secure ID)
  const sessionId = secureRandomId('s_8', 8); // 8 random nibbles + timestamp
  const _telemetryRecent = {};
  const TELEMETRY_TTL_MS = 4000; // suppress identical high-chatter events (e.g. repeated skips) within 4s window
  function postTelemetry(event, data){
    try {
      const now = Date.now();
      // Build a suppression key only for noisy families; others always send
      let key;
      if(/^send\.flush\.skip$/.test(event)) key = event + '|' + (data&&data.reason);
      if(/^send\.queue$/.test(event)) key = event + '|' + (data&&data.reason);
      if(/^hub\.connect\.retry$/.test(event)) key = event + '|' + (data&&data.attempt);
      if(key){
        const prev = _telemetryRecent[key];
        if(prev && (now - prev) < TELEMETRY_TTL_MS){ return; }
        _telemetryRecent[key] = now;
      }
      fetch('/api/telemetry/reconnect',{
        method:'POST',
        headers:{'Content-Type':'application/json'},
        body:JSON.stringify({evt:event, ts: now, sessionId, ...data})
      });
    } catch(_) { /* ignore */ }
  }
  // Telemetry suppression cache compaction (prevents unbounded growth across very long sessions)
  setInterval(()=>{
    try {
      const now = Date.now();
      const maxAge = TELEMETRY_TTL_MS * 3; // prune anything not touched in ~3 windows
      for(const k in _telemetryRecent){
        if(now - _telemetryRecent[k] > maxAge){ delete _telemetryRecent[k]; }
      }
    } catch(_) { /* ignore */ }
  }, 120000); // every 2 minutes
  /**
   * Classify a SignalR (or underlying transport) error into a coarse category for reconnect telemetry.
   * @param {any} err Error or reason object
   * @returns {{cat:string,msg:string}} category + original message
   */
  function classifyError(err){
    if(!err) return {cat:'unknown', msg:''};
    const msg=(err && (err.message||err.toString()))||'';
    if(/403|401|unauthor/i.test(msg)) return {cat:'auth', msg};
    if(/timeout|timed out/i.test(msg)) return {cat:'timeout', msg};
    if(/network|fetch|socket|ws|transport/i.test(msg)) return {cat:'transport', msg};
    if(/server|500|hubexception/i.test(msg)) return {cat:'server', msg};
    return {cat:'other', msg};
  }
  let lastReconnectError=null;
  function ensureHub(){
    if(hub) return hub;
    hub=new signalR.HubConnectionBuilder()
      .withUrl('/chatHub')
      .withAutomaticReconnect(InfiniteRetryPolicy)
      .build();
    // Extend timeouts to tolerate background tab timer throttling and long sleeps
    try {
      // If the server doesn't send a message within this interval, the client considers the connection lost.
      // Set to 12h to tolerate long background/minimized periods without forcing a disconnect.
      hub.serverTimeoutInMilliseconds = 12 * 60 * 60 * 1000; // 12 hours
      // Client keep-alive pings. Browsers may throttle timers in background; a 20s interval is fine.
      hub.keepAliveIntervalInMilliseconds = 20000; // 20 seconds
    } catch(_) { /* some transports may not expose setters; ignore */ }
    wireHub(hub);
    return hub;
  }
  function startHub(){
    ensureHub();
    // Prevent duplicate start attempts while hub is already in a non-Disconnected state.
    const currentState = hub.state && hub.state.toLowerCase ? hub.state.toLowerCase() : '';
    if(currentState && currentState !== 'disconnected'){
      // Hub is already connecting/reconnecting - ensure visual state reflects this
      if(currentState === 'connecting' || currentState === 'reconnecting'){
        // Mark as reconnecting if not already tracked
        if(!_connectionState.isReconnecting){
          _connectionState = {
            current: 'reconnecting',
            lastUpdate: Date.now(),
            isReconnecting: true,
            reconnectSource: currentState === 'reconnecting' ? 'automatic' : 'manual'
          };
          applyConnectionVisual('reconnecting');
        }
      }
      // Avoid noisy reconnect attempt 0 inflation.
      log('warn','signalr.start.skip',{state: currentState});
      postTelemetry('hub.connect.skip',{state: currentState});
      return Promise.resolve();
    }
    log('info','signalr.start');
    
    // Mark as reconnecting before starting
    _connectionState = {
      current: 'reconnecting',
      lastUpdate: Date.now(),
      isReconnecting: true,
      reconnectSource: 'manual'
    };
    applyConnectionVisual('reconnecting'); // initial connecting state
    
    const startedAt = performance.now();
    if(state.authStatus===AuthStatus.PROBING && !state.profile){
      // hub started before auth probe finished -> early start telemetry (once)
      if(!state._hubStartedEarly){ postTelemetry('auth.hub.start.early',{}); state._hubStartedEarly=true; }
    }
    return hub.start().then(()=>{
      reconnectAttempts=0; lastReconnectError=null;
      
      // Clear reconnecting state - connection successful
      _connectionState = {
        current: 'connected',
        lastUpdate: Date.now(),
        isReconnecting: false,
        reconnectSource: null
      };
      
      // Re-probe auth if we don't have a profile yet (handles case where SignalR connects before HTTP session cookie set)
      if (!state.profile && (state.authStatus === AuthStatus.PROBING || state.authStatus === AuthStatus.UNKNOWN)) {
        fetch('/api/auth/me', {credentials:'include'})
          .then(r => r.ok ? r.json() : null)
          .then(u => {
            if (u && u.userName) {
              state.profile = {userName: u.userName, fullName: u.fullName, avatar: u.avatar};
              state.authStatus = AuthStatus.AUTHENTICATED;
              renderProfile();
              postTelemetry('auth.reprobe.success', {});
            }
          })
          .catch(() => { /* ignore - UI already handles unauthenticated state */ });
      }
      
      loadRooms();
      postTelemetry('hub.connected',{durationMs: Math.round(performance.now()-startedAt)});
  applyConnectionVisual('connected');
      // Fallback: if we already have a profile (from /api/auth/me) but still showing loading because getProfileInfo hasn't fired, hide loader.
      if(state.profile && state.loading){ setLoading(false); }
    }).catch(err=>{ 
      const msg = (err && err.message)||'';
      // If the error indicates a duplicate start (already starting/started), suppress scheduling a reconnect.
      if(/not in the 'disconnected' state/i.test(msg) || /cannot start a hubconnection that is not in the 'disconnected' state/i.test(msg)){
        log('warn','signalr.start.duplicate',{message: msg});
        postTelemetry('hub.connect.duplicateStart',{message: msg});
        return; // leave existing connection / reconnection logic intact
      }
      log('error','signalr.start.fail',{error:msg}); 
      postTelemetry('hub.connect.fail',{message: msg}); 
      scheduleReconnect(err); 
    });
  }
  // Proactively ensure a connection is in progress/established when conditions allow (e.g., tab becomes visible or network back online)
  function ensureConnected(){
    ensureHub();
    const s = hub.state && hub.state.toLowerCase ? hub.state.toLowerCase() : '';
    if(s==='disconnected'){
      // Kick off a start cycle; automatic reconnect handles mid-connection losses
      startHub();
    }
  }
  /**
   * Applies exponential backoff and records telemetry for each reconnect attempt.
   * @param {Error} err the error that triggered scheduling
   */
  function scheduleReconnect(err){
    if(err) lastReconnectError=err;
    reconnectAttempts++;
    
    // Mark manual reconnection state
    _connectionState = {
      current: 'reconnecting',
      lastUpdate: Date.now(),
      isReconnecting: true,
      reconnectSource: 'manual'
    };
    
    const delay=Math.min(1000*Math.pow(2,reconnectAttempts), maxBackoff);
  applyConnectionVisual('reconnecting');
    const {cat,msg}=classifyError(lastReconnectError);
    postTelemetry('hub.connect.retry',{
      attempt: reconnectAttempts,
      delayMs: delay,
      errorCategory: cat,
      errorMessage: msg.slice(0,300)
    });
    setTimeout(()=> startHub().catch(e=> scheduleReconnect(e)), delay);
  }
  function wireHub(c){
  c.on('getProfileInfo', u=>{ state.profile={userName:u.userName, fullName:u.fullName, avatar:u.avatar}; state.authStatus = AuthStatus.AUTHENTICATED; setLoading(false); renderProfile(); flushOutbox('profile'); });
    // Full presence snapshot after join (server push) ensures newly joined user and existing members converge
    c.on('presenceSnapshot', list=> { if(!Array.isArray(list)) return; // Replace users list for current room only
      const normalized = list.map(u=>({
        userName: u.userName || u.UserName || u.username || '',
        fullName: u.fullName || u.FullName || u.name || '',
        avatar: u.avatar || u.Avatar || '',
        currentRoom: u.currentRoom || u.CurrentRoom || u.room || null,
        ...u
      }));
      if(state.joinedRoom){
        state.users = normalized.filter(u=> u.currentRoom === state.joinedRoom.name);
      } else {
        state.users = normalized;
      }
      renderUsers(); });
    c.on('newMessage', m=> {
      const mineUser = state.profile && state.profile.userName;
      // Reconcile by correlationId for own messages regardless of pending ack state
      if(mineUser && m.correlationId){
        const idx = state.messages.findIndex(x=> x.isMine && x.correlationId===m.correlationId);
        if(idx>=0){
          state.messages[idx] = {id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine:true, correlationId:m.correlationId, readBy: m.readBy||[]};
          // pendingMessages entry may have been removed on invoke ack; ensure cleanup if present
          if(state.pendingMessages[m.correlationId]) delete state.pendingMessages[m.correlationId];
          // Remove any duplicate server entry with the same id that might have been loaded via history
          for(let j=state.messages.length-1;j>=0;j--){
            if(j!==idx && state.messages[j] && state.messages[j].id===m.id){
              state.messages.splice(j,1);
            }
          }
          renderMessages();
          finalizeMessageRender();
          return;
        }
      }
      // If we didn't find the optimistic by correlationId, but a message with the same server id already exists (e.g., from history), update it in place and avoid adding a duplicate
      if(m && m.id!==undefined){
        const byIdIdx = state.messages.findIndex(x=> x && x.id===m.id);
        if(byIdIdx>=0){
          const isMine = !!(state.profile && state.profile.userName === m.fromUserName);
          state.messages[byIdIdx] = {id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine, correlationId: m.correlationId||state.messages[byIdIdx].correlationId, readBy: m.readBy||state.messages[byIdIdx].readBy||[]};
          renderMessages();
          finalizeMessageRender();
          return;
        }
      }
  addOrRenderMessage({ ...m, correlationId: m.correlationId });
      // Increment unread and start/continue title blinking until the message is read
      try {
        const isMine = !!(state.profile && state.profile.userName === m.fromUserName);
        if(!isMine){
          if(!isReadingView()){
            state.unreadCount = (state.unreadCount||0) + 1;
            updateTitleBlinkLabel(m.room || (state.joinedRoom && state.joinedRoom.name) || '');
            startTitleBlink();
          } else {
            // User is actively viewing - schedule viewport-based read marking
            scheduleMarkVisibleRead();
          }
        }
      } catch(_) { /* ignore */ }
    });
    c.on('notify', n=> handleNotify(n));
    c.on('messageRead', payload => {
      try {
        const id = payload && (payload.id||payload.Id);
        const readers = (payload && (payload.readers||payload.Readers)) || [];
        const idx = state.messages.findIndex(x=> x && x.id===id);
        if(idx>=0){
          const msg = state.messages[idx];
          msg.readBy = readers;
          updateMessageDom(msg) || renderMessages();
        }
      } catch(_) { /* ignore */ }
    });
    // Automatic reconnect handlers (withAutomaticReconnect is enabled on builder)
    // On successful re-connect, request a fresh user list for the current room.
    // This compensates for any missed join/leave events during the disconnected interval.
    c.onreconnected(connectionId => {
      // Clear reconnecting state - automatic reconnect successful
      _connectionState = {
        current: 'connected',
        lastUpdate: Date.now(),
        isReconnecting: false,
        reconnectSource: null
      };
      
      try {
        if(state.joinedRoom){
          // Snapshot unsent local messages for this room before the forced reload wipes the list
          snapshotUnsentForRoom(state.joinedRoom.name, 'reconnected');
          // Force re-join to ensure group membership and refresh presence/messages
          joinRoom(state.joinedRoom.name, /*force*/ true);
        } else {
          // No prior room, refresh rooms list
          loadRooms();
        }
      } catch(_){ }
      applyConnectionVisual('connected');
    });
  c.onreconnecting(err => { 
    // Mark automatic reconnection state
    _connectionState = {
      current: 'reconnecting',
      lastUpdate: Date.now(),
      isReconnecting: true,
      reconnectSource: 'automatic'
    };
    
    log('warn','hub.reconnecting', {message: err && err.message}); 
    applyConnectionVisual('reconnecting'); 
  });
  c.onclose((err)=> { 
    log('warn','hub.onclose', {message: err && err.message}); 
    
    // Only set disconnected if not actively reconnecting (manual reconnect will handle state)
    if(!_connectionState.isReconnecting){
      _connectionState = {
        current: 'disconnected',
        lastUpdate: Date.now(),
        isReconnecting: false,
        reconnectSource: null
      };
      
      // If connection closed with an error, trigger manual reconnection
      // This handles cases where backend is down and automatic reconnect doesn't kick in
      if(err){
        log('warn','hub.onclose.error.triggering.reconnect',{msg: (err.message||'').slice(0,120)});
        scheduleReconnect(err);
      }
    }
    applyConnectionVisual(_connectionState.current); 
    // Clear presence list when fully disconnected to avoid showing stale users.
    state.users = [];
    renderUsers();
  });
    // Other hub-driven mutations
    c.on('addUser', u=> upsertUser(u));
    c.on('removeUser', u=> removeUser(u.userName));
    c.on('addChatRoom', r=> upsertRoom(r));
    c.on('updateChatRoom', r=> upsertRoom(r));
    c.on('removeChatRoom', id=>{ state.rooms=state.rooms.filter(x=>x.id!==id); if(state.joinedRoom && state.joinedRoom.id===id){ state.joinedRoom=null; state.messages=[]; } renderAll(); });
    c.on('onError', msg=> showError(msg));
  }

  // --------------- Mutators -------------
  function upsertRoom(r){ const e=state.rooms.find(x=>x.id===r.id); if(e){ e.name=r.name; e.admin=r.admin; } else state.rooms.push({id:r.id,name:r.name,admin:r.admin}); renderRooms(); }
  function upsertUser(u){
    const normalized = {
      userName: u.userName || u.UserName || u.username || '',
      fullName: u.fullName || u.FullName || u.name || '',
      avatar: u.avatar || u.Avatar || '',
      currentRoom: u.currentRoom || u.CurrentRoom || u.room || null,
      ...u
    };
    const e=state.users.find(x=>x.userName===normalized.userName);
    if(e) Object.assign(e, normalized); else state.users.push(normalized);
    renderUsers();
  }
  function removeUser(userName){ state.users=state.users.filter(u=>u.userName!==userName); renderUsers(); }
  /**
   * Appends a message to local state and re-renders.
   * @param {object} m MessageViewModel-like payload
   */
  function addOrRenderMessage(m){
    const isMine=state.profile && state.profile.userName===m.fromUserName;
    // Deduplicate: prefer correlationId for own optimistic merges; fallback to server id if present
    let existingIdx = -1;
    if(m && m.correlationId){ existingIdx = state.messages.findIndex(x=> x && x.correlationId===m.correlationId); }
    if(existingIdx<0 && m && m.id!==undefined){ existingIdx = state.messages.findIndex(x=> x && x.id===m.id); }
  const rec={id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine, correlationId:m.correlationId, readBy: m.readBy||[]};
    if(existingIdx>=0){
      state.messages[existingIdx] = rec;
      renderMessages();
    } else {
      state.messages.push(rec);
      if(els.messagesList && state.messages.length>1){ renderSingleMessage(rec, true); finalizeMessageRender(); } else { renderMessages(); }
    }
  }

  /**
   * Handles a direct notification (non-room message). We surface it as a system message in the current transcript.
   * @param {{title:string,body:string,from:string,ts:string}} n
   */
  function handleNotify(n){
    const ts = n.ts || new Date().toISOString();
    const content = `[NOTIFY] ${n.title}${n.body?': '+n.body:''}`;
    state.messages.push({id:'notify_'+ts, content, timestamp:ts, fromUserName:n.from||'system', fromFullName:n.from||'system', avatar:null, isMine:false});
    renderMessages();
  }

  // --------------- Actions --------------
  function joinRoom(roomName, force){
    if(!hub||!roomName) return;
    if(state.joinInProgress && state.pendingJoin===roomName) return; // already joining this room
    // When force=true (e.g., after reconnect), bypass the early return to re-establish SignalR group membership
    if(!force && state.joinedRoom && state.joinedRoom.name===roomName && !state.joinInProgress) return; // already in room
    if(force && state.joinedRoom && state.joinedRoom.name===roomName){
      // Before reloading messages on a forced re-join, snapshot unsent content
      snapshotUnsentForRoom(roomName, 'forceJoin');
    }
    const attemptToken = secureRandomId('j_',8);
    state._joinToken = attemptToken;
    state.joinInProgress = true;
    state.pendingJoin = roomName;
    renderRooms(); // show joining state
    const startedAt = performance.now();
    const MAX_RETRIES = 3;
    let attempt = 0;

    function performJoin(){
      attempt++;
      hub.invoke('Join',roomName).then(()=>{
        // Ensure this result is for the latest token and still desired room
        if(state._joinToken !== attemptToken || state.pendingJoin !== roomName){
          return; // stale
        }
        state.joinedRoom = state.rooms.find(r=>r.name===roomName)||{name:roomName};
        state.pendingJoin = null; state.joinInProgress=false;
        localStorage.setItem('lastRoom',roomName);
        if(els.joinedRoomTitle){
          state._baseRoomTitle = state.joinedRoom.name;
          els.joinedRoomTitle.textContent = state._baseRoomTitle;
        }
        loadUsers(); loadMessages(); renderRoomContext(); ensureProfileAvatar();
        postTelemetry('room.join.success',{room:roomName, durationMs: Math.round(performance.now()-startedAt), attempts:attempt});
        flushOutbox('join');
      }).catch(err=>{
        // If token changed, abandon silently
        if(state._joinToken !== attemptToken) return;
        const msg = (err && err.message)||'';
        const transient = /timeout|temporar|network|reconnect|connection/i.test(msg);
        if(transient && attempt < MAX_RETRIES){
          const backoff = 300 * attempt; // linear backoff (0,300,600)
          postTelemetry('room.join.retry',{room:roomName, attempt, backoff, msg: msg.slice(0,120)});
          setTimeout(performJoin, backoff);
        } else {
          if(state.pendingJoin===roomName){ state.joinInProgress=false; state.pendingJoin=null; }
          renderRooms();
          postTelemetry('room.join.fail',{room:roomName, attempts:attempt, msg: msg.slice(0,200)});
          showError('Join failed: '+msg);
          // If join failed after retries, the backend may be down - trigger manual reconnect
          log('warn','room.join.failed.triggering.reconnect',{room:roomName, msg: msg.slice(0,120)});
          scheduleReconnect(err);
        }
      });
    }
    performJoin();
  }
  function loadRooms(){
    apiGet('/api/Rooms').then(list=>{
      // Defensive normalization: support either PascalCase (Id/Name/Admin) or camelCase (id/name/admin)
      state.rooms = (list||[]).map(r=>({
        id: r.id!==undefined? r.id : r.Id,
        name: r.name!==undefined? r.name : r.Name,
        admin: r.admin!==undefined? r.admin : r.Admin
      }));
      renderRooms();
      const stored=localStorage.getItem('lastRoom');
      if(stored && state.rooms.some(r=>r.name===stored)) joinRoom(stored);
      else if(state.rooms.length>0) joinRoom(state.rooms[0].name);
    }).catch(()=>{});
  }
  function loadUsers(){ if(!state.joinedRoom) return; hub.invoke('GetUsers', state.joinedRoom.name).then(users=>{
    const normalized = (users||[]).map(u=>({
      userName: u.userName || u.UserName || u.username || '',
      fullName: u.fullName || u.FullName || u.name || '',
      avatar: u.avatar || u.Avatar || '',
      currentRoom: u.currentRoom || u.CurrentRoom || u.room || null,
      ...u
    }));
    state.users=normalized;
    renderUsers();
  }).catch(err=>{
    log('warn','loadUsers.failed',{msg: err && err.message});
    // Backend may be down - trigger reconnect after a short delay
    setTimeout(()=> scheduleReconnect(err), 1000);
  }); }
  /**
   * Loads most recent page of messages for joined room (resets pagination state).
   */
  function loadMessages(){
    if(!state.joinedRoom) return;
    state.oldestLoaded=null; state.canLoadMore=true; state._autoFillPass = 0;
    apiGet('/api/Messages/Room/'+encodeURIComponent(state.joinedRoom.name)+'?take='+state.pageSize)
      .then(list=>{
        // Server returns ascending order (repository sorts ascending)
  state.messages = list.map(m=>({id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine: state.profile && state.profile.userName===m.fromUserName, readBy: m.readBy||[]}));
        // After baseline load, reattach any locally unsent (pending/failed) messages captured for this room
        restoreUnsentForRoom(state.joinedRoom.name);
        if(state.messages.length>0){
          state.oldestLoaded = state.messages[0].timestamp;
          // If fewer than requested, no more pages
          if(list.length < state.pageSize) state.canLoadMore = false;
        } else {
          state.canLoadMore = false;
        }
        renderMessages();
        attachScrollPagination();
      }).catch(()=>{});
  }
  /**
   * Loads the next (older) page of messages and prepends them while preserving scroll position.
   */
  let _pageReqToken = 0;
  function loadOlderMessages(){
    if(!state.canLoadMore || state.loadingMore || !state.joinedRoom || !state.oldestLoaded) return;
    state.loadingMore = true; // set immediately to block concurrent triggers
    const reqToken = ++_pageReqToken;
    const beforeTs = state.oldestLoaded; // capture for telemetry & consistency
    const before = encodeURIComponent(beforeTs);
    postTelemetry('messages.page.req',{before: beforeTs, token: reqToken});
    apiGet('/api/Messages/Room/'+encodeURIComponent(state.joinedRoom.name)+'?before='+before+'&take='+state.pageSize)
      .then(list=>{
        if(reqToken !== _pageReqToken){
          // Stale (a newer pagination started meanwhile); ignore
          postTelemetry('messages.page.stale',{token:reqToken});
          return;
        }
        if(!Array.isArray(list) || list.length===0){ state.canLoadMore=false; postTelemetry('messages.page.empty',{token:reqToken}); return; }
  const mapped=list.map(m=>({id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine: state.profile && state.profile.userName===m.fromUserName, readBy: m.readBy||[]}));
        const mc=document.querySelector('.messages-container'); let prevScrollHeight = mc? mc.scrollHeight:0;
        // Prepend and adjust oldest timestamp verifying monotonicity
        const prevOldest = state.oldestLoaded;
        state.messages = mapped.concat(state.messages);
        state.oldestLoaded = state.messages[0].timestamp;
        if(prevOldest && state.oldestLoaded > prevOldest){
          // Anomaly: new oldest is newer than previous oldest (out-of-order). Log & revert oldest pointer to previous to avoid skipping.
          postTelemetry('messages.page.nonmonotonic',{prev:prevOldest, now: state.oldestLoaded});
          state.oldestLoaded = prevOldest; // keep previous anchor to retry later if needed
        }
        if(list.length < state.pageSize) state.canLoadMore=false;
        renderMessages();
        if(mc){ const newScrollHeight = mc.scrollHeight; mc.scrollTop = newScrollHeight - prevScrollHeight; }
        postTelemetry('messages.page.ok',{token:reqToken, added:list.length, remaining: state.canLoadMore?1:0});
      })
      .catch(err=>{ postTelemetry('messages.page.err',{token:reqToken, msg: (err&&err.message)||''}); })
      .finally(()=>{ if(reqToken === _pageReqToken) state.loadingMore=false; });
  }
  /**
   * One-time scroll listener that triggers pagination when scrolled near top.
   */
  function attachScrollPagination(){
    const mc=document.querySelector('.messages-container');
    if(!mc || mc.dataset.paginated) return;
    mc.dataset.paginated='true';
    let lastReq=0;
    mc.addEventListener('scroll',()=>{
      // Update auto-scroll preference: stick to bottom only when the user is at bottom
      state.autoScroll = isAtBottom();
      if(mc.scrollTop < 40){ // slightly larger threshold for slower devices
        const now=Date.now();
        if(now-lastReq>500){ // widen throttle window to reduce rapid duplicates
          lastReq=now;
          // Proactively set loadingMore if eligible to block overlapping triggers
          if(!state.loadingMore && state.canLoadMore && state.joinedRoom && state.oldestLoaded){
            loadOlderMessages();
          }
        }
      }
      // Also check read status when user scrolls
      maybeMarkRead();
      // And mark any newly visible messages as read (viewport-based)
      scheduleMarkVisibleRead();
    });
  }
  let pendingIdCounter=-1;
  function internalSendMessage(text, bypassRateLimit, fromFlush, resendCorrelationId){
    const now=Date.now();
    if(!bypassRateLimit && (now - state.lastSendAt < state.minSendIntervalMs)){
      showError('You are sending messages too quickly');
      return Promise.reject('rateLimited');
    }
    if(!fromFlush && !resendCorrelationId){
      state.lastSendAt = now; // don't advance send timestamp for flush/resend so user can still send manually
    }
    let correlationId = resendCorrelationId || secureRandomId('c_', 12);
    let tempId;
    if(resendCorrelationId){
      // Reuse existing optimistic message (already rendered)
      const existing = state.messages.find(m=> m.correlationId===resendCorrelationId);
      if(existing){ tempId = existing.id; }
    } else {
      tempId = pendingIdCounter--;
      const nowIso = new Date().toISOString();
  const rec={id:tempId,content:text,timestamp:nowIso,fromUserName:state.profile?.userName,fromFullName:state.profile?.fullName,avatar:state.profile?.avatar,isMine: !!state.profile, correlationId, pending:true};
  state.messages.push(rec);
  if(els.messagesList && state.messages.length>1){ renderSingleMessage(rec, true); finalizeMessageRender(); } else { renderMessages(); }
    }
    if(!hub){ return Promise.reject('noHub'); }
    const pm = state.pendingMessages[correlationId] || { attempts:0, createdAt: Date.now(), text, tempId };
    pm.attempts++;
    state.pendingMessages[correlationId] = pm;
    postTelemetry('send.attempt',{cid:correlationId, len:text.length, fromFlush:!!fromFlush, attempts: pm.attempts, resend: !!resendCorrelationId});
    const p = hub.invoke('SendMessage', text, correlationId)
      .then(()=>{ postTelemetry('send.invoke.ok',{cid:correlationId}); markMessageDelivered(correlationId); })
      .catch(err=>{
        const msg=(err&&err.message)||'';
        const record = state.messages.find(m=> m.correlationId===correlationId);
        if(record){ record.failed=true; record.pending=false; }
        delete state.pendingMessages[correlationId];
        clearAckTimeout(correlationId);
        postTelemetry('send.invoke.fail',{cid:correlationId, msg:msg});
        if(record){ if(!updateMessageDom(record)) renderMessages(); else finalizeMessageRender(); }
        // If SendMessage fails, backend may be down - trigger reconnect after short delay
        log('warn','sendMessage.failed.triggering.reconnect',{msg: msg.slice(0,120)});
        setTimeout(()=> scheduleReconnect(err), 500);
        throw err;
      });
    // Schedule optimistic acknowledgement timeout (30s)
    // Always ensure an ack timeout exists for this correlation id; if one is already pending the timer path will no-op on ack
    scheduleAckTimeout(correlationId);
    return p;
  }
  function clearAckTimeout(correlationId){
    const t = state.ackTimers && state.ackTimers[correlationId];
    if(t){ try { clearTimeout(t); } catch(_) {} delete state.ackTimers[correlationId]; }
  }
  function scheduleAckTimeout(correlationId){
    const TIMEOUT_MS = 30000;
    if(!state.ackTimers) state.ackTimers = {};
    // If a timer already exists for this correlationId, do nothing
    if(state.ackTimers[correlationId]) return;
    state.ackTimers[correlationId] = setTimeout(()=>{
      // Drop the handle — this invocation owns the slot
      delete state.ackTimers[correlationId];
      const entry = state.pendingMessages[correlationId];
      if(!entry) return; // already acked
      if(entry.attempts >= 3){
        // Give up
        postTelemetry('send.timeout.giveup',{cid:correlationId, attempts: entry.attempts});
        postTelemetry('send.reconcile',{cid:correlationId, mode:'giveup', attempts: entry.attempts, latencyMs: Date.now()-entry.createdAt});
        delete state.pendingMessages[correlationId];
        const record = state.messages.find(m=> m.correlationId===correlationId);
        if(record){ record.failed=true; record.pending=false; if(!updateMessageDom(record)) renderMessages(); else finalizeMessageRender(); }
        return;
      }
      postTelemetry('send.timeout.resend',{cid:correlationId, attempts: entry.attempts});
      postTelemetry('send.reconcile',{cid:correlationId, mode:'timeout-resend', attempts: entry.attempts, latencyMs: Date.now()-entry.createdAt});
      internalSendMessage(entry.text, /*bypassRateLimit*/ true, /*fromFlush*/ true, correlationId);
      // Reschedule a new timeout if still pending — guarded by state.ackTimers
      scheduleAckTimeout(correlationId);
    }, TIMEOUT_MS);
  }
  function markMessageDelivered(correlationId){
    const record = state.messages.find(m=> m.correlationId===correlationId);
    if(record){ record.pending=false; record.failed=false; if(!updateMessageDom(record)) renderMessages(); else finalizeMessageRender(); }
    const entry = state.pendingMessages[correlationId];
    if(entry){
      postTelemetry('send.reconcile',{cid:correlationId, mode:'ack', attempts: entry.attempts, latencyMs: Date.now()-entry.createdAt});
    }
    delete state.pendingMessages[correlationId];
    clearAckTimeout(correlationId);
  }
  function retrySend(correlationId){
    const record = state.messages.find(m=> m.correlationId===correlationId);
    if(!record) return;
    // Reset state
    record.failed=false; record.pending=true; if(!updateMessageDom(record)) renderMessages(); else finalizeMessageRender();
    postTelemetry('send.retry.manual',{cid:correlationId});
    internalSendMessage(record.content, /*bypassRateLimit*/ true, /*fromFlush*/ true, correlationId);
  }
  /**
   * Sends a chat message. Implements rate limiting & optimistic update.
   */
  function queueOutbound(text){
    // Cap queue to avoid unbounded growth
    const MAX_QUEUE = 50;
    if(state.outbox.length >= MAX_QUEUE){
      // Drop oldest to make space
      state.outbox.shift();
    }
    const cid = secureRandomId('c_', 12);
    const item = { text, cid };
    state.outbox.push(item);
    // Present a single optimistic UI entry tied to this correlation id
    ensureOptimisticMessage(text, cid);
    renderQueueBadge();
    persistOutbox();
  }
  function sendMessage(){
    const text=(els.messageInput && els.messageInput.value||'').trim(); if(!text) return;
    // If the browser reports offline, queue and exit early.
    if(state.isOffline){
      queueOutbound(text);
      postTelemetry('send.queue',{reason:'offline', size: state.outbox.length});
      if(els.messageInput) els.messageInput.value='';
      return;
    }
    // Require profile; if absent we distinguish between uncertain (awaiting probe/profile) vs confirmed unauthenticated.
    if(!state.profile){
      const now = Date.now();
  const withinGrace = isWithinAuthGrace();
  if(state.authStatus===AuthStatus.UNKNOWN || state.authStatus===AuthStatus.PROBING || withinGrace){
  queueOutbound(text);
        let reason = 'awaitingProfile';
        if(withinGrace) reason='authGrace';
        else if(state.loading) reason='loadingUI';
        else if(state.authStatus===AuthStatus.PROBING) reason='authProbing';
        postTelemetry('send.queue',{reason, size: state.outbox.length});
        if(els.messageInput) els.messageInput.value='';
        return;
      }
      // After grace window with confirmed unauth
      showError(window.i18n.SessionExpired || 'Session expired. Please refresh.');
      return;
    }
    // If a join is in progress (or scheduled) queue silently
    if(state.joinInProgress || state.pendingJoin){
      queueOutbound(text);
      postTelemetry('send.queue',{reason:'joinInProgress', size: state.outbox.length});
      if(els.messageInput) els.messageInput.value='';
      return;
    }
    // If not currently in a room, attempt (or re-attempt) auto join then queue
    if(!state.joinedRoom){
      // Attempt to trigger a join if rooms known
      const target = state.rooms && state.rooms.length ? (localStorage.getItem('lastRoom') && state.rooms.some(r=>r.name===localStorage.getItem('lastRoom')) ? localStorage.getItem('lastRoom') : state.rooms[0].name) : null;
      if(target){
        joinRoom(target);
      }
      queueOutbound(text);
      postTelemetry('send.queue',{reason:'noRoomYet', size: state.outbox.length});
      if(els.messageInput) els.messageInput.value='';
      return;
    }
    // If hub is not in a connected state (connecting/reconnecting/disconnected), queue for later
    try {
      const s = computeConnectionState();
      if(s !== 'connected'){
        queueOutbound(text);
        postTelemetry('send.queue',{reason:'hubNotConnected:'+s, size: state.outbox.length});
        if(els.messageInput) els.messageInput.value='';
        return;
      }
    } catch(_) { /* ignore and attempt normal path */ }
    // Normal path: create (or reuse) a single optimistic message and pass its cid down
    const cid = secureRandomId('c_', 12);
    ensureOptimisticMessage(text, cid);
    internalSendMessage(text, false, false, cid);
    if(els.messageInput) els.messageInput.value='';
  }
  // deleteMessage removed
  function logoutCleanup(){ state.rooms=[]; state.users=[]; state.messages=[]; state.profile=null; state.joinedRoom=null; renderAll(); setLoading(false); }

  // --------------- Auth Probe -----------
  function probeAuth(){
    state.authStatus = AuthStatus.PROBING;
    const startedAt = performance.now();
  // Introduce an initial grace period (allows hub getProfileInfo to arrive even if direct /api/auth/me is slow)
  state.authGraceUntil = Date.now() + 5000; // 5s grace
    // Schedule a grace finalization: if after grace we are still probing/unknown and no profile, mark unauth
    setTimeout(()=>{
  if(!state.profile && (state.authStatus===AuthStatus.PROBING || state.authStatus===AuthStatus.UNKNOWN) && !isWithinAuthGrace()){
        state.authStatus = AuthStatus.UNAUTHENTICATED;
        postTelemetry('auth.finalize.unauth',{});
        renderProfile();
      }
    }, 5200);
  const timeout=setTimeout(()=>{ log('warn','auth.timeout'); setLoading(false); postTelemetry('auth.probe.timeout',{}); },6000);
    return fetch('/api/auth/me',{credentials:'include'})
      .then(r=> r.ok? r.json(): null)
  .then(u=>{ clearTimeout(timeout); if(u&&u.userName){ state.profile={userName:u.userName, fullName:u.fullName, avatar:u.avatar}; state.authStatus = AuthStatus.AUTHENTICATED; postTelemetry('auth.probe.success',{durationMs: Math.round(performance.now()-startedAt)}); startHub(); flushOutbox('authProbe'); state.authGraceUntil=null; } else { setLoading(false); state.authStatus = AuthStatus.UNAUTHENTICATED; postTelemetry('auth.probe.unauth',{durationMs: Math.round(performance.now()-startedAt)}); /* allow UI to show unauth after grace */ } renderProfile(); })
      .catch(err=>{ clearTimeout(timeout); setLoading(false); const msg=(err&&err.message)||''; // classify transient errors (networkish or recoverable)
        const transient=/timeout|network|fetch|offline|temporar(?:y|ily)?|dns|refused/i.test(msg); if(transient){ // extend grace and retry once after short delay
        postTelemetry('auth.probe.errorTransient',{durationMs: Math.round(performance.now()-startedAt)});
        state.authGraceUntil = Date.now() + 5000; // extend
        setTimeout(()=>{ if(state.authStatus===AuthStatus.PROBING || state.authStatus===AuthStatus.UNKNOWN){ probeAuth(); } }, 1200);
      } else { state.authStatus = AuthStatus.UNAUTHENTICATED; postTelemetry('auth.probe.error',{durationMs: Math.round(performance.now()-startedAt)}); }
      })
      .finally(()=>{ /* no-op */ });
  }

  // --------------- UI Wiring ------------
  function wireUi(){ if(els.messageInput) els.messageInput.addEventListener('keypress',e=>{ if(e.key==='Enter') sendMessage(); }); const sendBtn=document.getElementById('btn-send-message'); if(sendBtn) sendBtn.addEventListener('click',e=>{ e.preventDefault(); sendMessage(); }); if(els.filterInput) els.filterInput.addEventListener('input',()=>{ state.filter=els.filterInput.value; renderUsers(); }); if(els.btnLogout) els.btnLogout.addEventListener('click',()=>{ fetch('/api/auth/logout',{method:'POST',credentials:'include'})
    .catch(()=>{/* ignore */})
    .finally(()=>{ try { if(hub && hub.stop) hub.stop(); } catch(_) {} logoutCleanup(); window.location.replace('/login?ReturnUrl=/chat'); }); }); }

  function showError(msg){ if(!els.errorAlert) return; const span=els.errorAlert.querySelector('span'); if(span) span.textContent=msg; els.errorAlert.classList.remove('d-none'); setTimeout(()=> els.errorAlert.classList.add('d-none'),5000); }

  // Expose hooks for OTP flow
  window.chatApp = window.chatApp || {};
  window.chatApp.onAuthenticated = function(){
    setLoading(true);
    state.authStatus = AuthStatus.PROBING;
    const startedAt = performance.now();
    let probeFinished = false;
    let gotUser = false;
    const maxAttempts = 6; // ~ (1 + 0.4 + 0.6 + 0.8 + 1.0 + 1.2)s stagger = ~5s worst case
    const baseDelay = 400;
    let attempt = 0;

    function attemptProbe(){
      attempt++;
      fetch('/api/auth/me',{credentials:'include'}).then(r=> r.ok? r.json(): null).then(u=>{
        if(u && u.userName){
            gotUser = true;
            state.profile = { userName:u.userName, fullName:u.fullName, avatar:u.avatar };
            state.authStatus = AuthStatus.AUTHENTICATED;
            renderProfile();
            startHub();
            flushOutbox('authRetry');
            if(state.loading) setLoading(false);
        } else if(attempt < maxAttempts){
          setTimeout(attemptProbe, baseDelay * attempt); // linear-ish backoff
        } else {
          // Give up; show UI even if unauth (shouldn't normally happen after verify)
          if(state.loading) setLoading(false);
          log('warn','auth.retry.exhausted');
          state.authStatus = AuthStatus.UNAUTHENTICATED;
        }
      }).catch(()=>{
        if(attempt < maxAttempts){
          setTimeout(attemptProbe, baseDelay * attempt);
        } else {
          if(state.loading) setLoading(false);
          log('error','auth.retry.failed');
          state.authStatus = AuthStatus.UNAUTHENTICATED;
        }
      }).finally(()=>{ if(attempt >= maxAttempts || gotUser) probeFinished = true; });
    }
    attemptProbe();

    // Safety fallback (keep previous behavior) to ensure spinner never persists >2s after auth if profile already loaded or probe cycle closed
    setTimeout(()=>{
      if(state.loading && (state.profile || probeFinished)){
        setLoading(false);
        log('warn','auth.safetyFallback',{elapsedMs: Math.round(performance.now()-startedAt)});
      }
    }, 2000);
  };
  window.chatApp.logoutCleanup = logoutCleanup;

  // Boot
  function loadOutbox(){
    try {
      const raw = sessionStorage.getItem('chat.outbox');
      if(raw){
        const arr = JSON.parse(raw);
        if(Array.isArray(arr)) {
          // Upgrade legacy string-based outbox to object form with stable cid and inject optimistic entries
          state.outbox = arr.slice(0,50).map(it=>{
            if(typeof it === 'string') return { text: it, cid: stableCidForText(it) };
            if(it && typeof it === 'object' && it.text){ return it; }
            return { text: String(it||''), cid: stableCidForText(String(it||'')) };
          });
        }
      }
    } catch(_) {}
    renderQueueBadge();
  }

  // Deterministic correlation ID for legacy string-based outbox entries
  function stableCidForText(text) {
    // Use a simple hash (FNV-1a) for short strings, base64-encode, prefix with 'c_'
    // This is synchronous and sufficient for deduplication
    let hash = 2166136261;
    for (let i = 0; i < text.length; i++) {
      hash ^= text.charCodeAt(i);
      hash += (hash << 1) + (hash << 4) + (hash << 7) + (hash << 8) + (hash << 24);
    }
    // Convert hash to base36 and pad/truncate to 12 chars
    let hstr = Math.abs(hash).toString(36).padStart(12, '0').slice(0,12);
    return 'c_' + hstr;
  }
  function persistOutbox(){
    try { sessionStorage.setItem('chat.outbox', JSON.stringify(state.outbox)); } catch(_) {}
  }
  /**
   * Attempts to flush queued outbound messages if we have both a profile and a joined room.
   * @param {string} phase telemetry phase label
   */
  function flushOutbox(phase){
    // Serialize concurrent flush attempts
    if(!flushOutbox._queue) flushOutbox._queue=[];
    if(flushOutbox._inProgress){
      flushOutbox._queue.push(phase);
      return;
    }
    // Preconditions
    if(!state.outbox.length){ return; }
    // Skip while offline (avoid futile send attempts). Use skipFlags throttling to avoid telemetry spam.
    if(state.isOffline){
      const key='flushSkip:offline:'+phase;
      if(!flushOutbox._skipFlags) flushOutbox._skipFlags={};
      if(!flushOutbox._skipFlags[key]){ flushOutbox._skipFlags[key]=Date.now(); postTelemetry('send.flush.skip',{reason:'offline', phase, qlen: state.outbox.length}); }
      return;
    }
    if(!state.profile || !state.joinedRoom){
      const prereq = !state.profile ? (!state.joinedRoom ? 'noProfile_noRoom' : 'noProfile') : 'noRoom';
      const key = 'flushSkip:'+prereq+':'+phase;
      if(!flushOutbox._skipFlags) flushOutbox._skipFlags = {};
      if(!flushOutbox._skipFlags[key]){
        flushOutbox._skipFlags[key] = Date.now();
        postTelemetry('send.flush.skip',{reason:prereq, phase, qlen: state.outbox.length});
      }
      return;
    }
    flushOutbox._inProgress = true;
    const batchSize = 10; // send in manageable batches to avoid burst
    const total = state.outbox.length;
    const flushStart = performance.now();
    postTelemetry('send.flush.start',{room: state.joinedRoom && state.joinedRoom.name, count: total, phase});
    let success=0, failed=0;
    const toSend = state.outbox.splice(0, total); // drain
    renderQueueBadge(); persistOutbox();

    function sendNextBatch(){
      if(!toSend.length) return Promise.resolve();
      const slice = toSend.splice(0,batchSize);
      const results = [];
      return Promise.all(slice.map(item=>{
        const {text, cid} = (typeof item === 'string') ? {text: item, cid: null} : item;
        if(cid) ensureOptimisticMessage(text, cid);
        return internalSendMessage(text, /*bypassRateLimit*/ true, /*fromFlush*/ true, cid || undefined)
          .then(()=>{ success++; results.push({ok:true}); })
          .catch(()=>{ failed++; results.push({ok:false, item}); });
      })).then(()=>{
        // Requeue failed from this batch immediately to preserve order locality (FIFO semantics with retry tail)
        const failedItems = results.filter(r=>!r.ok).map(r=> r.item);
        if(failedItems.length){
          failedItems.forEach(it=> state.outbox.push(it));
          persistOutbox();
          postTelemetry('send.flush.batchFail',{failed: failedItems.length, phase});
        }
        return sendNextBatch();
      });
    }
    sendNextBatch().finally(()=>{
      if(failed){
        postTelemetry('send.flush.partialFail',{failed, success, remaining: state.outbox.length, phase});
      }
      postTelemetry('send.flush.done',{room: state.joinedRoom && state.joinedRoom.name, count: total, success, failed, durationMs: Math.round(performance.now()-flushStart), phase});
      flushOutbox._inProgress = false;
      // Process any queued phases (collapse duplicates)
      if(flushOutbox._queue.length){
        const nextPhase = flushOutbox._queue.pop(); // take latest
        flushOutbox._queue.length = 0;
        // Small microtask delay to allow state changes (e.g., room join finishing)
        setTimeout(()=> flushOutbox(nextPhase), 0);
      }
    });
  }
  // Periodic fallback: attempt to flush every 1500ms if prerequisites satisfied & outbox still non-empty.
  setInterval(()=>{
    try {
      if(state.outbox.length){ flushOutbox('periodic'); }
      // Repair UI badge if it became stale (e.g., DOM mutated / replaced)
      if(state.outbox.length===0 && els.queueBadge && els.queueBadge.textContent !== '0' && !els.queueBadge.classList.contains('d-none')){
        els.queueBadge.textContent = '0';
        els.queueBadge.classList.add('d-none');
      }
    } catch(_){ /* swallow */ }
  }, 1500);
  // Flush when tab becomes visible again (covers background tab timing misses)
  document.addEventListener('visibilitychange', ()=>{ if(!document.hidden){ flushOutbox('visibility'); } });
  document.addEventListener('visibilitychange', ()=>{ if(!document.hidden){ maybeMarkRead(); scheduleMarkVisibleRead(); ensureConnected(); } });
  window.addEventListener('focus', ()=>{ maybeMarkRead(); scheduleMarkVisibleRead(); ensureConnected(); });
  document.addEventListener('DOMContentLoaded',()=>{ 
    cacheDom(); 
    // No offline banner popup - connection state is shown via header color only
    loadOutbox(); 
    setLoading(true); 
    probeAuth(); 
    wireUi(); 
    startConnectionStateLoop(); 
    installOfflineHandlers();
    // Capture initial title for blinking restoration
    initTitleBlink();
  });
  
  // ---------------- Offline Handling ----------------
  function installOfflineHandlers(){
    function updateOffline(on){
      state.isOffline = on;
      // Don't show offline banner popup - connection state is shown via header color
      // if(els.offlineBanner){ els.offlineBanner.classList.toggle('d-none', !on); }
      if(on){
        postTelemetry('net.offline',{});
      } else {
        postTelemetry('net.online',{});
        // After coming online try to flush any queued messages (slight delay so hub reconnect can settle)
        setTimeout(()=>{ ensureConnected(); if(state.outbox.length) flushOutbox('online'); }, 150);
      }
      applyConnectionVisual(computeConnectionState());
    }
    if(typeof navigator !== 'undefined' && 'onLine' in navigator){
      updateOffline(!navigator.onLine);
    }
    window.addEventListener('offline', ()=> updateOffline(true));
    window.addEventListener('online', ()=> updateOffline(false));
  }
  
  // ---------------- Title Blinking ----------------
  let _titleBlink = { base:null, timer:null, active:false, counter:0, lastLabel:'' };
  function initTitleBlink(){
    try { _titleBlink.base = document.title || 'Chat'; } catch(_) { _titleBlink.base = 'Chat'; }
  }
  function startTitleBlink(){
    if(!_titleBlink.base) initTitleBlink();
    // Do not mutate counter here; label is derived from state.unreadCount via updateTitleBlinkLabel
    if(_titleBlink.active){
      // If already blinking, update label; next tick will pick it up
      return;
    }
    _titleBlink.active = true;
    let showAlt = true;
    _titleBlink.timer = setInterval(()=>{
      // In heavily throttled tabs this may run infrequently, which is acceptable
      try { document.title = showAlt ? _titleBlink.lastLabel : _titleBlink.base; } catch(_) {}
      showAlt = !showAlt;
    }, 1000);
  }
  function updateTitleBlinkLabel(roomLabel){
    const count = state.unreadCount || 0;
    const label = (count > 0 ? '('+count+') ' : '') + 'New message' + (roomLabel? ' • '+roomLabel : '');
    _titleBlink.lastLabel = label;
    // If not currently blinking but there are unread messages, ensure blinking starts
    if(count > 0 && !_titleBlink.active){ startTitleBlink(); }
  }
  function stopTitleBlink(){
    if(_titleBlink.timer){ try { clearInterval(_titleBlink.timer); } catch(_) {} _titleBlink.timer=null; }
    _titleBlink.active=false;
    // Removed obsolete _titleBlink.counter reset; unread count is managed by state.unreadCount
    try { if(_titleBlink.base) document.title = _titleBlink.base; } catch(_) {}
  }
  function isAtBottom(){
    const mc = document.querySelector('.messages-container');
    if(!mc) return false;
    const tolerance = 8; // px
    return (mc.scrollHeight - mc.scrollTop - mc.clientHeight) <= tolerance;
  }
  function isReadingView(){
    // Consider the message read if: tab visible, window focused, in a joined room, and scrolled to bottom
    try { return !document.hidden && !!state.joinedRoom && window.document.hasFocus() && isAtBottom(); } catch(_) { return false; }
  }
  function isActivelyViewing(){
    // User is actively viewing (regardless of scroll position): tab visible, window focused, in a joined room
    try { return !document.hidden && !!state.joinedRoom && window.document.hasFocus(); } catch(_) { return false; }
  }
  // ---------- Viewport-based mark-as-read (plan A) ----------
  // Keeps a session-level cache of message IDs already sent for read-marking to avoid duplicate invokes
  const _readMarkCache = new Set();
  function getSelfUserLower(){ return (state.profile && state.profile.userName || '').toLowerCase(); }
  function collectVisibleMessageIds(){
    try {
      const mc = document.querySelector('.messages-container');
      if(!mc || !els.messagesList) return [];
      const rect = mc.getBoundingClientRect();
      const items = Array.from(els.messagesList.children || []);
      const ids = [];
      const self = getSelfUserLower();
      for(const li of items){
        if(!li || !li.getBoundingClientRect) continue;
        const r = li.getBoundingClientRect();
        // Consider visible if it intersects container viewport by at least 12px
        const overlap = Math.min(rect.bottom, r.bottom) - Math.max(rect.top, r.top);
        if(overlap > 12){
          const idStr = li.dataset && li.dataset.id;
          const id = idStr ? parseInt(idStr, 10) : NaN;
          if(!Number.isFinite(id)) continue;
          // Consult state for message meta: skip my own messages and those already read by me
          const m = state.messages.find(x=> x && x.id===id);
          if(!m || m.isMine) continue;
          const readers = Array.isArray(m.readBy) ? m.readBy : [];
          if(readers.some(u => (u||'').toLowerCase() === self)) continue;
          ids.push(id);
        }
      }
      // Deduplicate and cap per pass to a reasonable number
      const unique = Array.from(new Set(ids));
      return unique.slice(0, 50);
    } catch(_) { return []; }
  }
  function markVisibleNow(){
    try {
      // Only mark as read if user is actively viewing: tab visible + window focused (same as title blink conditions)
      if(!isActivelyViewing()) return;
      if(!hub || !state.joinedRoom) return;
      const ids = collectVisibleMessageIds().filter(id => !_readMarkCache.has(id));
      if(!ids.length) return;
      // Optimistically add to cache to avoid duplicate invokes from rapid events
      ids.forEach(id => _readMarkCache.add(id));
      // Send individual invokes (server supports single-item MarkRead); keep lightweight and fire-and-forget
      ids.forEach(id => { try { hub.invoke('MarkRead', id); } catch(_){} });
    } catch(_) { /* ignore */ }
  }
  function debounce(fn, wait){ let t; return function(){ const args=arguments; clearTimeout(t); t=setTimeout(()=> fn.apply(null,args), wait); }; }
  const scheduleMarkVisibleRead = debounce(markVisibleNow, 300);
  function maybeMarkRead(){
    if(state.unreadCount > 0 && isReadingView()){
      state.unreadCount = 0;
      stopTitleBlink();
      // Mark visible messages as read using viewport-based detection
      scheduleMarkVisibleRead();
    } else if(state.unreadCount > 0){
      // Keep label up-to-date (e.g., switched rooms)
      updateTitleBlinkLabel(state.joinedRoom && state.joinedRoom.name || '');
    }
  }

  // Connectivity watchdog: ensure we attempt reconnection periodically if fully disconnected
  setInterval(()=>{
    try {
      if(hub){
        const s = hub.state && hub.state.toLowerCase ? hub.state.toLowerCase() : '';
        if(s==='disconnected') ensureConnected();
      }
    } catch(_) { /* ignore */ }
  }, 10000);
  // Fail-safe: if something throws during early init, show the app with a generic error.
  window.addEventListener('error', function(){
    if(state.loading){ setLoading(false); }
  });
  window.addEventListener('unhandledrejection', function(){
    if(state.loading){ setLoading(false); }
  });
})();
