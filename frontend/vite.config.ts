import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Same origin as the page during development too, so the HttpOnly session cookie works without CORS.
    proxy: { '/api': 'http://localhost:5000' },
  },
})
