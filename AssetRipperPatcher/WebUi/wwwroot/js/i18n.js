// 极简 i18n：文案表由后端 /api/text 一次性下发，
// 前端只负责按 key 取字符串并替换 DOM 上的 data-i18n 占位。
//
// 为什么放后端：设置项的说明文字（每个枚举值的解释）和 API 里的下拉选项
// 必须来自同一份数据，否则会出现「选项名是中文、说明是英文」的错位。

let strings = {};
let currentLanguage = 'zh-Hans';

export const i18n = {
    /** 当前语言代码。 */
    get language() {
        return currentLanguage;
    },

    /** 取文案。缺失时回退到 key 本身，方便定位漏翻的位置。 */
    t(key, fallback) {
        const value = strings[key];
        return value ?? fallback ?? key;
    },

    /** 应用后端下发的文案包。 */
    apply(payload) {
        strings = payload.strings ?? {};
        currentLanguage = payload.language ?? 'zh-Hans';
        document.documentElement.lang = currentLanguage === 'en' ? 'en' : 'zh-CN';
        this.refreshDom();
    },

    /** 把所有带 data-i18n 的元素重新填一遍。 */
    refreshDom(root) {
        const scope = root ?? document;
        for (const element of scope.querySelectorAll('[data-i18n]')) {
            const key = element.dataset.i18n;
            const value = this.t(key);
            if (element.dataset.i18nAttr) {
                element.setAttribute(element.dataset.i18nAttr, value);
            } else {
                element.textContent = value;
            }
        }
        for (const element of scope.querySelectorAll('[data-i18n-placeholder]')) {
            element.placeholder = this.t(element.dataset.i18nPlaceholder);
        }
        for (const element of scope.querySelectorAll('[data-i18n-title]')) {
            element.title = this.t(element.dataset.i18nTitle);
        }
    },
};