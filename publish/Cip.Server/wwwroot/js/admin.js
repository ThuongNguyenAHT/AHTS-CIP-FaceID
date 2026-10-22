(() => {
    'use strict';

    const state = {
        passengers: [],
        selectedId: null,
        currentPage: 1,
        pageSize: 20
    };

    const statusLabels = {
        NotSent: 'Chưa hiển thị',
        Pending: 'Đang chờ xác nhận',
        Accepted: 'Đã đồng ý',
        Declined: 'Đã từ chối'
    };

    const byId = id => document.getElementById(id);
    const elements = {
        user: byId('currentUser'),
        refresh: byId('refreshButton'),
        logout: byId('logoutButton'),
        search: byId('searchInput'),
        status: byId('statusFilter'),
        rows: byId('passengerRows'),
        empty: byId('emptyState'),
        message: byId('adminMessage'),
        display: byId('displayButton'),
        selectedSummary: byId('selectedSummary'),
        rowSummary: byId('rowSummary'),
        pageSummary: byId('pageSummary'),
        previousPage: byId('previousPageButton'),
        nextPage: byId('nextPageButton'),
        tableWrap: byId('passengerTableWrap'),
        updated: byId('lastUpdated')
    };

    const dateFormatter = new Intl.DateTimeFormat('vi-VN', {
        dateStyle: 'short', timeStyle: 'short'
    });

    function parseServerDate(value) {
        if (!value) return null;
        return new Date(value.endsWith('Z') || /[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
    }

    function formatDate(value) {
        const date = parseServerDate(value);
        return date ? dateFormatter.format(date) : '—';
    }

    function createCell(text, className) {
        const cell = document.createElement('td');
        cell.textContent = text || '—';
        if (className) cell.className = className;
        return cell;
    }

    function createStackedCell(primaryText, secondaryText, className) {
        const cell = document.createElement('td');
        if (className) cell.className = className;

        const primary = document.createElement('span');
        primary.className = 'cell-primary';
        primary.textContent = primaryText || '—';
        cell.append(primary);

        if (secondaryText) {
            const secondary = document.createElement('span');
            secondary.className = 'cell-secondary';
            secondary.textContent = secondaryText;
            cell.append(secondary);
        }

        return cell;
    }

    function showMessage(message, type = 'success') {
        elements.message.textContent = message;
        elements.message.className = `admin-message is-${type}`;
    }

    function clearMessage() {
        elements.message.className = 'admin-message hidden';
        elements.message.textContent = '';
    }

    function selectPassenger(id) {
        state.selectedId = id;
        const passenger = state.passengers.find(item => item.id === id);
        elements.display.disabled = !passenger || !['NotSent', 'Pending'].includes(passenger.consentStatus);
        elements.selectedSummary.textContent = passenger
            ? `${passenger.passengerName} · ${passenger.flightCode}`
            : 'Chưa chọn hành khách';

        document.querySelectorAll('input[name="passengerSelection"]').forEach(input => {
            input.checked = Number(input.value) === id;
            input.closest('tr')?.classList.toggle('selected-row', input.checked);
        });
    }

    function filteredPassengers() {
        const query = elements.search.value.trim().toLocaleLowerCase('vi');
        const status = elements.status.value;

        return state.passengers.filter(passenger => {
            if (status && passenger.consentStatus !== status) return false;
            if (!query) return true;

            return [
                passenger.passengerName,
                passenger.flightCode,
                passenger.workstationCode,
                passenger.warehouseName,
                passenger.userName,
                passenger.fullName,
                String(passenger.id)
            ].some(value => value?.toLocaleLowerCase('vi').includes(query));
        });
    }

    function renderRows() {
        const passengers = filteredPassengers();
        const totalPages = Math.max(1, Math.ceil(passengers.length / state.pageSize));
        state.currentPage = Math.min(Math.max(state.currentPage, 1), totalPages);
        const startIndex = (state.currentPage - 1) * state.pageSize;
        const pagePassengers = passengers.slice(startIndex, startIndex + state.pageSize);

        elements.rows.replaceChildren();
        elements.empty.classList.toggle('hidden', passengers.length !== 0);

        for (const passenger of pagePassengers) {
            const selectable = ['NotSent', 'Pending'].includes(passenger.consentStatus);
            const row = document.createElement('tr');
            row.dataset.id = passenger.id;

            const selectCell = document.createElement('td');
            selectCell.className = 'select-column';
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.name = 'passengerSelection';
            checkbox.value = passenger.id;
            checkbox.disabled = !selectable;
            checkbox.checked = state.selectedId === passenger.id;
            checkbox.setAttribute('aria-label', `Chọn ${passenger.passengerName}`);
            checkbox.addEventListener('change', () => {
                selectPassenger(checkbox.checked ? passenger.id : null);
            });
            selectCell.append(checkbox);
            row.append(selectCell);

            row.append(createCell(String(passenger.id), 'id-cell'));
            row.append(createCell(passenger.passengerName, 'passenger-cell'));
            row.append(createStackedCell(
                passenger.flightCode,
                [passenger.operatingCarrierDesignator && `Hãng ${passenger.operatingCarrierDesignator}`,
                    passenger.flightNumber && `Số ${passenger.flightNumber}`]
                    .filter(Boolean)
                    .join(' · '),
                'flight-cell'));
            row.append(createCell(formatDate(passenger.dateOfFlight)));
            row.append(createStackedCell(
                passenger.workstationCode,
                passenger.warehouseName,
                'location-cell'));
            row.append(createCell(passenger.fullName));

            const statusCell = document.createElement('td');
            const badge = document.createElement('span');
            badge.className = `status-badge status-${passenger.consentStatus.toLowerCase()}`;
            badge.textContent = statusLabels[passenger.consentStatus] || passenger.consentStatus;
            statusCell.append(badge);
            row.append(statusCell);
            row.append(createCell(formatDate(passenger.confirmedAt)));

            if (checkbox.checked) row.classList.add('selected-row');
            if (selectable) {
                row.classList.add('selectable-row');
                row.addEventListener('click', event => {
                    if (event.target !== checkbox) selectPassenger(passenger.id);
                });
            }

            elements.rows.append(row);
        }

        const firstVisible = passengers.length === 0 ? 0 : startIndex + 1;
        const lastVisible = Math.min(startIndex + state.pageSize, passengers.length);
        elements.rowSummary.textContent = passengers.length === state.passengers.length
            ? `Hiển thị ${firstVisible}–${lastVisible} / ${passengers.length} hành khách`
            : `Hiển thị ${firstVisible}–${lastVisible} / ${passengers.length} kết quả lọc (${state.passengers.length} tổng)`;
        elements.pageSummary.textContent = `Trang ${state.currentPage} / ${totalPages}`;
        elements.previousPage.disabled = state.currentPage <= 1;
        elements.nextPage.disabled = state.currentPage >= totalPages;
    }

    async function api(url, options) {
        const response = await fetch(url, { cache: 'no-store', ...options });
        if (response.status === 401) {
            window.location.replace('/admin/login');
            throw new Error('Phiên đăng nhập đã hết hạn.');
        }

        if (!response.ok) {
            const detail = await response.json().catch(() => null);
            throw new Error(detail?.message || detail?.detail || `Lỗi HTTP ${response.status}.`);
        }

        return response.status === 204 ? null : response.json();
    }

    async function loadDashboard() {
        const data = await api('/api/admin/dashboard');
        byId('metricTotal').textContent = data.total;
        byId('metricAccepted').textContent = data.accepted;
        byId('metricDeclined').textContent = data.declined;
        byId('metricPending').textContent = data.pending;
    }

    async function loadPassengers() {
        state.passengers = await api('/api/admin/source-passengers');
        state.currentPage = 1;
        if (!state.passengers.some(item => item.id === state.selectedId)) {
            selectPassenger(null);
        }
        renderRows();
    }

    async function refresh() {
        elements.refresh.disabled = true;
        clearMessage();
        try {
            await Promise.all([loadDashboard(), loadPassengers()]);
            elements.updated.textContent = `Tải dữ liệu lúc ${dateFormatter.format(new Date())}`;
        } catch (exception) {
            showMessage(exception.message, 'error');
        } finally {
            elements.refresh.disabled = false;
        }
    }

    async function displaySelectedPassenger() {
        if (state.selectedId === null) return;
        const selected = state.passengers.find(item => item.id === state.selectedId);
        elements.display.disabled = true;
        clearMessage();

        try {
            await api('/api/admin/display', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ sourcePassengerId: state.selectedId })
            });
            showMessage(`Đã gửi ${selected?.passengerName || 'hành khách'} lên tablet.`);
            state.selectedId = null;
            elements.selectedSummary.textContent = 'Chưa chọn hành khách';
            await Promise.all([loadDashboard(), loadPassengers()]);
        } catch (exception) {
            showMessage(exception.message, 'error');
            elements.display.disabled = false;
        }
    }

    async function initialize() {
        try {
            const me = await api('/api/auth/me');
            elements.user.textContent = `${me.username} · Lễ tân`;
        } catch {
            return;
        }

        elements.refresh.addEventListener('click', refresh);
        elements.search.addEventListener('input', () => {
            state.currentPage = 1;
            renderRows();
        });
        elements.status.addEventListener('change', () => {
            state.currentPage = 1;
            renderRows();
        });
        elements.previousPage.addEventListener('click', () => {
            if (state.currentPage <= 1) return;
            state.currentPage--;
            renderRows();
            elements.tableWrap.scrollIntoView({ block: 'start' });
        });
        elements.nextPage.addEventListener('click', () => {
            const totalPages = Math.max(1, Math.ceil(filteredPassengers().length / state.pageSize));
            if (state.currentPage >= totalPages) return;
            state.currentPage++;
            renderRows();
            elements.tableWrap.scrollIntoView({ block: 'start' });
        });
        elements.display.addEventListener('click', displaySelectedPassenger);
        elements.logout.addEventListener('click', async () => {
            await api('/api/auth/logout', { method: 'POST' });
            window.location.replace('/admin/login');
        });

        await refresh();
    }

    initialize();
})();
