// 界面主逻辑：视图切换、资源列表、详情预览、导出进度、设置与日志。
// 纯 ES module，无框架 —— 桌面壳里没必要时引入 React 这种体量的运行时。

import { api, shell, isNativeShell } from './api.js';
import { i18n } from './i18n.js';

const $ = (id) => document.getElementById(id);
const t = (key, fallback) => i18n.t(key, fallback);

const state = {
    view: 'welcome',
    loaded: false,
    status: null,
    stats: null,
    classes: [],
    filter: { search: '', className: '', sort: 'class', desc: false, offset: 0, limit: 400 },
    total: 0,
    rows: [],
    selected: null,
    detail: null,
    detailTab: 'info',
    logSince: undefined,
    logFilter: '',
    operation: null,
    outputPath: '',
        languages: null,
        settingsSnapshot: null,
        logCount: 0,
            busyKind: null,
    };

// ---------------------------------------------------------------- 提示

function toast(message, kind = 'info', timeout = 4200) {
    const host = $('toast-host');
    const node = document.createElement('div');
    node.className = `toast is-${kind}`;
    node.innerHTML = '<span class="toast__dot"></span><span></span>';
    node.lastElementChild.textContent = message;
    host.append(node);
    setTimeout(() => {
        node.style.opacity = '0';
        node.style.transition = 'opacity 180ms linear';
        setTimeout(() => node.remove(), 200);
    }, timeout);
}

// 状态栏在长任务进行中只显示任务本身，不要被 refreshStatus 的轮询覆盖，
// 否则会看到「正在导入 37%」和「正在导出…」来回闪。
function setStatus(text) {
    if (state.busyKind) {
        return;
    }
    $('status-line').textContent = text;
}

// ---------------------------------------------------------------- 视图切换

const RAIL_TITLES = {
    welcome: 'panel.import',
    browse: 'panel.classes',
    tree: 'panel.tree',
    export: 'panel.export',
    settings: 'panel.settings',
    log: 'panel.log',
};

/** 让导航项反映「现在有没有东西可看」。必须在 loaded 变化后调用。 */
function updateNavAvailability() {
    const needsLoad = ['browse', 'tree', 'export'];
    for (const item of document.querySelectorAll('.rail__item[data-nav]')) {
        if (!needsLoad.includes(item.dataset.nav)) {
            continue;
        }
        const blocked = !state.loaded;
        item.disabled = blocked;
        item.title = blocked ? t('nav.needsImport') : '';
    }
}

function setView(view) {
    state.view = view;
    document.querySelectorAll('.rail__item[data-nav]').forEach((item) => {
        item.classList.toggle('is-active', item.dataset.nav === view);
    });

    const target = view === 'welcome' ? 'welcome' : view;
    document.querySelectorAll('.view').forEach((element) => {
        element.classList.toggle('is-active', element.dataset.view === target);
    });
    $('app').dataset.view = view;

    // 未导入文件时，这三个视图没有内容可显示，禁掉比让用户点进去看空列表好。
        updateNavAvailability();

        const titleKey = RAIL_TITLES[view];
        $('side-title').textContent = titleKey ? t(titleKey) : '';

    if (view === 'browse') {
        renderClassPanel();
    } else {
        renderSideInfo(view);
    }

    if (view === 'tree') {
        loadTree();
    } else if (view === 'settings') {
        loadSettings();
        } else if (view === 'export') {
            // 侧栏「当前导出选项」读的是设置快照，而这份快照默认只在打开设置页时加载。
            // 首次进导出页必须自己补一次，否则用户第一眼看到的是一个没有选项摘要的空栏。
            if (!state.settingsSnapshot) {
                loadSettings();
            }
        } else if (view === 'log') {
            renderLogSideInfo();
        }
}

// ---------------------------------------------------------------- 侧栏信息
//
// 每个视图的左栏除了标题还会给一组「有用的事实」。
// 只写一句说明的话左栏会显得很空，而且用户其实想知道
// 「现在能做什么 / 会写到哪 / 已经有多少」这类具体信息。

function sideSection(titleKey) {
    const section = document.createElement('section');
    section.className = 'side-info__section';
    const title = document.createElement('h3');
    title.className = 'side-info__section-title';
    title.textContent = t(titleKey);
    section.append(title);
    return section;
}

/** facts: [[labelKey, value, tone?], ...]，tone 取 '' | 'muted' | 'warn' | 'error' | 'path'。 */
function sideFacts(facts) {
    const dl = document.createElement('dl');
    dl.className = 'side-facts';
    for (const [labelKey, value, tone] of facts) {
        const div = document.createElement('div');
        const dt = document.createElement('dt');
        const dd = document.createElement('dd');
        dt.textContent = t(labelKey);
        dt.title = t(labelKey);
        dd.textContent = value ?? '—';
        if (tone) {
            dd.classList.add(`is-${tone}`);
        }
        if (tone === 'path' && value) {
            dd.title = value;
        }
        div.append(dt, dd);
        dl.append(div);
    }
    return dl;
}

function sideNote(textKey) {
    const p = document.createElement('p');
    p.className = 'side-note';
    p.textContent = t(textKey);
    return p;
}

function sideTag(textKey, tone) {
    const span = document.createElement('span');
    span.className = `side-tag${tone ? ` is-${tone}` : ''}`;
    span.textContent = t(textKey);
    return span;
}

