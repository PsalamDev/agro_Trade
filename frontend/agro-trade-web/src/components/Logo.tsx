import type { HTMLAttributes } from 'react'

export interface LogoProps extends HTMLAttributes<HTMLDivElement> {
  /**
   * Logo variant to display. `panel` is the horizontal lockup re-tinted to the
   * brand-800 auth panel so its backdrop blends seamlessly.
   */
  variant?: 'horizontal' | 'stacked' | 'icon' | 'panel'
  /** Size class for height */
  size?: 'xs' | 'sm' | 'md' | 'lg' | 'xl'
}

const SIZE_CLASSES = {
  xs: 'h-5 w-auto max-w-[140px]',
  sm: 'h-7 w-auto max-w-[180px]',
  md: 'h-8 sm:h-9 w-auto max-w-[200px]',
  lg: 'h-10 sm:h-12 w-auto max-w-[260px]',
  /** Width-driven: the horizontal lockup is 1.87:1, so height-based sizing renders it tiny. */
  xl: 'h-auto w-full max-w-[26rem]',
}

const LOGO_SOURCES = {
  horizontal: '/Horizontal _ dark.png',
  stacked: '/Stacked.png',
  icon: '/App _ social icon (1).png',
  panel: '/Horizontal _ panel.png',
} as const

const ALT_TEXT = {
  horizontal: 'AgroTrade',
  stacked: 'AgroTrade',
  icon: 'AgroTrade icon',
  panel: 'AgroTrade',
} as const

/**
 * Branded logo component. Uses the uploaded assets in public/.
 * The horizontal lockup is designed for dark backgrounds (e.g. the auth
 * panel); the icon and stacked variants work on any surface.
 */
export function Logo({ variant = 'horizontal', size = 'md', className = '', ...props }: LogoProps) {
  return (
    <div className={className} {...props}>
      <img
        src={LOGO_SOURCES[variant]}
        alt={ALT_TEXT[variant]}
        className={SIZE_CLASSES[size]}
      />
    </div>
  )
}
