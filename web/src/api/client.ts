import axios from 'axios'

function resolveBaseUrl() {
  const envUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.trim()
  const raw = envUrl && envUrl.length > 0 ? envUrl : `${window.location.origin}/`
  return raw.endsWith('/') ? raw : `${raw}/`
}

export const apiClient = axios.create({
  baseURL: resolveBaseUrl(),
  timeout: 30000,
  headers: {
    'ngrok-skip-browser-warning': '1',
  },
})

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    const messageFromBody =
      error?.response?.data?.message ||
      error?.response?.data?.title ||
      error?.message ||
      'Ошибка сети. Проверьте VITE_API_BASE_URL и доступность backend.'
    return Promise.reject(new Error(String(messageFromBody)))
  },
)