function renderSideInfo(view) {
    const body = $('side-body');
    body.replaceChildren();

    const info = document.createElement('div');
    info.className = 'side-info';
    body.append(info);

    const status = state.status;
    const stats = state.stats;

        if (view === 'welcome') {
            info.append(
                sideFacts([
                    ['side.loaded', status?.loaded ? t('common.yes') : t('common.no'), status?.loaded ? 'ok' : 'muted'],
                    ['side.targetVersion', status?.unityVersion ?? null, status?.unityVersion ? '' : 'muted'],
                    ['side.webview', status?.webView2 ?? status?.shell ?? null, status?.webView2 ? 'ok' : 'warn'],
                    ['side.settingsFile', status?.settingsPath ?? null, status?.settingsPath ? 'path' : 'muted'],
                ]),
                sideNote('side.importNote'),
            );
            if (status?.loaded) {
                const section = sideSection('side.reimportHint');
                const warn = document.createElement('p');
                warn.className = 'side-note';
                warn.textContent = t('side.replaceWarning');
                section.append(warn);
                info.append(section);
            }
            return;
        }

        if (view === 'tree') {
        info.append(
            sideFacts([
                ['side.bundles', stats ? Number(stats.bundles).toLocaleString() : null],
                ['side.collections', stats ? Number(stats.collections).toLocaleString() : null],
                ['side.assets', stats ? Number(stats.assets).toLocaleString() : null],
                ['side.failedFiles', stats ? Number(stats.failedFiles).toLocaleString() : null,
                    stats?.failedFiles ? 'warn' : 'muted'],
            ]),
            sideNote('panel.treeHint'),
        );
    } else if (view === 'export') {
        info.append(
            sideFacts([
                ['side.assets', stats ? Number(stats.assets).toLocaleString() : null],
                ['side.targetVersion', status?.unityVersion ?? null, status?.unityVersion ? '' : 'muted'],
                ['side.outputDir', state.outputPath || null, state.outputPath ? 'path' : 'muted'],
            ]),
        );
        // 导出选项当前值摘要，比一句「会写出 Unity 工程」有用得多
        if (state.settingsSnapshot?.groups) {
            const exportGroup = state.settingsSnapshot.groups.find((group) => group.id === 'export');
            if (exportGroup) {
                const section = sideSection('side.currentExportOptions');
                section.append(sideFacts(
                    exportGroup.items
                        .filter((item) => item.type === 'enum' || item.type === 'bool')
                        .map((item) => {
                            if (item.type === 'bool') {
                                return [item.label, t(item.value === true || item.value === 'true' ? 'common.yes' : 'common.no')];
                            }
                            const option = (item.options ?? []).find((o) => o.value === item.value);
                            return [item.label, option?.label ?? item.value];
                        }),
                ));
                info.append(section);
            }
        }
        info.append(sideNote('panel.exportHint'));
    } else if (view === 'settings') {
        info.append(
            sideFacts([
                ['side.settingsFile', status?.settingsPath ?? null, status?.settingsPath ? 'path' : 'muted'],
                ['side.loaded', status?.loaded ? t('common.yes') : t('common.no'), status?.loaded ? 'warn' : 'muted'],
            ]),
            status?.loaded ? sideTag('side.settingsLocked', 'warn') : sideTag('side.settingsEditable', 'ok'),
            sideNote('panel.settingsHint'),
        );
    } else if (view === 'log') {
        info.append(sideFacts([
            ['side.logSource', t('side.logSourceGeneral')],
            ['side.entriesBuffered', state.logCount ? Number(state.logCount).toLocaleString() : null],
        ]), sideNote('panel.logHint'));
    }
}

function renderLogSideInfo() {
    // 日志条数会随轮询变化，切视图时刷新一次就够了。
    const body = $('side-body');
    const factHost = body.querySelector('.side-facts');
    if (!factHost) {
        renderSideInfo('log');
        return;
    }
    const values = [t('side.logSourceGeneral'), state.logCount ? Number(state.logCount).toLocaleString() : null];
    const dds = factHost.querySelectorAll('dd');
    if (dds.length >= 2) {
        dds[0].textContent = values[0];
        dds[1].textContent = values[1] ?? '—';
    }
}

function renderSideHint(text) {
    const body = $('side-body');
    if (!text) {
        body.innerHTML = '';
        return;
    }
    const wrap = document.createElement('div');
    wrap.className = 'side-info';
    const p = document.createElement('p');
    p.className = 'panel__hint';
    p.textContent = text;
    wrap.append(p);
    body.replaceChildren(wrap);
}

// ---------------------------------------------------------------- 状态刷新

async function refreshStatus() {
    try {
        const status = await api.status();
        state.status = status;
        state.loaded = status.loaded;

        const badge = $('env-badge');
        const webview = status.webView2 ? `WebView2 ${status.webView2}` : 'no WebView2';
        badge.textContent = isNativeShell() ? webview : (status.shell ?? 'browser');
        badge.hidden = false;

        if (status.loaded) {
                    setStatus(`Unity ${status.unityVersion ?? '?'}`);
                } else {
                    setStatus(status.exporting ? t('common.exporting') : t('common.ready'));
                }
                // 视图切换只由真实动作驱动（导入完成、点导航、重置），
                // 不能凭「现在已加载」就跳走：用户可能正停在导入页看统计，
                // 或被浏览器拦住了文件对话框——自动跳转会把人从当前页面推走。
                updateNavAvailability();
                await refreshStats();
    } catch (error) {
        setStatus('offline');
        console.error(error);
    }
}

async function refreshStats() {
    try {
        const stats = await api.stats();
        state.stats = stats;
        renderWelcomeFacts();
        if (state.view === 'browse') {
            renderClassPanel();
        } else if (state.view === 'tree' || state.view === 'export') {
            // 这些视图的侧栏含统计数字，导入完成后要跟着变
            renderSideInfo(state.view);
        }
    } catch {
        // 统计失败不影响主流程
    }
}

function renderWelcomeFacts() {
    const host = $('welcome-facts');
    const stats = state.stats;
    if (!stats) {
        host.replaceChildren();
        return;
    }
    const items = [
        ['welcome.fact.assets', stats.assets],
        ['welcome.fact.collections', stats.collections],
        ['welcome.fact.scenes', stats.scenes],
        ['welcome.fact.failed', stats.failedFiles],
    ];
    host.replaceChildren(
        ...items.map(([key, value]) => {
            const div = document.createElement('div');
            div.innerHTML = '<dt></dt><dd></dd>';
            div.querySelector('dt').textContent = t(key);
            div.querySelector('dd').textContent = Number(value).toLocaleString();
            return div;
        }),
    );
}

// ---------------------------------------------------------------- 资源类型面板

async function loadClasses() {
    try {
        state.classes = await api.classes();
    } catch {
        state.classes = [];
    }
}

const CLASS_TONE = [
    [/Texture|Cubemap|RenderTexture/i, 'texture'],
    [/Sprite/i, 'sprite'],
    [/Mesh|Terrain|Model/i, 'mesh'],
    [/Shader|Material/i, 'shader'],
    [/Audio/i, 'audio'],
];

function toneFor(className) {
    for (const [pattern, tone] of CLASS_TONE) {
        if (pattern.test(className)) {
            return tone;
        }
    }
    return '';
}

