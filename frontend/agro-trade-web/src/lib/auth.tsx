import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import {
  api,
  clearTokens,
  getAccessToken,
  problemFrom,
  saveTokens,
} from './api'
import type { AuthResponse, AuthUser, LoginRequest, RegisterRequest, UpdateProfileRequest } from '../types/auth'
import { AuthContext } from './auth-context'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [initializing, setInitializing] = useState(true)

  // Restore the session on first load using the persisted access token.
  useEffect(() => {
    let cancelled = false

    async function bootstrap() {
      if (!getAccessToken()) {
        if (!cancelled) setInitializing(false)
        return
      }
      try {
        const { data } = await api.get<AuthUser>('/auth/me')
        if (!cancelled) setUser(data)
      } catch {
        clearTokens()
      } finally {
        if (!cancelled) setInitializing(false)
      }
    }

    void bootstrap()
    return () => {
      cancelled = true
    }
  }, [])

  const login = useCallback(async (payload: LoginRequest) => {
    try {
      const { data } = await api.post<AuthResponse>('/auth/login', payload)
      saveTokens(data)
      setUser(data.user)
      return data
    } catch (error) {
      throw problemFrom(error)
    }
  }, [])

  const register = useCallback(async (payload: RegisterRequest) => {
    try {
      const { data } = await api.post<AuthResponse>('/auth/register', payload)
      saveTokens(data)
      setUser(data.user)
      return data
    } catch (error) {
      throw problemFrom(error)
    }
  }, [])

  const logout = useCallback(async () => {
    const refreshToken = localStorage.getItem('agrotrade.refresh_token')
    if (refreshToken && getAccessToken()) {
      try {
        await api.post('/auth/logout', { refreshToken })
      } catch {
        // Best effort — local session is cleared regardless.
      }
    }
    clearTokens()
    setUser(null)
  }, [])

  const updateProfile = useCallback(async (payload: UpdateProfileRequest) => {
    try {
      const { data } = await api.put<AuthUser>('/me', payload)
      setUser(data)
      return data
    } catch (error) {
      throw problemFrom(error)
    }
  }, [])

  const value = useMemo(
    () => ({ user, initializing, login, register, logout, updateProfile }),
    [user, initializing, login, register, logout, updateProfile],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}