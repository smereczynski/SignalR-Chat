
// SSO-first + email OTP fallback login logic for SignalR Chat
(function(){
  // Helpers
  const $ = sel => document.querySelector(sel);
  const on = (el, evt, h, opts) => el?.addEventListener(evt, h, opts||false);
  function postJson(url, data) {
    return fetch(url, { method:'POST', headers:{'Content-Type':'application/json'}, credentials:'same-origin', body: JSON.stringify(data||{}) });
  }
  function setOtpError(msg) {
    const el = document.getElementById('otpError');
    if (!el) return;
    if (msg) { el.textContent = msg; el.classList.remove('d-none'); }
    else { el.textContent = ''; el.classList.add('d-none'); }
  }
  function ensureOtpFlow() {
    globalThis.__otpFlow = globalThis.__otpFlow || {};
    return globalThis.__otpFlow;
  }
  function parseJsonWhenOk(response, errorMessage) {
    if (!response.ok) {
      throw new Error(errorMessage);
    }

    return response.json().catch(() => ({}));
  }
  function resetOtpIndicator(indicator, retryLink, retryCountdown) {
    if (indicator) {
      indicator.classList.remove('d-none', 'fade-hidden');
      indicator.querySelectorAll('[data-state]').forEach(node => node.classList.add('d-none'));
      indicator.querySelector('[data-state="sending"]')?.classList.remove('d-none');
    }

    if (retryLink) {
      retryLink.classList.add('disabled-link');
      retryLink.dataset.cooldownLeft = '0';
    }

    if (retryCountdown) {
      retryCountdown.textContent = '';
    }
  }
  function scheduleOtpCountdown(flow, countdownEl, countdownTotalMs) {
    let remainingMs = countdownTotalMs;
    if (countdownEl) countdownEl.textContent = Math.ceil(remainingMs / 1000);
    if (flow.countdownInterval) clearInterval(flow.countdownInterval);
    flow.countdownInterval = setInterval(() => {
      remainingMs -= 1000;
      if (remainingMs <= 0) {
        if (countdownEl) countdownEl.textContent = '0';
        clearInterval(flow.countdownInterval);
        flow.countdownInterval = null;
        return;
      }

      if (countdownEl) countdownEl.textContent = Math.ceil(remainingMs / 1000);
    }, 1000);
  }
  function scheduleRetryCooldown(flow, retryLink, retryCountdown, retryCooldownMs) {
    if (!retryLink) {
      return;
    }

    let left = retryCooldownMs;
    retryLink.classList.add('disabled-link');
    retryLink.dataset.cooldownLeft = left.toString();
    if (retryCountdown) retryCountdown.textContent = '(' + Math.ceil(left / 1000) + 's)';
    if (flow.retryInterval) clearInterval(flow.retryInterval);
    flow.retryInterval = setInterval(() => {
      left -= 1000;
      if (left <= 0) {
        clearInterval(flow.retryInterval);
        flow.retryInterval = null;
        retryLink.classList.remove('disabled-link');
        retryLink.dataset.cooldownLeft = '0';
        if (retryCountdown) retryCountdown.textContent = '';
        return;
      }

      retryLink.dataset.cooldownLeft = left.toString();
      if (retryCountdown) retryCountdown.textContent = '(' + Math.ceil(left / 1000) + 's)';
    }, 1000);
  }
  function clearOtpTimers(flow) {
    if (flow.startTimeout) {
      clearTimeout(flow.startTimeout);
      flow.startTimeout = null;
    }

    if (flow.countdownInterval) {
      clearInterval(flow.countdownInterval);
      flow.countdownInterval = null;
    }
  }
  function showOtpSuccessIndicator(indicator) {
    if (!indicator) {
      return;
    }

    indicator.querySelectorAll('[data-state]').forEach(node => node.classList.add('d-none'));
    indicator.querySelector('[data-state="success"]')?.classList.remove('d-none');
    setTimeout(() => indicator.classList.add('fade-hidden'), 2000);
    setTimeout(() => indicator.classList.add('d-none'), 2500);
  }
  function showOtpErrorIndicator(indicator) {
    if (!indicator) {
      return;
    }

    indicator.querySelectorAll('[data-state]').forEach(node => node.classList.add('d-none'));
    indicator.querySelector('[data-state="error"]')?.classList.remove('d-none');
  }
  function handleOtpSendSuccess(flow, email, isResend, indicator) {
    flow.lastSendCompletedTs = performance.now();
    if (!isResend) {
      $('#otp-step1')?.classList.add('d-none');
      $('#otp-step2')?.classList.remove('d-none');
      document.getElementById('otpCode')?.focus();
    }

    showOtpSuccessIndicator(indicator);
  }
  function handleOtpSendFailure(flow, indicator, retryLink, retryCountdown, retryCooldownMs, errorMessage) {
    setOtpError(errorMessage || globalThis.i18n?.errorSendingCode || 'Error sending code');
    showOtpErrorIndicator(indicator);
    scheduleRetryCooldown(flow, retryLink, retryCountdown, retryCooldownMs);
  }
  function validateResendAttempt(flow, isResend, resendDelayMs) {
    if (!isResend) {
      flow.firstSendAt = Date.now();
      return true;
    }

    const sinceFirst = Date.now() - (flow.firstSendAt || 0);
    if (sinceFirst >= resendDelayMs) {
      return true;
    }

    const remainingSeconds = Math.ceil((resendDelayMs - sinceFirst) / 1000);
    const message = globalThis.i18n?.pleaseWaitSeconds?.replace('{0}', remainingSeconds) || `Please wait ${remainingSeconds} seconds before resending`;
    setOtpError(message);
    return false;
  }
  function setOtpButtonsDisabled(sendBtn, resendBtn, isResend) {
    if (!isResend && sendBtn) sendBtn.disabled = true;
    if (isResend && resendBtn) resendBtn.disabled = true;
  }
  function primeOtpSend(flow, email, controller, countdownEl, countdownTotalMs, rawTimeoutMs) {
    flow.startAbort?.abort();
    flow.startAbort = controller;
    flow.activeEmail = email;
    flow.lastSendStartTs = performance.now();
    flow.lastSendCompletedTs = null;

    scheduleOtpCountdown(flow, countdownEl, countdownTotalMs);
    if (flow.startTimeout) clearTimeout(flow.startTimeout);
    flow.startTimeout = setTimeout(() => {
      if (!controller.signal.aborted) controller.abort();
    }, rawTimeoutMs);
  }

  document.addEventListener('DOMContentLoaded', () => {
    // SSO/Microsoft login button logic
    const msLoginBtn = document.getElementById('btn-microsoft-login');
    on(msLoginBtn, 'click', (e) => {
      e.preventDefault();
      globalThis.location.href = '/login/entra';
    });

    // OTP fallback logic (email input, not dropdown)
    function startOtpSend(isResend){
      setOtpError(null);
      const email = (document.getElementById('otpEmail')?.value || '').trim();
      if (!email) { setOtpError(globalThis.i18n?.userNameRequired || 'User name is required'); return; }
      const flow = ensureOtpFlow();
      const container = document.getElementById('otpContainer');
      const resendDelayMs = Number.parseInt(container?.dataset.otpResendDelayMs || '300000', 10);
      if (!validateResendAttempt(flow, isResend, resendDelayMs)) return;
      const indicator = document.getElementById('otpSendingIndicator');
      const countdownEl = document.getElementById('otpSendCountdown');
      const retryLink = document.getElementById('otpRetryLink');
      const retryCountdown = document.getElementById('otpRetryCountdown');
      const resendBtn = document.getElementById('btn-resend-otp');
      const sendBtn = document.getElementById('btn-send-otp');
      setOtpButtonsDisabled(sendBtn, resendBtn, isResend);

      const rawTimeoutMs = Number.parseInt(container?.dataset.otpTimeoutMs || '29000', 10);
      const countdownTotalMs = 30000;
      const retryCooldownMs = Number.parseInt(container?.dataset.otpRetryCooldownMs || '5000', 10);

      resetOtpIndicator(indicator, retryLink, retryCountdown);

      const controller = new AbortController();
      primeOtpSend(flow, email, controller, countdownEl, countdownTotalMs, rawTimeoutMs);

      postJson('/api/auth/start', { userName: email })
        .then(r => parseJsonWhenOk(r, globalThis.i18n?.failedToSendCode || 'Failed to send code'))
        .then(() => {
          if (flow.activeEmail !== email || controller.signal.aborted) return;
          handleOtpSendSuccess(flow, email, isResend, indicator);
        })
        .catch(error_ => {
          const aborted = controller.signal.aborted;
          if (!aborted) {
            handleOtpSendFailure(flow, indicator, retryLink, retryCountdown, retryCooldownMs, error_.message);
          }
        })
        .finally(() => {
          if (sendBtn) sendBtn.disabled = false;
          if (resendBtn) resendBtn.disabled = false;
          clearOtpTimers(flow);
        });
    }

    function executeOtpVerify(){
      setOtpError(null);
      const flow = ensureOtpFlow(); if (flow.verifyInFlight) return;
      const email = (document.getElementById('otpEmail')?.value || '').trim();
      const code = (document.getElementById('otpCode')?.value || '').trim();
      if (!email || !code) { setOtpError(globalThis.i18n?.userNameAndCodeRequired || 'User name and code are required'); return; }
      const btn = document.getElementById('btn-verify-otp');
      if (btn) btn.disabled = true;
      flow.verifyInFlight = true;
      const returnUrl = (typeof globalThis.__returnUrl === 'string' ? globalThis.__returnUrl : '/chat');
      postJson('/api/auth/verify', { userName: email, code, returnUrl })
        .then(r => parseJsonWhenOk(r, globalThis.i18n?.invalidVerificationCode || 'Invalid code'))
        .then(body => {
          const next = body && typeof body.nextUrl === 'string' ? body.nextUrl : '/chat';
          if (next.startsWith('/') && !next.startsWith('//')) {
            globalThis.location.href = next;
          } else {
            globalThis.location.href = '/chat';
          }
        })
        .catch(error_ => setOtpError(error_.message || globalThis.i18n?.verificationFailed || 'Verification failed'))
        .finally(()=>{ flow.verifyInFlight = false; if (btn) btn.disabled = false; });
    }

    document.addEventListener('click', e => {
      if (e.target.closest('#btn-send-otp')) { startOtpSend(false); return; }
      if (e.target.closest('#btn-resend-otp')) { startOtpSend(true); return; }
      if (e.target.closest('#btn-verify-otp')) { executeOtpVerify(); }
    });

    const otpCodeInput = document.getElementById('otpCode');
    on(otpCodeInput, 'keydown', ev => { if (ev.key === 'Enter') { ev.preventDefault(); executeOtpVerify(); } });

    const otpEmailInput = document.getElementById('otpEmail');
    on(otpEmailInput, 'keydown', ev => { if (ev.key === 'Enter') { ev.preventDefault(); startOtpSend(false); } });
  });
})();