function renderClassPanel() {
    const body = $('side-body');
    if (!state.loaded) {
        renderSideHint(t('browse.importHint'));
        return;
    }
    if (state.classes.length === 0) {
        renderSideHint(t('browse.counting'));
        loadClasses().then(renderClassPanel);
        return;
    }

    const max = Math.max(...state.classes.map((item) => item.count), 1);
    const list = document.createElement('div');
    list.className = 'class-list';

    const makeItem = (label, count, active, onClick, width) => {
        const button = document.createElement('button');
        button.className = 'class-item' + (active ? ' is-active' : '');
        button.title = `${label} · ${count}`;
        button.innerHTML = '<span class="class-item__name"></span><span class="class-item__bar"><span class="class-item__fill"></span></span><span class="class-item__count"></span>';
        button.querySelector('.class-item__name').textContent = label;
        button.querySelector('.class-item__count').textContent = String(count);
        button.querySelector('.class-item__fill').style.width = `${Math.max(4, width * 100)}%`;
        button.addEventListener('click', onClick);
        return button;
    };

    list.append(makeItem(
        t('browse.allClasses'),
        state.stats?.assets ?? 0,
        state.filter.className === '',
        () => {
            state.filter.className = '';
            state.filter.search = '';
            state.filter.offset = 0;
            $('search').value = '';
            renderClassPanel();
            loadAssets();
        },
        state.stats?.assets ? 1 : 0,
    ));

    for (const item of state.classes) {
        list.append(makeItem(
            item.name,
            item.count,
            state.filter.className === item.name,
            () => {
                state.filter.className = item.name;
                state.filter.search = '';
                state.filter.offset = 0;
                $('search').value = '';
                renderClassPanel();
                loadAssets();
            },
            item.count / max,
        ));
    }

    body.replaceChildren(list);

        // 列表底部补一条汇总：当前筛选命中了多少、总共多少，
        // 以及当前排序方式。空着也是空着，不如把状态讲清楚。
        body.append(buildClassFooter());
    }

    /** 类列表下方的汇总块。结果数在 loadAssets 之后才知道，所以单独抽出来可重复刷新。 */
    function buildClassFooter() {
        const footer = document.createElement('div');
        footer.className = 'side-info side-info--footer';
        footer.dataset.role = 'class-footer';
        footer.append(sideFacts([
            ['side.matched', state.total ? Number(state.total).toLocaleString() : null],
            ['side.sortBy', t(`browse.col.${state.filter.sort}`) + (state.filter.desc ? ' ↓' : ' ↑'), 'muted'],
        ]));
        return footer;
    }

    function refreshClassFooter() {
        const existing = document.querySelector('.side-info--footer[data-role="class-footer"]');
        if (existing) {
            existing.replaceWith(buildClassFooter());
        }
    }

// ---------------------------------------------------------------- 资源列表

let loadAssetsToken = 0;

async function loadAssets() {
    const token = ++loadAssetsToken;
    const tbody = $('asset-rows');
    const empty = $('asset-empty');

    if (!state.loaded) {
        tbody.replaceChildren();
        empty.hidden = false;
        empty.querySelector('p').textContent = t('browse.empty');
        $('asset-count').textContent = '—';
            // 重置后必须把上一次的「加载更多」也清掉，否则它会浮在空状态上方。
            tbody.closest('.table-wrap')?.querySelector('.load-more')?.remove();
            return;
        }

    try {
        const page = await api.assets({
            q: state.filter.search,
            class: state.filter.className,
            sort: state.filter.sort,
            desc: state.filter.desc,
            offset: state.filter.offset,
            limit: state.filter.limit,
        });
        if (token !== loadAssetsToken) {
            return; // 有更新的请求在跑，丢弃这次结果
        }

        state.rows = page.items;
        state.total = page.total;

        if (page.total === 0) {
            tbody.replaceChildren();
            empty.hidden = false;
            empty.querySelector('p').textContent = state.filter.search || state.filter.className
                ? t('browse.noMatch')
                : t('browse.empty');
        } else {
            empty.hidden = true;
            renderRows(page);
        }

        const shown = Math.min(page.offset + page.items.length, page.total);
        $('asset-count').textContent = `${shown.toLocaleString()} / ${page.total.toLocaleString()}`;
                refreshClassFooter();
    } catch (error) {
        empty.hidden = false;
        empty.querySelector('p').textContent = `${t('common.readFailed')}: ${error.message}`;
    }
}

function renderRows(page) {
    const tbody = $('asset-rows');
    const fragment = document.createDocumentFragment();

    for (const row of page.items) {
        const tr = document.createElement('tr');
        tr.dataset.pathId = String(row.pathId);
        tr.dataset.path = row.collectionPath;
        if (state.selected && state.selected.pathId === row.pathId && state.selected.path === row.collectionPath) {
            tr.classList.add('is-selected');
        }

        const tone = toneFor(row.className);
        tr.innerHTML = `
            <td class="col-name"></td>
            <td class="col-class"><span class="type-pill ${tone ? `type-pill--${tone}` : ''}"></span></td>
            <td class="col-coll"></td>
            <td class="col-size col-num"></td>
            <td class="col-dim col-num"></td>`;
        tr.querySelector('.col-name').textContent = row.name || `#${row.pathId}`;
        tr.querySelector('.type-pill').textContent = row.className;
        tr.querySelector('.col-coll').textContent = row.collection;
        tr.querySelector('.col-size').textContent = row.sizeBytes ? formatBytes(row.sizeBytes) : '—';
        tr.querySelector('.col-dim').textContent = row.width && row.height ? `${row.width}×${row.height}` : '—';

        tr.addEventListener('click', () => selectAsset(row, tr));
        fragment.append(tr);
    }

    tbody.replaceChildren(fragment);

    const wrap = tbody.closest('.table-wrap');
    wrap.querySelector('.load-more')?.remove();
    if (page.offset + page.items.length < page.total) {
        const button = document.createElement('fluent-button');
        button.className = 'load-more';
        button.appearance = 'subtle';
        const remaining = page.total - page.offset - page.items.length;
        button.textContent = `${t('common.loadMore')} (${remaining.toLocaleString()})`;
        button.addEventListener('click', () => {
            state.filter.offset += state.filter.limit;
            loadAssets();
        });
        wrap.append(button);
    }
}

function formatBytes(bytes) {
    if (!bytes) {
        return '—';
    }
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = bytes;
    let index = 0;
    while (value >= 1024 && index < units.length - 1) {
        value /= 1024;
        index++;
    }
    return `${value >= 100 || index === 0 ? Math.round(value) : value.toFixed(1)} ${units[index]}`;
}

// ---------------------------------------------------------------- 详情面板

async function selectAsset(row, tr) {
    document.querySelectorAll('#asset-rows tr.is-selected').forEach((element) => element.classList.remove('is-selected'));
    tr?.classList.add('is-selected');
    state.selected = { path: row.collectionPath, pathId: row.pathId, row };
        // 窄屏下详情面板是抽屉式（CSS 只在 .has-selection 时显示），必须同步这个类。
        document.querySelector('.split')?.classList.add('has-selection');
        const hasPreview = row.preview || row.audio || row.model || row.video || row.font;
    state.detailTab = hasPreview ? 'preview' : 'info';

    const panel = $('detail');
    panel.innerHTML = `<div class="detail__placeholder">${t('common.loading')}</div>`;

    try {
        state.detail = await api.detail(row.collectionPath, row.pathId);
        renderDetail();
    } catch (error) {
        panel.innerHTML = `<div class="detail__placeholder">${escapeHtml(t('common.readFailed'))}: ${escapeHtml(error.message)}</div>`;
    }
}

