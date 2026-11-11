// 后端 JSON 端点与原生窗口 IPC 的薄封装。
// 只做三件事：拼 URL、解错误、把原生对话框包成 Promise。

const BASE = '';

async function request(path, options) {
    const response = await fetch(BASE + path, options);
    if (!response.ok) {
        // 端点失败时后端可能返回 { error } 或纯文本，两种都尽量透出原因。
        let detail = response.statusText;
        try {
            const text = await response.text();
            if (text) {
                try {
                    const parsed = JSON.parse(text);
                    detail = parsed.error ?? parsed.title ?? text;
                } catch {
                    detail = text;
                }
            }
        } catch {
            /* 读 body 失败就保持 statusText */
        }
        throw new Error(detail || `HTTP ${response.status}`);
    }
    if (response.status === 204) {
        return null;
    }
    const type = response.headers.get('content-type') ?? '';
    return type.includes('application/json') ? response.json() : response.text();
}

const json = (body) => ({
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
});

export const api = {
    status: () => request('/api/status'),
    stats: () => request('/api/stats'),
    classes: () => request('/api/classes'),
    tree: () => request('/api/tree'),

    assets: (params) => {
        const query = new URLSearchParams();
        for (const [key, value] of Object.entries(params ?? {})) {
            if (value !== undefined && value !== null && value !== '') {
                query.set(key, String(value));
            }
        }
        return request(`/api/assets?${query}`);
    },

    detail: (path, pathId) => request(`/api/assets/detail?path=${encodeURIComponent(path)}&pathId=${pathId}`),
    text: (path, pathId) => request(`/api/assets/text?path=${encodeURIComponent(path)}&pathId=${pathId}`),
    assetJson: (path, pathId) => request(`/api/assets/json?path=${encodeURIComponent(path)}&pathId=${pathId}`),
    assetYaml: (path, pathId) => request(`/api/assets/yaml?path=${encodeURIComponent(path)}&pathId=${pathId}`),

    imageUrl: (path, pathId, ext) =>
        `/api/assets/image?path=${encodeURIComponent(path)}&pathId=${pathId}${ext ? `&ext=${ext}` : ''}`,
    audioUrl: (path, pathId) => `/api/assets/audio?path=${encodeURIComponent(path)}&pathId=${pathId}`,
    modelUrl: (path, pathId) => `/api/assets/model?path=${encodeURIComponent(path)}&pathId=${pathId}`,
    fontUrl: (path, pathId) => `/api/assets/font?path=${encodeURIComponent(path)}&pathId=${pathId}`,
    videoUrl: (path, pathId) => `/api/assets/video?path=${encodeURIComponent(path)}&pathId=${pathId}`,

    settings: () => request('/api/settings'),
    saveSettings: (values) => request('/api/settings', json({ values })),

        uiText: () => request('/api/text'),
        setLanguage: (language) => request('/api/language', json({ language })),

        /** 原生目录选择。浏览器/不支持时返回 { unsupported: true }。 */
        pickFolder: (title) => request(`/api/dialog/folder?title=${encodeURIComponent(title ?? '')}`),
        pickFile: (title, multi) =>
            request(`/api/dialog/file?title=${encodeURIComponent(title ?? '')}${multi ? '&multi=true' : ''}`),

    load: (paths) => request('/api/load', json({ paths })),
    exportProject: (outputPath, createSubfolder) =>
        request('/api/export', json({ outputPath, createSubfolder })),
    reset: () => request('/api/reset', { method: 'POST' }),
    operation: () => request('/api/operation'),

    log: (since) =>
        request(since === undefined ? '/api/log?tail=400' : `/api/log?since=${since}&limit=800`),
};

// ---- 原生窗口 IPC ----

const hasNativeHost = () =>
    typeof window.chrome?.webview?.postMessage === 'function' ||
    typeof window.external?.sendMessage === 'function';

/** 当前是否跑在 Photino 原生窗口里（而不是普通浏览器）。 */
export const isNativeShell = hasNativeHost;

let ipcSeq = 0;
const pending = new Map();

window.addEventListener('message', (event) => {
    // Photino 把原生回执作为字符串消息投递过来。
    const payload = typeof event.data === 'string' ? safeParse(event.data) : event.data;
    if (!payload || typeof payload.id !== 'string') {
        return;
    }
    const resolver = pending.get(payload.id);
    if (resolver) {
        pending.delete(payload.id);
        resolver(payload);
    }
});

function safeParse(text) {
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

function post(message) {
    const payload = JSON.stringify(message);
    if (typeof window.chrome?.webview?.postMessage === 'function') {
        window.chrome.webview.postMessage(payload);
    } else if (typeof window.external?.sendMessage === 'function') {
        window.external.sendMessage(payload);
    }
}

/**
 * 请求原生侧执行一个动作。
 * 浏览器模式下没有原生通道，返回 null，调用方退化成普通 input。
 */
function invokeNative(action, args) {
    if (!hasNativeHost()) {
        return Promise.resolve(null);
    }
    const id = `ipc-${++ipcSeq}`;
    return new Promise((resolve) => {
        pending.set(id, resolve);
        post({ id, action, args: args ?? null });
        // 原生侧没有响应（例如对话框被系统吞掉）时别永久挂住。
        setTimeout(() => {
            if (pending.delete(id)) {
                resolve({ id, canceled: true, paths: [] });
            }
        }, 10 * 60 * 1000);
    });
}

export const shell = {
    close: () => invokeNative('window.close'),
    minimize: () => invokeNative('window.minimize'),
    maximize: () => invokeNative('window.maximize'),
    openFolder: () => invokeNative('dialog.openFolder'),
    openFile: () => invokeNative('dialog.openFile'),
    revealInExplorer: (path) => invokeNative('shell.openFolder', [path]),
    openUrl: (url) => invokeNative('shell.openUrl', [url]),
};