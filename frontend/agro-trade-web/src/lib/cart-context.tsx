import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { getMyCart } from './trade'
import { useAuth } from './auth-context'
import { BUYER_ROLES } from '../types/auth'

export interface CartContextValue {
  /** Sum of line quantities — the figure the header badge shows. */
  totalQuantity: number
  /** Re-reads the cart from the server (after add / remove / checkout / login). */
  refresh: () => Promise<void>
}

const CartContext = createContext<CartContextValue | null>(null)

function isBuyerRoles(roles: string[] | undefined): boolean {
  return !!roles?.some((role) => BUYER_ROLES.includes(role))
}

export function CartProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const [totalQuantity, setTotalQuantity] = useState(0)

  const refresh = useCallback(async () => {
    // Company-only users have no cart; the Buyer policy would reject the call anyway.
    if (!user || !isBuyerRoles(user.roles)) {
      setTotalQuantity(0)
      return
    }
    try {
      const cart = await getMyCart()
      setTotalQuantity(cart.totalQuantity)
    } catch {
      // Signed-in but unreadable (e.g. no buyer role yet) — show an empty cart rather than an error.
      setTotalQuantity(0)
    }
  }, [user])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const value = useMemo(() => ({ totalQuantity, refresh }), [totalQuantity, refresh])

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCart(): CartContextValue {
  const ctx = useContext(CartContext)
  if (!ctx) throw new Error('useCart must be used inside <CartProvider>')
  return ctx
}