function clearSelection() {
    state.selected = null;
    state.detail = null;
    document.querySelector('.split')?.classList.remove('has-selection');
    const panel = $('detail');
    panel.innerHTML = `<div class="detail__placeholder">${t('browse.detailPlaceholder')}</div>`;
}

function renderDetail() {
    const detail = state.detail;
    const row = state.selected?.row;
    const panel = $('detail');
    if (!detail) {
        return;
    }

    const tabs = [];
    if (row?.preview || row?.audio || row?.model || row?.video || row?.font) {
        tabs.push(['preview', 'detail.tab.preview']);
    }
    tabs.push(['info', 'detail.tab.info']);
    if (detail.previews?.includes('text')) {
        tabs.push(['text', 'detail.tab.text']);
    }
    if (detail.previews?.includes('json')) {
        tabs.push(['json', 'detail.tab.json']);
    }
    if (detail.previews?.includes('yaml')) {
        tabs.push(['yaml', 'detail.tab.yaml']);
    }

    if (!tabs.some(([key]) => key === state.detailTab)) {
        state.detailTab = tabs[0][0];
    }

    panel.innerHTML = `
        <div class="detail__header">
            <div style="min-width:0">
                <h2 class="detail__title"></h2>
                <p class="detail__subtitle"></p>
            </div>
                <button class="detail__close" type="button" aria-label="close">×</button>
            </div>
            <div class="detail__tabs"></div>
            <div class="detail__body"></div>`;

        panel.querySelector('.detail__close').addEventListener('click', clearSelection);

    panel.querySelector('.detail__title').textContent = detail.name || `#${detail.pathId}`;
    panel.querySelector('.detail__subtitle').textContent = `${detail.className} · ${detail.collection}`;

    const tabHost = panel.querySelector('.detail__tabs');
    for (const [key, labelKey] of tabs) {
        const button = document.createElement('button');
        button.className = 'tab-btn' + (key === state.detailTab ? ' is-active' : '');
        button.textContent = t(labelKey);
        button.addEventListener('click', () => {
            state.detailTab = key;
            renderDetail();
        });
        tabHost.append(button);
    }

    renderDetailBody(panel.querySelector('.detail__body'), detail, state.detailTab);
}

function renderDetailBody(body, detail, tab) {
    if (tab === 'info') {
        const table = document.createElement('table');
        table.className = 'props';
        for (const property of detail.properties) {
            const tr = document.createElement('tr');
            tr.innerHTML = '<th></th><td></td>';
            tr.querySelector('th').textContent = property.name;
            tr.querySelector('td').textContent = property.value;
            table.append(tr);
        }
        body.replaceChildren(table);
        return;
    }

    if (tab === 'preview') {
        renderPreview(body, detail);
        return;
    }

    body.innerHTML = `<div class="detail__placeholder">${t('common.loading')}</div>`;
    const loader = tab === 'text'
        ? api.text(detail.collectionPath, detail.pathId)
        : tab === 'json'
            ? api.assetJson(detail.collectionPath, detail.pathId)
            : api.assetYaml(detail.collectionPath, detail.pathId);

    loader.then((content) => {
        const pre = document.createElement('pre');
        pre.className = 'detail__text';
        pre.textContent = typeof content === 'string' ? content : JSON.stringify(content, null, 2);
        body.replaceChildren(pre);
    }).catch((error) => {
        body.innerHTML = `<div class="detail__placeholder">${escapeHtml(t('common.readFailed'))}: ${escapeHtml(error.message)}</div>`;
    });
}

function renderPreview(body, detail) {
    const row = state.selected?.row;
    const path = detail.collectionPath;
    const id = detail.pathId;
    const children = [];

    if (row?.preview || detail.previews?.includes('image')) {
        const img = document.createElement('img');
        img.className = 'preview-image';
        img.loading = 'lazy';
        img.src = api.imageUrl(path, id, 'png');
        img.alt = detail.name;
        img.addEventListener('error', () => {
            img.replaceWith(createPlaceholder(t('common.notImage')));
        });
        children.push(img);

        const meta = document.createElement('p');
        meta.className = 'preview-meta';
        meta.textContent = row?.width ? `${row.width} × ${row.height}` : t('detail.pngPreview');
        children.push(meta);
    }

    if (detail.previews?.includes('audio')) {
        const audio = document.createElement('audio');
        audio.controls = true;
        audio.className = 'preview-audio';
        audio.src = api.audioUrl(path, id);
        children.push(audio);
    }

    if (detail.previews?.includes('video')) {
        const video = document.createElement('video');
        video.controls = true;
        video.className = 'preview-audio';
        video.src = api.videoUrl(path, id);
        children.push(video);
    }

    if (detail.previews?.includes('model')) {
        const link = document.createElement('fluent-anchor-button');
        link.appearance = 'outline';
        link.href = api.modelUrl(path, id);
        link.target = '_blank';
        link.textContent = t('detail.downloadGlb');
        children.push(link);
    }

    if (detail.previews?.includes('font')) {
        const link = document.createElement('fluent-anchor-button');
        link.appearance = 'outline';
        link.href = api.fontUrl(path, id);
        link.textContent = t('detail.downloadFont');
        children.push(link);
    }

    if (children.length === 0) {
        body.replaceChildren(createPlaceholder(t('common.empty')));
        return;
    }
    body.replaceChildren(...children);
}

function createPlaceholder(text) {
    const div = document.createElement('div');
    div.className = 'detail__placeholder';
    div.textContent = text;
    return div;
}

// ---------------------------------------------------------------- 文件树

let treeLoaded = false;

async function loadTree() {
    if (treeLoaded) {
        return;
    }
    const root = $('tree-root');
    root.innerHTML = `<div class="panel__hint">${t('common.loading')}</div>`;
    try {
        const nodes = await api.tree();
        root.replaceChildren(...nodes.map((node) => buildTree(node, 0)));
        treeLoaded = true;
    } catch (error) {
        root.innerHTML = `<div class="panel__hint">${escapeHtml(t('common.readFailed'))}: ${escapeHtml(error.message)}</div>`;
    }
}

const ICONS = {
    bundle: '<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M3 8 12 3l9 5v8l-9 5-9-5z" stroke-linejoin="round"/></svg>',
    collection: '<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.6"><rect x="4" y="4" width="16" height="16" rx="2"/><path d="M4 10h16"/></svg>',
    scene: '<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.6"><circle cx="12" cy="12" r="8"/><path d="M12 4v16M4 12h16"/></svg>',
};

