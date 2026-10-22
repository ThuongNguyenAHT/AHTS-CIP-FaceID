(() => {
    'use strict';

    const elements = {
        waiting: document.getElementById('waitingView'),
        consent: document.getElementById('consentView'),
        complete: document.getElementById('completeView'),
        passengerName: document.getElementById('passengerName'),
        flightCode: document.getElementById('flightCode'),
        policyVersion: document.getElementById('policyVersion'),
        policyLanguage: document.getElementById('policyLanguage'),
        policyTitle: document.getElementById('consentTitle'),
        policyContent: document.getElementById('policyContent'),
        accept: document.getElementById('acceptButton'),
        decline: document.getElementById('declineButton'),
        error: document.getElementById('decisionError'),
        connection: document.getElementById('connectionStatus')
    };

    let currentRequestId = null;
    let loading = false;
    let submitting = false;
    let fallbackInterval = 1000;
    let reconnectTimer = null;

    function showView(name) {
        elements.waiting.classList.toggle('hidden', name !== 'waiting');
        elements.consent.classList.toggle('hidden', name !== 'consent');
        elements.complete.classList.toggle('hidden', name !== 'complete');
    }

    function setConnection(state, text) {
        elements.connection.className = `connection-pill is-${state}`;
        elements.connection.querySelector('span:last-child').textContent = text;
    }

    function renderConsent(data) {
        currentRequestId = data.id;
        elements.passengerName.textContent = data.passengerName;
        elements.flightCode.textContent = data.flightCode;
        elements.policyVersion.textContent = `v${data.policyVersion}`;
        elements.policyLanguage.textContent = data.languageCode;
        elements.policyTitle.textContent = data.policyTitle;
        elements.policyContent.textContent = data.policyContent;
        elements.error.classList.add('hidden');
        elements.policyContent.scrollTop = 0;
        showView('consent');
    }

    async function loadCurrent() {
        if (loading || submitting) return;
        loading = true;

        try {
            const response = await fetch('/api/tablet/current', {
                cache: 'no-store',
                headers: { 'Accept': 'application/json' }
            });

            if (response.status === 204) {
                currentRequestId = null;
                showView('waiting');
                return;
            }

            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();
            if (data.id !== currentRequestId) renderConsent(data);
        } catch (error) {
            console.error('Unable to load current consent request.', error);
            setConnection('offline', 'Mất kết nối');
        } finally {
            loading = false;
        }
    }

    async function submitDecision(decision) {
        if (!currentRequestId || submitting) return;
        submitting = true;
        elements.accept.disabled = true;
        elements.decline.disabled = true;
        elements.error.classList.add('hidden');

        try {
            const response = await fetch(`/api/tablet/requests/${currentRequestId}/decision`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ decision })
            });

            if (!response.ok) {
                const problem = await response.json().catch(() => null);
                throw new Error(problem?.message || 'Không thể ghi nhận lựa chọn.');
            }

            currentRequestId = null;
            showView('complete');
            window.setTimeout(loadCurrent, 2200);
        } catch (error) {
            elements.error.textContent = error.message || 'Không thể ghi nhận lựa chọn. Vui lòng thử lại.';
            elements.error.classList.remove('hidden');
        } finally {
            submitting = false;
            elements.accept.disabled = false;
            elements.decline.disabled = false;
        }
    }

    async function connectSignalR() {
        window.clearTimeout(reconnectTimer);
        setConnection('connecting', 'Đang kết nối');

        try {
            const negotiateResponse = await fetch('/hubs/tablet/negotiate?negotiateVersion=1', {
                method: 'POST',
                headers: { 'Content-Type': 'text/plain;charset=UTF-8' }
            });
            if (!negotiateResponse.ok) throw new Error('SignalR negotiate failed.');

            const negotiation = await negotiateResponse.json();
            const token = negotiation.connectionToken;
            const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
            const socket = new WebSocket(`${protocol}//${location.host}/hubs/tablet?id=${encodeURIComponent(token)}`);

            socket.onopen = () => {
                socket.send('{"protocol":"json","version":1}\u001e');
                setConnection('online', 'Đã kết nối');
                loadCurrent();
            };

            socket.onmessage = event => {
                const messages = String(event.data).split('\u001e').filter(Boolean);
                for (const rawMessage of messages) {
                    let message;
                    try { message = JSON.parse(rawMessage); } catch { continue; }
                    if (message.type === 1 && message.target === 'CustomerAvailable') loadCurrent();
                }
            };

            socket.onerror = () => socket.close();
            socket.onclose = () => {
                setConnection('offline', 'Đang kết nối lại');
                reconnectTimer = window.setTimeout(connectSignalR, 2000);
            };
        } catch (error) {
            console.warn('SignalR unavailable; fallback polling remains active.', error);
            setConnection('offline', 'Đang kết nối lại');
            reconnectTimer = window.setTimeout(connectSignalR, 3000);
        }
    }

    async function initialize() {
        try {
            const response = await fetch('/api/tablet/config', { cache: 'no-store' });
            if (response.ok) {
                const config = await response.json();
                fallbackInterval = config.fallbackPollingMilliseconds || fallbackInterval;
            }
        } catch {
            // Continue with the safe local default.
        }

        elements.accept.addEventListener('click', () => submitDecision('Accepted'));
        elements.decline.addEventListener('click', () => submitDecision('Declined'));
        await loadCurrent();
        connectSignalR();
        window.setInterval(loadCurrent, fallbackInterval);
    }

    initialize();
})();
