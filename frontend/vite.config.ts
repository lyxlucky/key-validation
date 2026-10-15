import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1', port: 5173, strictPort: true,
    proxy: {
      '/api': process.env.API_PROXY_TARGET ?? 'http://127.0.0.1:5080',
      '^/[mM][cC][pP](?:/|$)': {
        target: process.env.MCP_PROXY_TARGET ?? process.env.API_PROXY_TARGET ?? 'http://127.0.0.1:5080',
        changeOrigin: false,
        configure(proxy) {
          proxy.on('proxyReq', (proxyRequest, request) => {
            proxyRequest.setHeader('X-Forwarded-For', request.socket.remoteAddress ?? '127.0.0.1');
            proxyRequest.setHeader('X-Forwarded-Proto', 'http');
            proxyRequest.removeHeader('X-Forwarded-Host');
            proxyRequest.removeHeader('X-KeyGate-Country');
          });
          proxy.on('proxyRes', (response) => {
            response.headers['cache-control'] = 'no-store';
          });
        },
      },
    },
  },
  build: { chunkSizeWarningLimit: 1600 },
});
