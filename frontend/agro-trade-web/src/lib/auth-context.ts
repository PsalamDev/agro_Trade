import { createContext, useContext } from 'react'
import type { AuthResponse, AuthUser, LoginRequest, RegisterRequest, UpdateProfileRequest } from '../types/auth'

export interface AuthContextValue {
  user: AuthUser | null
  /** True while the persisted session is being restored. */
  initializing: boolean
  login: (payload: LoginRequest) => Promise<AuthResponse>
  register: (payload: RegisterRequest) => Promise<AuthResponse>
  logout: () => Promise<void>
  updateProfile: (payload: UpdateProfileRequest) => Promise<AuthUser>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}