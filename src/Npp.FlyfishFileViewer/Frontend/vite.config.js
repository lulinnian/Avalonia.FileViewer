import { defineConfig } from 'vite';
import { fileViewerRenderers } from '@file-viewer/vite-plugin';

export default defineConfig({
  base: './',
  plugins: [fileViewerRenderers({ preset: 'standard', formats: ['ppt'], copyAssets: true })],
  build: {
    outDir: '../Web',
    emptyOutDir: true,
    rollupOptions: {
      // The official plugin copies the complete runtime to vendor/ppt. Load that
      // copy from the renderer chunk instead of bundling its font/WASM again.
      external: ['@file-viewer/ppt'],
      output: { paths: { '@file-viewer/ppt': '../vendor/ppt/index.mjs' } }
    }
  }
});
