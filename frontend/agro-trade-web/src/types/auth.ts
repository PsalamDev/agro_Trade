// Wire-format mirrors of the API DTOs (plan §03 — Auth & Identity).

export interface AuthUser {
  id: string
  email: string
  displayName: string
  homeCountry: string | null
  defaultCurrency: string
  isEmailVerified: boolean
  isActive: boolean
  roles: string[]
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresInSeconds: number
  user: AuthUser
}

export interface RegisterRequest {
  email: string
  password: string
  displayName: string
  homeCountry?: string
  defaultCurrency?: string
  roles?: string[]
}

export interface LoginRequest {
  email: string
  password: string
}

export interface UpdateProfileRequest {
  displayName?: string
  homeCountry?: string
  defaultCurrency?: string
}

/** Uniform error shape emitted by GlobalExceptionHandler. */
export interface ProblemDetails {
  title?: string
  status?: number
  detail?: string
  code?: string
  fieldErrors?: Record<string, string[]>
}

/** Roles a user may choose at registration (mirror of RoleNames.Registrable, buyer/importer only). */
export const REGISTRABLE_ROLES = ['Buyer', 'Importer', 'Distributor'] as const

/** The single supplier role — held only by the Achievers Global Consult account. */
export const COMPANY_ROLE = 'Company'