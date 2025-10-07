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
  const state = { loading:true, profile:null, rooms:[], users:[], messages:[], joinedRoom:null, filter:'', oldestLoaded:null, canLoadMore:true, pageSize:20, loadingMore:false, lastSendAt:0, minSendIntervalMs:800, joinInProgress:false, pendingJoin:null, outbox:[], pendingAck:{}, authProbing:false };
  const els = {};

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
  els.usersCount = document.getElementById('users-count');
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
          els.joinedRoomTitle.textContent = state._baseRoomTitle + ' (RECONNECTING…)';
        }
        break;
      case 'disconnected':
        els.roomHeader.classList.add('connection-state-disconnected');
        if(els.joinedRoomTitle){
          const base = state._baseRoomTitle || els.joinedRoomTitle.textContent || '';
          // Use explicit variation selector for broader rendering + fallback triangle if some fonts strip emoji style
          const warn = '\u26A0\uFE0F'; // ⚠️
          els.joinedRoomTitle.textContent = base + ' (' + warn + ' DISCONNECTED)';
        }
        break;
    }
  }
  function computeConnectionState(){
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
  function renderUsers(){ if(!els.usersList) return; const term=state.filter.toLowerCase(); els.usersList.innerHTML=''; const filtered=state.users.filter(u=>!term|| (u.fullName||u.userName||'').toLowerCase().includes(term)); filtered.forEach(u=>{ const li=document.createElement('li'); li.dataset.username=u.userName; const wrap=document.createElement('div'); wrap.className='user'; if(!u.avatar){ const span=document.createElement('span'); span.className='avatar me-2 text-uppercase'; span.textContent=initialFrom(u.fullName, u.userName); wrap.appendChild(span);} else { const img=document.createElement('img'); img.className='avatar me-2'; img.src='/avatars/'+u.avatar; wrap.appendChild(img);} const info=document.createElement('div'); info.className='user-info'; const nameSpan=document.createElement('span'); nameSpan.className='name'; nameSpan.textContent=u.fullName || u.userName; info.appendChild(nameSpan); const devSpan=document.createElement('span'); devSpan.className='device'; devSpan.textContent=u.device; info.appendChild(devSpan); wrap.appendChild(info); li.appendChild(wrap); els.usersList.appendChild(li); }); if(els.usersCount) els.usersCount.textContent=filtered.length; }
  function formatDateParts(ts){ const date=new Date(ts); const now=new Date(); const diffDays=Math.round((date-now)/(1000*3600*24)); const day=date.getDate(); const month=date.getMonth()+1; const year=date.getFullYear(); let hours=date.getHours(); const minutes=('0'+date.getMinutes()).slice(-2); const ampm=hours>=12?'PM':'AM'; if(hours>12) hours=hours%12; const dateOnly=`${day}/${month}/${year}`; const timeOnly=`${hours}:${minutes} ${ampm}`; const full=`${dateOnly} ${timeOnly}`; let relative=dateOnly; if(diffDays===0) relative=`Today, ${timeOnly}`; else if(diffDays===-1) relative=`Yesterday, ${timeOnly}`; return {relative, full}; }
  function renderMessages(){ if(!els.messagesList) return; els.messagesList.innerHTML=''; state.messages.forEach(m=>{ const li=document.createElement('li'); const wrap=document.createElement('div'); wrap.className='message-item'; if(m.isMine) wrap.classList.add('ismine'); if(!m.avatar){ const span=document.createElement('span'); span.className='avatar avatar-lg mx-2 text-uppercase'; span.textContent=initialFrom(m.fromFullName, m.fromUserName); wrap.appendChild(span);} else { const img=document.createElement('img'); img.className='avatar avatar-lg mx-2'; img.src='/avatars/'+m.avatar; wrap.appendChild(img);} const content=document.createElement('div'); content.className='message-content'; const info=document.createElement('div'); info.className='message-info d-flex flex-wrap align-items-center'; const author=document.createElement('span'); author.className='author'; author.textContent=m.fromFullName||m.fromUserName; info.appendChild(author); const time=document.createElement('span'); time.className='timestamp'; const fp=formatDateParts(m.timestamp); time.textContent=fp.relative; time.dataset.bsTitle=fp.full; time.setAttribute('data-bs-toggle','tooltip'); info.appendChild(time); content.appendChild(info); const body=document.createElement('div'); body.className='content'; body.textContent=m.content; content.appendChild(body); wrap.appendChild(content); li.appendChild(wrap); els.messagesList.appendChild(li); }); const noInfo=document.querySelector('.no-messages-info'); if(noInfo) noInfo.classList.toggle('d-none', state.messages.length>0); const mc=document.querySelector('.messages-container'); if(mc && !state.loadingMore) mc.scrollTop=mc.scrollHeight; 
    // Re-init Bootstrap tooltips if available
    if(window.bootstrap){ const ttEls = [].slice.call(els.messagesList.querySelectorAll('[data-bs-toggle="tooltip"]')); ttEls.forEach(el=>{ try{ new window.bootstrap.Tooltip(el); }catch(_){} }); }
  }
  function renderProfile(){ const p=state.profile; if(!els.profileName) return; if(!p){ els.profileName.textContent='Not signed in'; if(els.profileAvatarImg) els.profileAvatarImg.classList.add('d-none'); if(els.profileAvatarInitial){ els.profileAvatarInitial.classList.remove('d-none'); els.profileAvatarInitial.textContent='?'; } if(els.btnLogin) els.btnLogin.classList.remove('d-none'); if(els.btnLogout) els.btnLogout.classList.add('d-none'); } else { els.profileName.textContent=p.fullName||p.userName; if(p.avatar){ if(els.profileAvatarImg){ els.profileAvatarImg.src='/avatars/'+p.avatar; els.profileAvatarImg.classList.remove('d-none'); } if(els.profileAvatarInitial) els.profileAvatarInitial.classList.add('d-none'); } else { if(els.profileAvatarInitial){ els.profileAvatarInitial.textContent=initialFrom(p.fullName, p.userName); els.profileAvatarInitial.classList.remove('d-none'); } if(els.profileAvatarImg) els.profileAvatarImg.classList.add('d-none'); } if(els.btnLogin) els.btnLogin.classList.add('d-none'); if(els.btnLogout) els.btnLogout.classList.remove('d-none'); } }

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

  // --------------- API ----------------
  const apiGet = url => fetch(url,{credentials:'include'}).then(r=> r.ok? r.json(): Promise.reject(r));
  const apiPost = (u,o)=> fetch(u,{method:'POST',headers:{'Content-Type':'application/json'},credentials:'include',body:JSON.stringify(o)});
  const apiPut = (u,o)=> fetch(u,{method:'PUT',headers:{'Content-Type':'application/json'},credentials:'include',body:JSON.stringify(o)});
  const apiDelete = u => fetch(u,{method:'DELETE',credentials:'include'});

  // --------------- SignalR --------------
  let hub=null, reconnectAttempts=0; const maxBackoff=30000;
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
  function postTelemetry(event, data){
    try {
      fetch('/api/telemetry/reconnect',{
        method:'POST',
        headers:{'Content-Type':'application/json'},
        body:JSON.stringify({evt:event, ts: Date.now(), sessionId, ...data})
      });
    } catch(_) { /* ignore */ }
  }
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
  function ensureHub(){ if(hub) return hub; hub=new signalR.HubConnectionBuilder().withUrl('/chatHub').withAutomaticReconnect().build(); wireHub(hub); return hub; }
  function startHub(){
    ensureHub();
    log('info','signalr.start');
  applyConnectionVisual('reconnecting'); // initial connecting state
    const startedAt = performance.now();
    return hub.start().then(()=>{
      reconnectAttempts=0; lastReconnectError=null; loadRooms();
      postTelemetry('hub.connected',{durationMs: Math.round(performance.now()-startedAt)});
  applyConnectionVisual('connected');
      // Fallback: if we already have a profile (from /api/auth/me) but still showing loading because getProfileInfo hasn't fired, hide loader.
      if(state.profile && state.loading){ setLoading(false); }
    }).catch(err=>{ log('error','signalr.start.fail',{error:err&&err.message}); postTelemetry('hub.connect.fail',{message: (err&&err.message)||''}); scheduleReconnect(err); });
  }
  /**
   * Applies exponential backoff and records telemetry for each reconnect attempt.
   * @param {Error} err the error that triggered scheduling
   */
  function scheduleReconnect(err){
    if(err) lastReconnectError=err;
    reconnectAttempts++;
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
    c.on('getProfileInfo', u=>{ state.profile={userName:u.userName, fullName:u.fullName, avatar:u.avatar}; setLoading(false); renderProfile(); });
    // Full presence snapshot after join (server push) ensures newly joined user and existing members converge
    c.on('presenceSnapshot', list=> { if(!Array.isArray(list)) return; // Replace users list for current room only
      if(state.joinedRoom){ state.users = list.filter(u=> u.currentRoom === state.joinedRoom.name || u.CurrentRoom === state.joinedRoom.name); }
      else { state.users = list; }
      renderUsers(); });
    c.on('newMessage', m=> {
      const mineUser = state.profile && state.profile.userName;
      if(mineUser){
        // Prefer correlationId reconciliation
        if(m.correlationId){
          const byCorr = state.messages.findIndex(x=> x.isMine && x.correlationId && x.correlationId===m.correlationId);
          if(byCorr>=0){
            state.messages[byCorr] = {id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine:true, correlationId:m.correlationId};
            renderMessages();
            return;
          }
        }
        // Fallback to content + negative id heuristic
        const pendingIdx = state.messages.findIndex(x=> x.isMine && x.id<0 && x.content===m.content);
        if(pendingIdx>=0){
          state.messages[pendingIdx] = {id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine:true, correlationId:m.correlationId};
          renderMessages();
          return;
        }
      }
      addOrRenderMessage({ ...m, correlationId: m.correlationId });
    });
    c.on('notify', n=> handleNotify(n));
    // Automatic reconnect handlers (withAutomaticReconnect is enabled on builder)
    // On successful re-connect, request a fresh user list for the current room.
    // This compensates for any missed join/leave events during the disconnected interval.
    c.onreconnected(connectionId => {
      try {
        if(state.joinedRoom){
          hub.invoke('GetUsers', state.joinedRoom.name).then(users=>{ state.users = users; renderUsers(); });
        }
      } catch(_){ }
  applyConnectionVisual('connected');
    });
  c.onreconnecting(err => { log('warn','hub.reconnecting', {message: err && err.message}); applyConnectionVisual('reconnecting'); });
  c.onclose(()=> { 
    applyConnectionVisual('disconnected'); 
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
  function upsertUser(u){ const e=state.users.find(x=>x.userName===u.userName); if(e) Object.assign(e,u); else state.users.push(u); renderUsers(); }
  function removeUser(userName){ state.users=state.users.filter(u=>u.userName!==userName); renderUsers(); }
  /**
   * Appends a message to local state and re-renders.
   * @param {object} m MessageViewModel-like payload
   */
  function addOrRenderMessage(m){ const isMine=state.profile && state.profile.userName===m.fromUserName; state.messages.push({id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine, correlationId:m.correlationId}); renderMessages(); }

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
  function joinRoom(roomName){
    if(!hub||!roomName) return;
    if(state.joinInProgress && state.pendingJoin===roomName) return; // already joining this room
    if(state.joinedRoom && state.joinedRoom.name===roomName && !state.joinInProgress) return; // already in room
    state.joinInProgress = true;
    state.pendingJoin = roomName;
    renderRooms(); // show joining state
    const thisAttempt = roomName;
    const startedAt = performance.now();
    hub.invoke('Join',roomName).then(()=>{
      // If another join was initiated meanwhile, ignore this result
      if(state.pendingJoin !== thisAttempt) return;
      state.joinedRoom = state.rooms.find(r=>r.name===roomName)||{name:roomName};
      state.pendingJoin = null; state.joinInProgress=false;
      localStorage.setItem('lastRoom',roomName);
      // Update base room title reference so connection annotations reflect the new room.
      if(els.joinedRoomTitle){
        state._baseRoomTitle = state.joinedRoom.name;
        els.joinedRoomTitle.textContent = state._baseRoomTitle;
      }
  loadUsers(); loadMessages(); renderRoomContext(); ensureProfileAvatar();
      postTelemetry('room.join.success',{room:roomName, durationMs: Math.round(performance.now()-startedAt)});
      // Flush any queued outbound messages collected while joining
      if(state.outbox && state.outbox.length){
        const queued = state.outbox.splice(0, state.outbox.length);
        renderQueueBadge(); persistOutbox();
        let success=0, failed=0; const retry=[];
        const flushStart = performance.now();
        queued.forEach(text=> {
          try {
            internalSendMessage(text, /*bypassRateLimit*/ true, /*fromFlush*/ true).then(()=>{success++;}).catch(()=>{failed++; retry.push(text);});
          } catch(_){ failed++; retry.push(text); }
        });
        // Simple async settle after a tick
        setTimeout(()=>{
          if(retry.length){
            state.outbox.unshift(...retry.slice(0,50)); // put back (cap to 50)
            renderQueueBadge(); persistOutbox();
            postTelemetry('send.flush.retry',{retry:retry.length, room: roomName});
          }
          postTelemetry('send.flush.done',{room:roomName,count:queued.length,success,failed,durationMs: Math.round(performance.now()-flushStart)});
        },50);
      }
    }).catch(err=> { if(state.pendingJoin===thisAttempt){ state.joinInProgress=false; state.pendingJoin=null; renderRooms(); } showError('Join failed: '+(err&&err.message)); });
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
  function loadUsers(){ if(!state.joinedRoom) return; hub.invoke('GetUsers', state.joinedRoom.name).then(users=>{ state.users=users; renderUsers(); }); }
  /**
   * Loads most recent page of messages for joined room (resets pagination state).
   */
  function loadMessages(){
    if(!state.joinedRoom) return;
    state.oldestLoaded=null; state.canLoadMore=true;
    apiGet('/api/Messages/Room/'+encodeURIComponent(state.joinedRoom.name)+'?take='+state.pageSize)
      .then(list=>{
        // Server returns ascending order (repository sorts ascending)
        state.messages = list.map(m=>({id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine: state.profile && state.profile.userName===m.fromUserName}));
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
  function loadOlderMessages(){
    if(!state.canLoadMore || state.loadingMore || !state.joinedRoom || !state.oldestLoaded) return;
    state.loadingMore = true;
    const before = encodeURIComponent(state.oldestLoaded);
    apiGet('/api/Messages/Room/'+encodeURIComponent(state.joinedRoom.name)+'?before='+before+'&take='+state.pageSize)
      .then(list=>{
        if(!Array.isArray(list) || list.length===0){ state.canLoadMore=false; return; }
        const mapped=list.map(m=>({id:m.id,content:m.content,timestamp:m.timestamp,fromUserName:m.fromUserName,fromFullName:m.fromFullName,avatar:m.avatar,isMine: state.profile && state.profile.userName===m.fromUserName}));
        const mc=document.querySelector('.messages-container'); let prevScrollHeight = mc? mc.scrollHeight:0;
        // Prepend
        state.messages = mapped.concat(state.messages);
        state.oldestLoaded = state.messages[0].timestamp;
        if(list.length < state.pageSize) state.canLoadMore=false;
        renderMessages();
        if(mc){ const newScrollHeight = mc.scrollHeight; mc.scrollTop = newScrollHeight - prevScrollHeight; }
      })
      .finally(()=>{ state.loadingMore=false; });
  }
  /**
   * One-time scroll listener that triggers pagination when scrolled near top.
   */
  function attachScrollPagination(){ const mc=document.querySelector('.messages-container'); if(!mc || mc.dataset.paginated) return; mc.dataset.paginated='true'; let lastReq=0; mc.addEventListener('scroll',()=>{ if(mc.scrollTop < 30){ const now=Date.now(); if(now-lastReq>400){ lastReq=now; loadOlderMessages(); } } }); }
  let pendingIdCounter=-1;
  function internalSendMessage(text, bypassRateLimit, fromFlush){
    const now=Date.now();
    if(!bypassRateLimit && (now - state.lastSendAt < state.minSendIntervalMs)){
      showError('You are sending messages too quickly');
      return Promise.reject('rateLimited');
    }
    state.lastSendAt = now;
    const tempId = pendingIdCounter--;
    const nowIso = new Date().toISOString();
  const correlationId = secureRandomId('c_', 12); // ~48 bits of randomness (12 nibbles) + timestamp
    state.messages.push({id:tempId,content:text,timestamp:nowIso,fromUserName:state.profile?.userName,fromFullName:state.profile?.fullName,avatar:state.profile?.avatar,isMine: !!state.profile, correlationId});
    renderMessages();
    if(!hub){ return Promise.reject('noHub'); }
    postTelemetry('send.attempt',{cid:correlationId, len:text.length, fromFlush:!!fromFlush});
    const p = hub.invoke('SendMessage', text, correlationId)
      .then(()=>{ postTelemetry('send.invoke.ok',{cid:correlationId}); })
      .catch(err=>{
        state.messages = state.messages.filter(m=>m.id!==tempId);
        renderMessages();
        postTelemetry('send.invoke.fail',{cid:correlationId, msg:(err&&err.message)||''});
        showError('Send failed');
        throw err;
      });
    return p;
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
    state.outbox.push(text);
    renderQueueBadge();
    persistOutbox();
  }
  function sendMessage(){
    const text=(els.messageInput && els.messageInput.value||'').trim(); if(!text) return;
    // Require profile; if absent, show explicit message
    if(!state.profile){
      // While loading spinner visible or auth probing, optimistically queue.
      if(state.loading || state.authProbing){
        queueOutbound(text);
        postTelemetry('send.queue',{reason: state.loading ? 'loadingUI' : 'authProbing', size: state.outbox.length});
        if(els.messageInput) els.messageInput.value='';
        return;
      }
      // If we reach here, probe finished and user truly unauthenticated.
      showError('Not signed in.');
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
    // Normal path
    internalSendMessage(text, false, false);
    if(els.messageInput) els.messageInput.value='';
  }
  // deleteMessage removed
  function logoutCleanup(){ state.rooms=[]; state.users=[]; state.messages=[]; state.profile=null; state.joinedRoom=null; renderAll(); setLoading(false); }

  // --------------- Auth Probe -----------
  function probeAuth(){
    state.authProbing = true;
    const startedAt = performance.now();
    const timeout=setTimeout(()=>{ log('warn','auth.timeout'); setLoading(false); postTelemetry('auth.probe.timeout',{}); },6000);
    return fetch('/api/auth/me',{credentials:'include'})
      .then(r=> r.ok? r.json(): null)
      .then(u=>{ clearTimeout(timeout); if(u&&u.userName){ state.profile={userName:u.userName, fullName:u.fullName, avatar:u.avatar}; postTelemetry('auth.probe.success',{durationMs: Math.round(performance.now()-startedAt)}); startHub(); } else { setLoading(false); postTelemetry('auth.probe.unauth',{durationMs: Math.round(performance.now()-startedAt)}); } renderProfile(); })
      .catch(()=>{ clearTimeout(timeout); setLoading(false); postTelemetry('auth.probe.error',{durationMs: Math.round(performance.now()-startedAt)}); })
      .finally(()=>{ state.authProbing = false; });
  }

  // --------------- UI Wiring ------------
  function wireUi(){ if(els.messageInput) els.messageInput.addEventListener('keypress',e=>{ if(e.key==='Enter') sendMessage(); }); const sendBtn=document.getElementById('btn-send-message'); if(sendBtn) sendBtn.addEventListener('click',e=>{ e.preventDefault(); sendMessage(); }); if(els.filterInput) els.filterInput.addEventListener('input',()=>{ state.filter=els.filterInput.value; renderUsers(); }); if(els.btnLogout) els.btnLogout.addEventListener('click',()=>{ fetch('/api/auth/logout',{method:'POST',credentials:'include'}).finally(()=>{ try { if(hub && hub.stop) hub.stop(); } catch(_) {} logoutCleanup(); }); }); }

  function showError(msg){ if(!els.errorAlert) return; const span=els.errorAlert.querySelector('span'); if(span) span.textContent=msg; els.errorAlert.classList.remove('d-none'); setTimeout(()=> els.errorAlert.classList.add('d-none'),5000); }

  // Expose hooks for OTP flow
  window.chatApp = window.chatApp || {};
  window.chatApp.onAuthenticated = function(){
    setLoading(true);
    state.authProbing = true;
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
          renderProfile();
          // Start hub (which will load rooms)
          startHub();
          // Reveal UI immediately now that we have profile
          if(state.loading) setLoading(false);
          state.authProbing = false;
        } else if(attempt < maxAttempts){
          setTimeout(attemptProbe, baseDelay * attempt); // linear-ish backoff
        } else {
          // Give up; show UI even if unauth (shouldn't normally happen after verify)
          if(state.loading) setLoading(false);
          log('warn','auth.retry.exhausted');
          state.authProbing = false;
        }
      }).catch(()=>{
        if(attempt < maxAttempts){
          setTimeout(attemptProbe, baseDelay * attempt);
        } else {
          if(state.loading) setLoading(false);
          log('error','auth.retry.failed');
          state.authProbing = false;
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
      if(raw){ const arr = JSON.parse(raw); if(Array.isArray(arr)) { state.outbox = arr.slice(0,50); } }
    } catch(_) {}
    renderQueueBadge();
  }
  function persistOutbox(){
    try { sessionStorage.setItem('chat.outbox', JSON.stringify(state.outbox)); } catch(_) {}
  }
  document.addEventListener('DOMContentLoaded',()=>{ cacheDom(); loadOutbox(); setLoading(true); probeAuth(); wireUi(); startConnectionStateLoop(); });
  // Fail-safe: if something throws during early init, show the app with a generic error.
  window.addEventListener('error', function(){
    if(state.loading){ setLoading(false); }
  });
  window.addEventListener('unhandledrejection', function(){
    if(state.loading){ setLoading(false); }
  });
})();
