import axios, { AxiosError } from 'axios'
import type { AuthResponse } from '../types/auth'

const ACCESS_KEY = 'agrotrade.access_token'
const REFRESH_KEY = 'agrotrade.refresh_token'

declare module 'axios' {
  export interface InternalAxiosRequestConfig {
    _retried?: boolean
  }
}

export function saveTokens(res: AuthResponse) {
  localStorage.setItem(ACCESS_KEY, res.accessToken)
  localStorage.setItem(REFRESH_KEY, res.refreshToken)
}

export function clearTokens() {
  localStorage.removeItem(ACCESS_KEY)
  localStorage.removeItem(REFRESH_KEY)
}

export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_KEY)
}

/** Shared API client: attaches the bearer token and transparently refreshes on 401. */
export const api = axios.create({ baseURL: '/api/v1' })

api.interceptors.request.use((config) => {
  const token = getAccessToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

let refreshPromise: Promise<string> | null = null

function refreshAccessToken(): Promise<string> {
  if (!refreshPromise) {
    const refreshToken = localStorage.getItem(REFRESH_KEY)
    refreshPromise = api
      .post<AuthResponse>('/auth/refresh', { refreshToken })
      .then((res) => {
        saveTokens(res.data)
        return res.data.accessToken
      })
      .finally(() => {
        refreshPromise = null
      })
  }
  return refreshPromise
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config
    if (!original || original._retried) return Promise.reject(error)

    const isAuthCall = original.url?.startsWith('/auth/')
    if (error.response?.status === 401 && !isAuthCall) {
      original._retried = true
      try {
        const token = await refreshAccessToken()
        original.headers.Authorization = `Bearer ${token}`
        return api(original)
      } catch (refreshError) {
        clearTokens()
        return Promise.reject(refreshError)
      }
    }

    if (error.response?.status === 401 && original.url === '/auth/refresh') {
      clearTokens()
    }

    return Promise.reject(error)
  },
)

/**
 * Reads the uniform ProblemDetails shape from any axios error so the UI can show
 * a friendly message plus per-field errors.
 */
export function problemFrom(error: unknown) {
  if (axios.isAxiosError<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> }>(error)) {
    return {
      code: error.response?.data?.code ?? 'NETWORK_ERROR',
      detail: error.response?.data?.detail ?? error.message,
      fieldErrors: error.response?.data?.fieldErrors,
    }
  }

  // `unwrap()` throws the parsed shape, so a caller that hands it back in must not have the real
  // message replaced with the generic one. Passing through keeps the server's detail visible.
  if (isProblem(error)) {
    return error
  }

  return { code: 'UNKNOWN', detail: 'Something went wrong.', fieldErrors: undefined }
}

function isProblem(
  value: unknown,
): value is { code: string; detail: string; fieldErrors?: Record<string, string[]> } {
  return typeof value === 'object' && value !== null && typeof (value as { detail?: unknown }).detail === 'string'
}