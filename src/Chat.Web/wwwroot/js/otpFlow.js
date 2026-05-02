export function postJson(url, data, options = {}) {
  const { headers, ...fetchOptions } = options;

  return fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      ...(headers || {})
    },
    body: JSON.stringify(data || {}),
    ...fetchOptions
  });
}

export function setOtpError(msg) {
  const el = document.getElementById('otpError');
  if (!el) {
    return;
  }

  if (msg) {
    el.textContent = msg;
    el.classList.remove('d-none');
    return;
  }

  el.textContent = '';
  el.classList.add('d-none');
}

export function ensureOtpFlow() {
  globalThis.__otpFlow = globalThis.__otpFlow || {};
  return globalThis.__otpFlow;
}

export function parseJsonWhenOk(response, errorMessage) {
  if (!response.ok) {
    throw new Error(errorMessage);
  }

  return response.json().catch(() => ({}));
}

export function resetOtpIndicator(indicator, retryLink, retryCountdown) {
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

export function scheduleOtpCountdown(flow, countdownEl, countdownTotalMs) {
  let remainingMs = countdownTotalMs;
  if (countdownEl) {
    countdownEl.textContent = Math.ceil(remainingMs / 1000);
  }

  if (flow.countdownInterval) {
    clearInterval(flow.countdownInterval);
  }

  flow.countdownInterval = setInterval(() => {
    remainingMs -= 1000;
    if (remainingMs <= 0) {
      if (countdownEl) {
        countdownEl.textContent = '0';
      }

      clearInterval(flow.countdownInterval);
      flow.countdownInterval = null;
      return;
    }

    if (countdownEl) {
      countdownEl.textContent = Math.ceil(remainingMs / 1000);
    }
  }, 1000);
}

export function scheduleRetryCooldown(flow, retryLink, retryCountdown, retryCooldownMs) {
  if (!retryLink) {
    return;
  }

  let left = retryCooldownMs;
  retryLink.classList.add('disabled-link');
  retryLink.dataset.cooldownLeft = left.toString();
  if (retryCountdown) {
    retryCountdown.textContent = '(' + Math.ceil(left / 1000) + 's)';
  }

  if (flow.retryInterval) {
    clearInterval(flow.retryInterval);
  }

  flow.retryInterval = setInterval(() => {
    left -= 1000;
    if (left <= 0) {
      clearInterval(flow.retryInterval);
      flow.retryInterval = null;
      retryLink.classList.remove('disabled-link');
      retryLink.dataset.cooldownLeft = '0';
      if (retryCountdown) {
        retryCountdown.textContent = '';
      }
      return;
    }

    retryLink.dataset.cooldownLeft = left.toString();
    if (retryCountdown) {
      retryCountdown.textContent = '(' + Math.ceil(left / 1000) + 's)';
    }
  }, 1000);
}

export function clearOtpTimers(flow, options = {}) {
  if (flow.startTimeout) {
    clearTimeout(flow.startTimeout);
    flow.startTimeout = null;
  }

  if (flow.countdownInterval) {
    clearInterval(flow.countdownInterval);
    flow.countdownInterval = null;
  }

  if (options.retryInterval && flow.retryInterval) {
    clearInterval(flow.retryInterval);
    flow.retryInterval = null;
  }

  if (options.resendAvailInterval && flow.resendAvailInterval) {
    clearInterval(flow.resendAvailInterval);
    flow.resendAvailInterval = null;
  }
}

export function showOtpSuccessIndicator(indicator) {
  if (!indicator) {
    return;
  }

  indicator.querySelectorAll('[data-state]').forEach(node => node.classList.add('d-none'));
  indicator.querySelector('[data-state="success"]')?.classList.remove('d-none');
  setTimeout(() => indicator.classList.add('fade-hidden'), 2000);
  setTimeout(() => indicator.classList.add('d-none'), 2500);
}

export function showOtpErrorIndicator(indicator) {
  if (!indicator) {
    return;
  }

  indicator.querySelectorAll('[data-state]').forEach(node => node.classList.add('d-none'));
  indicator.querySelector('[data-state="error"]')?.classList.remove('d-none');
}