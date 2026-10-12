// Wire-format mirrors of the API DTOs (plan §03 — Auth & Identity).

export interface AuthUser {
  id: string
  email: string
  displayName: string
  homeCountry: string | null
  defaultCurrency: string
  /** Buyer account number assigned at registration, e.g. `BUY-ID-AG-000001`; null for non-buyers. */
  buyerId: string | null
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

/** Buyer-side roles — publish RFQs and place orders (mirror of RoleNames.BuyerRoles). */
export const BUYER_ROLES: string[] = ['Buyer', 'Importer', 'Distributor']

/** The role a domestic (buy-local) registrant is granted. */
export const DOMESTIC_BUYER_ROLE = 'Buyer' as const

/** The single supplier role — held only by the Achievers Global Consult account. */
export const COMPANY_ROLE = 'Company'

/** Platform administrator. May publish a listing for any business via the admin path. */
export const ADMIN_ROLE = 'Administrator'