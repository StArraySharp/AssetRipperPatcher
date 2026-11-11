// Fluent 2 组件库入口。esbuild 把整棵树打成一个 ESM 文件，
// 运行时不需要任何 CDN 或 node_modules —— 离线桌面应用必须自包含。
import '@fluentui/web-components/web-components.js';
import { setTheme } from '@fluentui/web-components';
import { webDarkTheme, webLightTheme } from '@fluentui/tokens';

setTheme(webDarkTheme);

export { setTheme, webDarkTheme, webLightTheme };