function buildTree(node, depth) {
    const wrap = document.createElement('div');
    wrap.className = 'tree-node';
    wrap.style.setProperty('--indent', `${depth * 14}px`);
    if (depth < 1) {
        wrap.classList.add('is-open');
    }

    const hasChildren = (node.children?.length ?? 0) > 0;
    const row = document.createElement('button');
    row.className = 'tree-node__row';
    row.innerHTML = `
        <span class="tree-node__chevron">${hasChildren ? '›' : ''}</span>
        <span class="tree-node__icon">${ICONS[node.kind] ?? ICONS.collection}</span>
        <span class="tree-node__label"></span>
        <span class="tree-node__count"></span>`;
    row.querySelector('.tree-node__label').textContent = node.name;
    row.querySelector('.tree-node__count').textContent = node.count != null ? String(node.count) : '';

    row.addEventListener('click', (event) => {
        event.stopPropagation();
        if (hasChildren) {
            wrap.classList.toggle('is-open');
        }
        if (node.kind === 'collection' || node.kind === 'scene') {
            state.filter.search = node.name;
            state.filter.className = '';
            state.filter.offset = 0;
            $('search').value = node.name;
            setView('browse');
            loadAssets();
        }
        document.querySelectorAll('.tree-node__row.is-selected').forEach((element) => element.classList.remove('is-selected'));
        row.classList.add('is-selected');
    });

    wrap.append(row);

    if (hasChildren) {
        const children = document.createElement('div');
        children.className = 'tree-node__children';
        children.append(...node.children.map((child) => buildTree(child, depth + 1)));
        wrap.append(children);
    }
    return wrap;
}

// ---------------------------------------------------------------- 路径输入对话框

// 原生对话框不可用时的兜底：让用户粘贴路径。
function askPath(title, value, placeholder) {
    return new Promise((resolve) => {
        const overlay = document.createElement('div');
        overlay.className = 'dialog-overlay';
        overlay.innerHTML = `
            <div class="dialog" role="dialog" aria-modal="true">
                <h2 class="dialog__title"></h2>
                <fluent-text-input class="dialog__field" appearance="outline"></fluent-text-input>
                <p class="dialog__error" hidden></p>
                <div class="dialog__actions">
                    <fluent-button class="dialog__cancel" appearance="subtle"></fluent-button>
                    <fluent-button class="dialog__ok" appearance="accent"></fluent-button>
                </div>
            </div>`;
        overlay.querySelector('.dialog__title').textContent = title;
        overlay.querySelector('.dialog__cancel').textContent = t('common.cancel');
        overlay.querySelector('.dialog__ok').textContent = t('common.ok');

        const field = overlay.querySelector('.dialog__field');
        field.placeholder = placeholder ?? t('dialog.pathPlaceholder');
        field.value = value ?? '';

        const close = (result) => {
            overlay.remove();
            resolve(result);
        };

        overlay.querySelector('.dialog__ok').addEventListener('click', () => {
            const text = (field.value ?? '').trim();
            if (!text) {
                const error = overlay.querySelector('.dialog__error');
                error.hidden = false;
                error.textContent = t('dialog.pathRequired');
                return;
            }
            close(text);
        });
        overlay.querySelector('.dialog__cancel').addEventListener('click', () => close(null));
        overlay.addEventListener('mousedown', (event) => {
            if (event.target === overlay) {
                close(null);
            }
        });
        overlay.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') {
                close(null);
            } else if (event.key === 'Enter') {
                overlay.querySelector('.dialog__ok').click();
            }
        });

        document.body.append(overlay);
        setTimeout(() => field.focus(), 0);
    });
}

// ---------------------------------------------------------------- 确认对话框

/** 应用内确认框。window.confirm 在 WebView2 里被禁用，必须自带。 */
function askConfirm(titleKey, messageKey, danger) {
    return new Promise((resolve) => {
        const overlay = document.createElement('div');
        overlay.className = 'dialog-overlay';
        overlay.innerHTML = `
            <div class="dialog dialog--confirm" role="dialog" aria-modal="true">
                <h2 class="dialog__title"></h2>
                <p class="dialog__message"></p>
                <div class="dialog__actions">
                    <fluent-button class="dialog__cancel" appearance="subtle"></fluent-button>
                    <fluent-button class="dialog__ok"></fluent-button>
                </div>
            </div>`;
        overlay.querySelector('.dialog__title').textContent = t(titleKey);
        overlay.querySelector('.dialog__message').textContent = t(messageKey);
        overlay.querySelector('.dialog__cancel').textContent = t('common.cancel');
        const ok = overlay.querySelector('.dialog__ok');
        ok.textContent = t('common.ok');
        ok.appearance = danger ? 'primary' : 'accent';

        const close = (result) => {
            overlay.remove();
            document.removeEventListener('keydown', onKey);
            resolve(result);
        };
        const onKey = (event) => {
            if (event.key === 'Escape') {
                close(false);
            } else if (event.key === 'Enter') {
                close(true);
            }
        };

        ok.addEventListener('click', () => close(true));
        overlay.querySelector('.dialog__cancel').addEventListener('click', () => close(false));
        overlay.addEventListener('mousedown', (event) => {
            if (event.target === overlay) {
                close(false);
            }
        });
        document.addEventListener('keydown', onKey);

        document.body.append(overlay);
        setTimeout(() => overlay.querySelector('.dialog__cancel').focus(), 0);
    });
}

// ---------------------------------------------------------------- 重置 / 重新导入

async function doReset() {
    const confirmed = await askConfirm('reset.title', 'reset.confirm', true);
    if (!confirmed) {
        return;
    }
    try {
        await api.reset();
        state.loaded = false;
        state.stats = null;
        state.rows = [];
        state.classes = [];
        state.selected = null;
        state.detail = null;
        state.settingsSnapshot = null;
        state.outputPath = '';
        state.total = 0;
                treeLoaded = false;
                clearSelection();
        document.querySelector('.split')?.classList.remove('has-selection');
        $('asset-rows')?.replaceChildren();
        $('asset-count').textContent = '—';
        await refreshStatus();
        await loadClasses();
        setView('welcome');
        toast(t('reset.done'), 'success');
    } catch (error) {
        toast(error.message, 'error', 7000);
    }
}

// ---------------------------------------------------------------- 导入

/** 走系统原生对话框（后端 NativeFileDialog），拿不到再退化为手输。 */
async function pickPaths(kind, titleKey, placeholder) {
    const result = kind === 'folder' ? await api.pickFolder(t(titleKey)) : await api.pickFile(t(titleKey));
    if (result && !result.unsupported) {
        return result.canceled ? null : result.paths;
    }

    const path = await askPath(t(titleKey), '', placeholder);
    return path ? [path] : null;
}

async function chooseFolder() {
    const paths = await pickPaths('folder', 'dialog.pickGameFolder', 'D:\\Games\\MyGame');
    if (paths?.length) {
        await startLoad(paths);
    }
}

