    (function () {
        'use strict';

        function ensureStyles() {
            if (document.getElementById('JellySyncDashboardStyles')) {
                return;
            }

            const style = document.createElement('style');
            style.id = 'JellySyncDashboardStyles';
            style.textContent = `
                #JellySyncConfigPage .jellySyncSourceUser { max-width: 28rem; }
            `;
            document.head.append(style);
        }

        function initializePage() {
        const pluginId = 'c7559c65-6673-48fe-a134-97f098adc315';
        const page = document.getElementById('JellySyncConfigPage');
        if (!page || page.dataset.jellysyncInitialized === 'true') {
            return;
        }

        page.dataset.jellysyncInitialized = 'true';
        const form = document.getElementById('JellySyncConfigForm');
        const enabled = document.getElementById('JellySyncEnabled');
        const allLibraries = document.getElementById('JellySyncAllLibraries');
        const usersContainer = document.getElementById('JellySyncUsers');
        const librariesContainer = document.getElementById('JellySyncLibraries');
        const sourceSelect = document.getElementById('JellySyncSourceUser');
        const startButton = document.getElementById('JellySyncStart');
        const statusElement = document.getElementById('JellySyncStatus');
        let configuration;
        let users = [];
        let libraries = [];
        let pollTimer;

        function escapeHtml(value) {
            const node = document.createElement('div');
            node.textContent = value || '';
            return node.innerHTML;
        }

        function selectedValues(container) {
            return Array.from(container.querySelectorAll('input[type="checkbox"]:checked')).map(input => input.value);
        }

        function renderChoices(container, choices, selected, idPrefix) {
            const selectedSet = new Set((selected || []).map(String).map(value => value.toLowerCase()));
            container.innerHTML = choices.map(choice => {
                const id = String(choice.Id);
                const checked = selectedSet.has(id.toLowerCase()) ? ' checked' : '';
                return '<label class="checkboxContainer"><input type="checkbox" is="emby-checkbox" id="' +
                    idPrefix + id + '" value="' + id + '"' + checked + '><span>' + escapeHtml(choice.Name) + '</span></label>';
            }).join('');
        }

        function updateLibraryVisibility() {
            librariesContainer.style.display = allLibraries.checked ? 'none' : '';
        }

        function isSupportedLibrary(library) {
            const collectionType = String(library.CollectionType ?? library.collectionType ?? '').toLowerCase();
            return collectionType === 'movies' || collectionType === 'tvshows';
        }

        function updateSourceUsers() {
            const selected = new Set(selectedValues(usersContainer));
            const previous = sourceSelect.value;
            sourceSelect.innerHTML = users
                .filter(user => selected.has(String(user.Id)))
                .map(user => '<option value="' + user.Id + '">' + escapeHtml(user.Name) + '</option>')
                .join('');
            if (selected.has(previous)) {
                sourceSelect.value = previous;
            }
            startButton.disabled = !sourceSelect.value;
        }

        function normalizeStatus(status) {
            if (typeof status === 'string') {
                try {
                    status = JSON.parse(status);
                } catch {
                    // Keep the original value for the caller's fallback handling.
                }
            }

            return status?.responseJSON ?? status?.data ?? status?.Data ?? status?.Status ?? status?.status ?? status;
        }

        function renderStatus(status) {
            status = normalizeStatus(status);
            if (!status) {
                statusElement.textContent = '';
                return;
            }

            const state = status.State ?? status.state;
            const processedItems = status.ProcessedItems ?? status.processedItems;
            const totalItems = status.TotalItems ?? status.totalItems;
            const updatedWrites = status.UpdatedWrites ?? status.updatedWrites;
            const unchangedWrites = status.UnchangedWrites ?? status.unchangedWrites;
            const failedWrites = status.FailedWrites ?? status.failedWrites;
            const latestError = status.LatestError ?? status.latestError;
            let text = state + ': ' + processedItems + '/' + totalItems +
                ' items; ' + updatedWrites + ' updated, ' + unchangedWrites +
                ' unchanged, ' + failedWrites + ' failed.';
            if (latestError) {
                text += ' Latest error: ' + latestError;
            }
            statusElement.textContent = text;
        }

        function isActiveStatus(status) {
            status = normalizeStatus(status);
            const state = status.State ?? status.state;
            return state === 'Queued' || state === 'Running';
        }

        function stopPolling() {
            if (pollTimer) {
                clearTimeout(pollTimer);
                pollTimer = null;
            }
        }

        async function loadStatus() {
            try {
                const status = await ApiClient.getJSON(ApiClient.getUrl('JellySync/Sync/Status'));
                renderStatus(status);
                    if (isActiveStatus(status)) {
                    pollTimer = setTimeout(loadStatus, 1000);
                }
            } catch (error) {
                statusElement.textContent = 'Unable to load synchronization status.';
            }
        }

        async function loadPage() {
            Dashboard.showLoadingMsg();
            stopPolling();
            try {
                const loaded = await Promise.all([
                    ApiClient.getPluginConfiguration(pluginId),
                    ApiClient.getJSON(ApiClient.getUrl('Users')),
                    ApiClient.getJSON(ApiClient.getUrl('Library/VirtualFolders'))
                ]);
                configuration = loaded[0];
                users = Array.isArray(loaded[1]) ? loaded[1] : (loaded[1].Items || []);
                libraries = Array.isArray(loaded[2]) ? loaded[2] : (loaded[2].Items || []);
                enabled.checked = !!configuration.Enabled;
                allLibraries.checked = configuration.IncludeAllLibraries !== false;
                renderChoices(usersContainer, users, configuration.UserIds, 'JellySyncUser');
                const libraryChoices = libraries
                    .filter(isSupportedLibrary)
                    .map(library => ({ Id: library.ItemId || library.Id, Name: library.Name }));
                renderChoices(librariesContainer, libraryChoices, configuration.LibraryIds, 'JellySyncLibrary');
                updateLibraryVisibility();
                updateSourceUsers();
                await loadStatus();
            } catch (error) {
                statusElement.textContent = 'Unable to load users and libraries. Check the Jellyfin server log for the request error.';
            } finally {
                Dashboard.hideLoadingMsg();
            }
        }

        form.addEventListener('submit', async function (event) {
            event.preventDefault();
            const selectedUsers = selectedValues(usersContainer);
            const selectedLibraries = selectedValues(librariesContainer);
            if (enabled.checked && new Set(selectedUsers).size < 2) {
                Dashboard.alert('Enabled synchronization requires at least two selected users.');
                return;
            }
            if (enabled.checked && !allLibraries.checked && new Set(selectedLibraries).size < 1) {
                Dashboard.alert('Selected-library mode requires at least one library.');
                return;
            }

            Dashboard.showLoadingMsg();
            try {
                configuration.Enabled = enabled.checked;
                configuration.UserIds = selectedUsers;
                configuration.IncludeAllLibraries = allLibraries.checked;
                configuration.LibraryIds = selectedLibraries;
                await ApiClient.updatePluginConfiguration(pluginId, configuration);
                Dashboard.processPluginConfigurationUpdateResult();
                updateSourceUsers();
            } finally {
                Dashboard.hideLoadingMsg();
            }
        });

        usersContainer.addEventListener('change', updateSourceUsers);
        allLibraries.addEventListener('change', updateLibraryVisibility);
        startButton.addEventListener('click', async function () {
            const sourceName = sourceSelect.options[sourceSelect.selectedIndex]?.text || 'the selected source';
            const confirmed = window.confirm(
                'This will overwrite every other selected user with ' + sourceName +
                '\'s watch state in the configured libraries. Missing source history will clear target history. Continue?');
            if (!confirmed) {
                return;
            }

            stopPolling();
            try {
                const response = await ApiClient.ajax({
                    type: 'POST',
                    url: ApiClient.getUrl('JellySync/Sync'),
                    dataType: 'json',
                    contentType: 'application/json',
                    data: JSON.stringify({ sourceUserId: sourceSelect.value })
                });
                renderStatus(response);
                    if (isActiveStatus(response)) {
                    pollTimer = setTimeout(loadStatus, 250);
                }
            } catch (error) {
                let message = 'Unable to start full sync.';
                    if (error && error.responseJSON && (error.responseJSON.Message || error.responseJSON.message)) {
                        message = error.responseJSON.Message ?? error.responseJSON.message;
                        renderStatus(error.responseJSON.Status ?? error.responseJSON.status);
                }
                statusElement.textContent = message + (statusElement.textContent ? ' ' + statusElement.textContent : '');
            }
        });

        page.addEventListener('viewshow', loadPage);
        page.addEventListener('viewhide', stopPolling);
        loadPage();
        }

        const pageObserver = new MutationObserver(initializePage);
        pageObserver.observe(document.documentElement, { childList: true, subtree: true });
        ensureStyles();
        initializePage();
    }());
