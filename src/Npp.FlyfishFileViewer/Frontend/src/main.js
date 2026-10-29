import { mountViewer } from '@file-viewer/web';
import standard from '@file-viewer/preset-standard';
import { pptRenderer } from '@file-viewer/renderer-ppt';
import './style.css';

const params = new URLSearchParams(location.search);
const token = params.get('token');
const filename = params.get('name');
let controller;
function report(state, message = '') {
  const value = JSON.stringify({ token, state, message });
  window.__nppPreviewState = { state, message };
  window.invokeCSharpAction?.(value);
}
async function open() {
  try {
    report('loading');
    const response = await fetch(`/file/${encodeURIComponent(token)}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`文件读取失败 (${response.status})`);
    const file = new File([await response.blob()], filename);
    controller = mountViewer(document.querySelector('#viewer'), {
      file, filename,
      options: { preset: { ...standard, renderers: [...standard.renderers, pptRenderer] }, rendererMode: 'replace', locale: params.get('locale') || 'zh-CN', theme: 'light', search: { enabled: true } },
      onEvent(event) {
        if (event.type === 'load-complete') report('ready');
      },
      onStateChange(state) {
        if (state.error) report('error', state.error.message || '此文件暂不支持预览');
        else if (state.ready) report('ready');
      }
    });
    window.__nppViewer = controller;
  } catch (error) {
    document.querySelector('#viewer').textContent = error.message;
    report('error', error.message);
  }
}
window.addEventListener('pagehide', () => { controller?.destroy(); controller = null; });
open();