async function chooseFile() {
    const paths = await pickPaths('file', 'dialog.pickGameFile', 'D:\\Games\\MyGame\\game_Data\\resources.assets');
    if (paths?.length) {
        await startLoad(paths);
    }
}

async function startLoad(paths) {
    try {
        setStatus(t('common.importing'));
        await api.load(paths);
        switchToBusy('import');
    } catch (error) {
        toast(error.message, 'error');
        setStatus(t('common.failed'));
    }
}

// ---------------------------------------------------------------- 长任务轮询

let operationTimer = null;

function switchToBusy(kind) {
    state.busyKind = kind;
    $('global-progress').visible = true;
    $('global-progress').value = 0;
    $('status-line').textContent = kind === 'export' ? t('common.exporting') : t('common.importing');
    if (operationTimer) {
        return;
    }
    operationTimer = setInterval(pollOperation, 600);
}

function stopBusy() {
    state.busyKind = null;
    $('global-progress').visible = false;
    if (operationTimer) {
        clearInterval(operationTimer);
        operationTimer = null;
    }
}

async function pollOperation() {
    try {
        const operation = await api.operation();
        state.operation = operation;

        if (!operation || operation.state !== 'running') {
            stopBusy();
            if (operation?.state === 'done') {
                toast(operation.kind === 'export' ? t('export.done') : t('import.done'), 'success');
                if (operation.outputPath) {
                    state.outputPath = operation.outputPath;
                    $('btn-open-output').disabled = false;
                }
                await refreshStatus();
                await loadClasses();
                treeLoaded = false;
                if (operation.kind === 'export') {
                    setView('export');
                    renderExportResult(operation);
                } else {
                    setView('browse');
                    loadAssets();
                }
            } else if (operation?.state === 'failed') {
                toast(operation.kind === 'export' ? t('export.failed') : t('import.failed'), 'error', 8000);
                setStatus(t('common.failed'));
                renderExportError(operation);
            }
            return;
        }

        const percent = Math.round(operation.percent ?? 0);
        $('global-progress').value = percent;
                // 长任务的消息来自日志行，英文/中文都可能，但标签统一用任务类型，
                // 避免用户看到「Exporting 'xxx'」这种与当前任务无关的措辞。
                $('status-line').textContent = `${operation.kind === 'export' ? t('common.exporting') : t('common.importing')} ${percent}%`;
                updateExportProgress(operation);
    } catch {
        // 轮询失败忽略，下一次重试
    }
}

function updateExportProgress(operation) {
    const block = $('export-progress');
    if (operation.kind !== 'export' || operation.state !== 'running') {
        return;
    }
    block.hidden = false;
    $('progress-bar').value = operation.percent ?? 0;
    $('progress-percent').textContent = `${Math.round(operation.percent ?? 0)}%`;
    $('progress-message').textContent = operation.message ?? t('common.exporting');
}

function renderExportResult(operation) {
    const block = $('export-progress');
    block.hidden = false;
    $('progress-bar').value = 100;
    $('progress-percent').textContent = '100%';
    $('progress-message').textContent = `${t('export.completed')}${operation.outputPath ?? ''}`;
}

function renderExportError(operation) {
    const block = $('export-progress');
    block.hidden = false;
    $('progress-bar').value = 100;
    $('progress-percent').textContent = t('common.failed');
    $('progress-message').textContent = operation.error?.split('\n')[0] ?? t('export.failed');
}

// ---------------------------------------------------------------- 导出视图

async function pickOutputFolder() {
    const result = await api.pickFolder(t('dialog.pickOutput'));
    if (result && !result.unsupported) {
        if (!result.canceled && result.paths?.length) {
            state.outputPath = result.paths[0];
            $('export-path').value = state.outputPath;
            renderSideInfo('export');
        }
        return;
    }
    const path = await askPath(t('dialog.pickOutput'), state.outputPath, 'D:\\Exports\\MyGame');
    if (path) {
        state.outputPath = path;
        $('export-path').value = state.outputPath;
        renderSideInfo('export');
    }
}

async function startExport() {
    if (!state.loaded) {
        toast(t('export.needLoad'), 'error');
        return;
    }
    const outputPath = $('export-path').value?.trim() || state.outputPath;
    if (!outputPath) {
        toast(t('export.needPath'), 'error');
        return;
    }

    try {
        await api.exportProject(outputPath, $('export-subfolder').checked);
        $('export-progress').hidden = false;
        $('progress-bar').value = 0;
        $('progress-percent').textContent = '0%';
        $('progress-message').textContent = t('export.preparing');
        switchToBusy('export');
    } catch (error) {
        toast(error.message, 'error', 7000);
    }
}

// ---------------------------------------------------------------- 设置

async function loadSettings() {
    const root = $('settings-root');
    root.innerHTML = `<div class="panel__hint">${t('common.loading')}</div>`;
    try {
        const settings = await api.settings();
        state.settingsSnapshot = settings;
        renderSettings(settings);
        if (state.view === 'settings' || state.view === 'export') {
            renderSideInfo(state.view);
        }
    } catch (error) {
        root.innerHTML = `<div class="panel__hint">${escapeHtml(t('common.readFailed'))}: ${escapeHtml(error.message)}</div>`;
    }
}

function renderSettings(settings) {
    const root = $('settings-root');
    root.replaceChildren();

    if (settings.readOnly) {
        const banner = document.createElement('div');
        banner.className = 'settings__banner';
        banner.textContent = t('settings.locked');
        root.append(banner);
    }

    const pending = new Map();

    for (const group of settings.groups) {
        const section = document.createElement('section');
        section.className = 'settings__group';
        const title = document.createElement('h2');
        title.textContent = t(`settings.group.${group.id}`);
        section.append(title);

        for (const item of group.items) {
            section.append(buildSettingRow(item, settings.readOnly, pending));
        }

        root.append(section);
    }

    if (!settings.readOnly) {
        const actions = document.createElement('div');
        actions.className = 'settings__actions';
        const save = document.createElement('fluent-button');
        save.appearance = 'accent';
        save.textContent = t('common.save');
        save.addEventListener('click', async () => {
            if (pending.size === 0) {
                toast(t('common.noChange'), 'info');
                return;
            }
            try {
                await api.saveSettings(Object.fromEntries(pending));
                pending.clear();
                toast(t('common.saved'), 'success');
            } catch (error) {
                toast(error.message, 'error', 7000);
            }
        });
        actions.append(save);
        root.append(actions);
    }
}

function buildSettingRow(item, readOnly, pending) {
    const row = document.createElement('div');
    row.className = 'settings__row';

    const labelWrap = document.createElement('div');
    labelWrap.className = 'settings__label';
    const label = document.createElement('span');
    label.className = 'settings__name';
    label.textContent = item.label;
    labelWrap.append(label);
    if (item.description) {
        const hint = document.createElement('span');
        hint.className = 'settings__hint';
        hint.textContent = item.description;
        labelWrap.append(hint);
    }

    const control = document.createElement('div');
    control.className = 'settings__control';

    if (item.type === 'bool') {
        const checkLabel = document.createElement('label');
        checkLabel.className = 'check-row';
        const checkbox = document.createElement('fluent-checkbox');
        checkbox.checked = item.value === 'true';
        checkbox.disabled = readOnly;
        const text = document.createElement('span');
        text.className = 'check-row__text';
        text.textContent = checkbox.checked ? '✓' : '';
        checkbox.addEventListener('change', () => {
            text.textContent = checkbox.checked ? '✓' : '';
            pending.set(item.key, checkbox.checked ? 'true' : 'false');
        });
        checkLabel.append(checkbox, text);
        control.append(checkLabel);
    } else if (item.type === 'enum') {
        control.append(buildEnumControl(item, readOnly, pending));
    } else {
        const input = document.createElement('fluent-text-input');
        input.value = item.value ?? '';
        input.disabled = readOnly;
        input.addEventListener('change', () => pending.set(item.key, input.value));
        control.append(input);
    }

    row.append(labelWrap, control);
    return row;
}

/** 枚举控件：下拉 + 选中项说明（复刻原始 WebUI 的 option-description 行为）。 */
function buildEnumControl(item, readOnly, pending) {
    const wrap = document.createElement('div');
    wrap.className = 'settings__enum';

    const select = document.createElement('select');
    select.className = 'settings__select';
    select.disabled = readOnly;

    const description = document.createElement('p');
    description.className = 'settings__option-desc';

    const options = item.options ?? [];
    for (const option of options) {
        const node = document.createElement('option');
        node.value = option.value;
        node.textContent = option.label ?? option.value;
        if (option.value === item.value) {
            node.selected = true;
        }
        select.append(node);
    }

    const syncDescription = () => {
        const selected = options.find((option) => option.value === select.value);
        const text = selected?.description ?? '';
        description.textContent = text;
        description.hidden = !text;
    };
    syncDescription();

    select.addEventListener('change', () => {
        pending.set(item.key, select.value);
        syncDescription();
    });

    wrap.append(select, description);
    return wrap;
}

// ---------------------------------------------------------------- 日志

let logTimer = null;

async function pollLog() {
    try {
        const payload = await api.log(state.logSince);
        if (!payload) {
            return;
        }
        const { items } = payload;
        if (!items?.length) {
            if (state.logSince === undefined) {
                renderLogEmpty();
            }
            return;
        }
        appendLog(items);
        state.logSince = payload.lastSeq ?? state.logSince;
    } catch {
        // 忽略：下一次轮询继续
    }
}

function renderLogEmpty() {
    const list = $('log-list');
    if (!list.querySelector('.log__empty')) {
        list.innerHTML = '<div class="log__empty"></div>';
        list.querySelector('.log__empty').textContent = t('log.empty');
    }
}

function appendLog(items) {
    const list = $('log-list');
    list.querySelector('.log__empty')?.remove();

    const fragment = document.createDocumentFragment();
    for (const item of items) {
        if (state.logFilter && !matchesFilter(item)) {
            continue;
        }
        const row = document.createElement('div');
        row.className = `log__row log__row--${item.level}`;
        row.innerHTML = '<span class="log__time"></span><span class="log__level"></span><span class="log__message"></span>';
        row.querySelector('.log__time').textContent = item.time;
        row.querySelector('.log__level').textContent = item.level;
        row.querySelector('.log__message').textContent = `[${item.category}] ${item.message}`;
        fragment.append(row);
    }
    list.append(fragment);
        state.logCount = list.querySelectorAll('.log__row').length;
        if (state.view === 'log') {
            renderLogSideInfo();
    }

        if ($('log-autoscroll').checked) {
            list.scrollTop = list.scrollHeight;
        }
    }

function matchesFilter(item) {
    const needle = state.logFilter.toLowerCase();
    return item.message.toLowerCase().includes(needle)
        || item.category.toLowerCase().includes(needle)
        || item.level.toLowerCase().includes(needle);
}

function startLogPolling() {
    if (logTimer) {
        return;
    }
    pollLog();
    logTimer = setInterval(pollLog, 900);
}

// ---------------------------------------------------------------- 详情宽度拖拽

const DETAIL_MIN = 260;
// 表格至少要留的空间，否则拖过头会把列表挤没
const LIST_MIN = 320;

function bindDetailGutter() {
    const gutter = $('detail-gutter');
    const split = document.querySelector('.split');
    if (!gutter || !split) {
        return;
    }

    let dragging = false;
    // 记住用户「想要」的宽度，和当前因窗口受限而实际生效的宽度分开。
    // 否则窗口临时变窄时的 clamp 结果会被当成用户意图存下来。
    let desired = 0;

    const apply = (width) => {
        desired = width;
        const total = split.getBoundingClientRect().width;
        const max = Math.max(DETAIL_MIN, total - LIST_MIN - 6);
        const clamped = Math.min(Math.max(width, DETAIL_MIN), max);
        split.style.setProperty('--detail-width', `${Math.round(clamped)}px`);
            // 标记「用户主动拖过」：CSS 用它决定是走百分比兜底还是走拖动值。
            split.dataset.resized = '1';
        };

    const onMove = (event) => {
        if (!dragging) {
            return;
        }
        const rect = split.getBoundingClientRect();
        // 分隔条中心到右侧的全长就是详情宽度
        apply(rect.right - event.clientX);
        event.preventDefault();
    };

    const stop = () => {
        if (!dragging) {
            return;
        }
        dragging = false;
        gutter.classList.remove('is-dragging');
        document.body.classList.remove('is-resizing');
        try {
            localStorage.setItem('arp.detailWidth', String(Math.round(desired)));
        } catch {
            /* 无痕模式忽略 */
        }
    };

    gutter.addEventListener('pointerdown', (event) => {
        if (event.button !== 0) {
            return;
        }
        dragging = true;
        gutter.classList.add('is-dragging');
        document.body.classList.add('is-resizing');
        gutter.setPointerCapture?.(event.pointerId);
        event.preventDefault();
    });
    gutter.addEventListener('pointermove', onMove);
    gutter.addEventListener('pointerup', stop);
    gutter.addEventListener('pointercancel', stop);

    // 双击恢复默认比例
    gutter.addEventListener('dblclick', () => {
            desired = 0;
            split.style.removeProperty('--detail-width');
            delete split.dataset.resized;
            try {
                localStorage.removeItem('arp.detailWidth');
            } catch {
                /* 忽略 */
            }
        });

        // 恢复上次拖的宽度
        try {
            const saved = localStorage.getItem('arp.detailWidth');
            const width = Number.parseInt(saved ?? '', 10);
            if (Number.isFinite(width) && width > 0) {
                apply(width);
            }
        } catch {
            /* 忽略 */
        }

        // 窗口尺寸变化时重新按「想要的宽度」clamp，保持用户意图不变
        window.addEventListener('resize', () => {
            if (desired > 0) {
                apply(desired);
        }
    });
}

// ---------------------------------------------------------------- 工具

function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, (char) => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;',
    }[char]));
}

function debounce(fn, delay) {
    let timer = null;
    return (...args) => {
        clearTimeout(timer);
        timer = setTimeout(() => fn(...args), delay);
    };
}

// ---------------------------------------------------------------- 语言切换

async function switchLanguage() {
    // 语言列表由后端下发，别硬编码二态：以后加语言不用改前端。
    const languages = state.languages?.length
        ? state.languages
        : [{ code: 'zh-Hans', name: '简体中文' }, { code: 'en', name: 'English' }];
    const index = languages.findIndex((item) => item.code === i18n.language);
    const next = languages[(index + 1) % languages.length].code;
    if (next === i18n.language) {
        return;
    }
    try {
        const payload = await api.setLanguage(next);
        if (payload?.languages) {
            state.languages = payload.languages;
        }
        i18n.apply(payload);
        updateLanguageButton();
        // 文案变了，已渲染的动态内容必须重建。
        rerenderAfterLanguageChange();
    } catch (error) {
        toast(error.message, 'error');
    }
}

function updateLanguageButton() {
    const button = $('lang-switch');
    const languages = state.languages?.length
        ? state.languages
        : [{ code: 'zh-Hans', name: '简体中文' }, { code: 'en', name: 'English' }];
    const current = languages.find((item) => item.code === i18n.language);
    // 下一次点击会切到哪个语言
    const index = languages.findIndex((item) => item.code === i18n.language);
    const next = languages[(index + 1) % languages.length];
    button.textContent = current ? current.code === 'en' ? 'EN' : '中文' : i18n.language;
    button.title = next ? next.name : '';
}

function rerenderAfterLanguageChange() {
    // 侧栏标题、提示、状态栏、当前视图里的动态内容都要按新语言重画。
    setView(state.view);
    if (state.loaded) {
        loadClasses().then(() => {
            if (state.view === 'browse') {
                renderClassPanel();
            }
        });
            if (state.view === 'settings' || state.view === 'export') {
            loadSettings();
        }
        if (state.detail) {
            renderDetail();
        }
    }
    if (state.stats) {
        renderWelcomeFacts();
    }
    if (state.view === 'log') {
        $('log-list').replaceChildren();
        state.logSince = undefined;
        pollLog();
    }
}

// ---------------------------------------------------------------- 绑定

function bind() {
    document.querySelectorAll('.rail__item[data-nav]').forEach((item) => {
        item.addEventListener('click', () => setView(item.dataset.nav));
    });

    $('lang-switch').addEventListener('click', switchLanguage);

    $('btn-load-folder').addEventListener('click', chooseFolder);
    $('btn-load-file').addEventListener('click', chooseFile);
    $('btn-load-from-empty').addEventListener('click', chooseFolder);
    $('btn-refresh').addEventListener('click', async () => {
        await loadClasses();
        await loadAssets();
    });

    $('search').addEventListener('input', debounce(() => {
        state.filter.search = $('search').value ?? '';
        state.filter.offset = 0;
        loadAssets();
    }, 260));

    document.querySelectorAll('#asset-table thead th[data-sort]').forEach((th) => {
        th.addEventListener('click', () => {
            const key = th.dataset.sort;
            if (state.filter.sort === key) {
                state.filter.desc = !state.filter.desc;
            } else {
                state.filter.sort = key;
                state.filter.desc = false;
            }
            document.querySelectorAll('#asset-table thead th').forEach((element) => {
                element.classList.remove('is-sorted', 'is-asc');
            });
            th.classList.add('is-sorted');
            th.classList.toggle('is-asc', !state.filter.desc);
            state.filter.offset = 0;
            loadAssets();
        });
    });

    $('btn-pick-output').addEventListener('click', pickOutputFolder);
    $('btn-export').addEventListener('click', startExport);
        bindDetailGutter();
        $('btn-reset').addEventListener('click', doReset);
    $('btn-open-output').addEventListener('click', () => {
        if (state.outputPath) {
            shell.revealInExplorer(state.outputPath);
        }
    });

    $('log-filter').addEventListener('input', debounce(() => {
        state.logFilter = $('log-filter').value ?? '';
        $('log-list').replaceChildren();
        state.logSince = undefined;
        pollLog();
    }, 220));

    $('btn-clear-log').addEventListener('click', () => {
        $('log-list').replaceChildren();
        renderLogEmpty();
    });

    $('btn-about').addEventListener('click', () => {
        const status = state.status;
        toast(status
            ? `AssetRipperPatcher ${status.version} · ${status.webView2 ?? status.shell}`
            : 'AssetRipperPatcher', 'info', 6000);
    });

    const drop = $('drop-zone');
    drop.addEventListener('dragover', (event) => {
        event.preventDefault();
        drop.classList.add('is-over');
    });
    drop.addEventListener('dragleave', () => drop.classList.remove('is-over'));
    drop.addEventListener('drop', (event) => {
        event.preventDefault();
        drop.classList.remove('is-over');
        const files = [...(event.dataTransfer?.files ?? [])];
        const withPath = files.map((file) => file.path).filter(Boolean);
        if (withPath.length) {
            startLoad(withPath);
        } else {
            toast(t('dialog.browserModeNoDialog'), 'error', 6000);
        }
    });
}

// ---------------------------------------------------------------- 启动

async function main() {
    // 文案必须先到位，否则第一帧会闪出 key。
    try {
            const payload = await api.uiText();
            state.languages = payload.languages ?? null;
            i18n.apply(payload);
        } catch (error) {
            console.error('Failed to load UI text', error);
        }
    i18n.refreshDom();
    updateLanguageButton();

    bind();
    await refreshStatus();
    await loadClasses();

    if (state.loaded) {
        setView('browse');
        loadAssets();
    } else {
        setView('welcome');
    }

    startLogPolling();

    try {
        const operation = await api.operation();
        if (operation?.state === 'running') {
            switchToBusy(operation.kind === 'export' ? 'export' : 'import');
        }
    } catch {
        /* 忽略 */
    }

    setInterval(refreshStatus, 15000);
}

main().catch((error) => {
    console.error(error);
    toast(`Init failed: ${error.message}`, 'error', 10000);
});